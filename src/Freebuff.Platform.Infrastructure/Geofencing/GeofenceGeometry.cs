using System.Text.Json;

namespace Freebuff.Platform.Infrastructure.Geofencing;

/// <summary>
/// Canonical geofence geometry — GeoJSON — with validation and containment.
///
/// Storage shape (the ONLY two forms ever produced/stored):
///   circle:  {"type":"circle",  "center":[lng,lat], "radiusMeters":<meters>}
///   polygon: {"type":"polygon", "coordinates":[[lng,lat], ...]}   (closed ring implied)
///
/// Every consumer of geofence data branches on geometry.Type — never on which
/// flat columns are populated.
///
/// Constraints (enforced here so draw-time checks, form saves, bulk import and
/// any future alert engine share ONE source of truth):
///   - circle radius: [MinRadiusMeters, MaxRadiusMeters]
///   - polygon: ≥ 3 distinct vertices, no self-intersection (live-checkable),
///     bounded area (area-equivalent of the max circle radius).
/// </summary>
public abstract record GeofenceGeometry
{
    public const double MinRadiusMeters = 10;     // smaller than this isn't operationally meaningful
    public const double MaxRadiusMeters = 50_000; // 50 km — beyond that it's a region, not a geofence
    public const double MaxPolygonAreaKm2 = 8_000; // π·50km² ≈ 7,854 km² — same reasoning as the radius cap

    public const double MinLat = -90, MaxLat = 90, MinLng = -180, MaxLng = 180;
    private const double Epsilon = 1e-9;

    public sealed record Circle(double CenterLng, double CenterLat, double RadiusMeters) : GeofenceGeometry
    {
        public double[] Center => new[] { CenterLng, CenterLat };
    }

    /// <summary>Vertices are [lng, lat] pairs; the ring is treated as closed.</summary>
    public sealed record Polygon(IReadOnlyList<(double Lng, double Lat)> Vertices) : GeofenceGeometry
    {
        public int PointCount => Vertices.Count;
        public double[][] Coordinates => Vertices.Select(v => new[] { v.Lng, v.Lat }).ToArray();
    }

    // ── Parse ────────────────────────────────────────────────────────────────

    /// <summary>Parses stored/client GeoJSON. Returns null (with reason) when the JSON is not a supported shape.</summary>
    public static GeofenceGeometry? TryParse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { error = "Geometry is not valid GeoJSON."; return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Geometry must be a GeoJSON object.";
                return null;
            }
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "circle":
                {
                    if (!TryReadLngLat(root, "center", out var lng, out var lat, out error))
                        return null;
                    if (!root.TryGetProperty("radiusMeters", out var r) || r.ValueKind != JsonValueKind.Number)
                    {
                        error = "Circle geometry requires a numeric radiusMeters.";
                        return null;
                    }
                    return new Circle(lng, lat, r.GetDouble());
                }
                case "polygon":
                {
                    if (!root.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array)
                    {
                        error = "Polygon geometry requires a coordinates array.";
                        return null;
                    }
                    var verts = new List<(double, double)>();
                    foreach (var c in coords.EnumerateArray())
                    {
                        if (!TryReadLngLatValue(c, out var clng, out var clat, out error))
                            return null;
                        verts.Add((clng, clat));
                    }
                    return new Polygon(verts);
                }
                default:
                    error = "Unsupported geometry type. Use \"circle\" or \"polygon\".";
                    return null;
            }
        }
    }

    /// <summary>Serializes a geometry back to its canonical GeoJSON string.</summary>
    public static string ToGeoJson(GeofenceGeometry g)
    {
        var props = new Dictionary<string, object?>
        {
            ["type"] = g is Circle ? "circle" : "polygon"
        };
        if (g is Circle c)
        {
            props["center"] = new[] { c.CenterLng, c.CenterLat };
            props["radiusMeters"] = c.RadiusMeters;
        }
        else if (g is Polygon p)
        {
            props["coordinates"] = p.Vertices.Select(v => new[] { v.Lng, v.Lat }).ToArray();
        }
        return JsonSerializer.Serialize(props);
    }

    private static bool TryReadLngLat(JsonElement root, string prop, out double lng, out double lat, out string? error)
    {
        error = null;
        if (!root.TryGetProperty(prop, out var el))
        {
            error = $"Circle geometry requires a \"{prop}\" [lng, lat] array.";
            lng = lat = 0;
            return false;
        }
        return TryReadLngLatValue(el, out lng, out lat, out error);
    }

    private static bool TryReadLngLatValue(JsonElement el, out double lng, out double lat, out string? error)
    {
        error = null;
        lng = lat = 0;
        if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() < 2
            || el[0].ValueKind != JsonValueKind.Number || el[1].ValueKind != JsonValueKind.Number)
        {
            error = "Positions must be [lng, lat] number pairs.";
            return false;
        }
        lng = el[0].GetDouble();
        lat = el[1].GetDouble();
        return true;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    /// <summary>Full validation; returns null when the geometry is acceptable to save.</summary>
    public string? Validate()
    {
        switch (this)
        {
            case Circle c:
                if (c.CenterLat is < MinLat or > MaxLat || c.CenterLng is < MinLng or > MaxLng)
                    return "Circle center is outside valid coordinates.";
                if (double.IsNaN(c.RadiusMeters) || c.RadiusMeters < MinRadiusMeters)
                    return $"Radius must be at least {MinRadiusMeters:N0} m.";
                if (c.RadiusMeters > MaxRadiusMeters)
                    return $"Radius cannot exceed {MaxRadiusMeters / 1000:N0} km — that is a region, not a geofence.";
                return null;

            case Polygon p:
                if (p.Vertices.Count < 3)
                    return "A polygon needs at least 3 points.";
                if (DistinctCount(p.Vertices) < 3)
                    return "A polygon needs at least 3 distinct points.";
                for (var i = 0; i < p.Vertices.Count; i++)
                {
                    var (lng, lat) = p.Vertices[i];
                    if (lat is < MinLat or > MaxLat || lng is < MinLng or > MaxLng)
                        return "Polygon contains a point outside valid coordinates.";
                }
                for (var i = 0; i < p.Vertices.Count; i++)
                {
                    if (p.Vertices[i] == p.Vertices[(i + 1) % p.Vertices.Count])
                        return "Polygon has duplicate consecutive points.";
                }
                if (IsSelfIntersecting(p))
                    return "This shape crosses itself — adjust the points.";
                var area = PolygonAreaKm2(p.Vertices);
                if (area > MaxPolygonAreaKm2)
                    return $"Polygon area is too large ({area:N1} km²) — max is {MaxPolygonAreaKm2:N0} km².";
                return null;
            default:
                return "Unsupported geometry.";
        }
    }

    /// <summary>Counts distinct vertices (for the ≥3-distinct rule).</summary>
    private static int DistinctCount(IReadOnlyList<(double Lng, double Lat)> verts)
    {
        var set = new HashSet<(double Lng, double Lat)>();
        foreach (var v in verts) set.Add(v);
        return set.Count;
    }

    // ── Self-intersection (draw-time and save-time) ───────────────────────────

    /// <summary>
    /// True when any two non-adjacent edges properly cross or overlap
    /// collinearly. Adjacent edges (sharing a vertex) are excluded — that is
    /// the normal polygon closure. Self-intersection breaks point-in-polygon
    /// math silently, so it is rejected live on every vertex drag.
    /// </summary>
    public static bool IsSelfIntersecting(Polygon p)
    {
        var v = p.Vertices;
        var n = v.Count;
        for (var i = 0; i < n; i++)
        {
            var a1 = v[i];
            var a2 = v[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                // Skip edges that share an endpoint (adjacent or the closing pair).
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                var b1 = v[j];
                var b2 = v[(j + 1) % n];
                if (SegmentsIntersect(a1, a2, b1, b2)) return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect((double Lng, double Lat) a, (double Lng, double Lat) b,
        (double Lng, double Lat) c, (double Lng, double Lat) d)
    {
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);

        if (((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon))
            && ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon)))
            return true; // proper crossing

        // Collinear touch/overlap of a vertex with the other segment counts as
        // a crossing — a figure-eight or boundary retrace must be rejected.
        if (Math.Abs(d1) <= Epsilon && OnSegment(c, d, a)) return true;
        if (Math.Abs(d2) <= Epsilon && OnSegment(c, d, b)) return true;
        if (Math.Abs(d3) <= Epsilon && OnSegment(a, b, c)) return true;
        if (Math.Abs(d4) <= Epsilon && OnSegment(a, b, d)) return true;
        return false;
    }

    private static double Cross((double Lng, double Lat) o, (double Lng, double Lat) a, (double Lng, double Lat) b)
        => (a.Lng - o.Lng) * (b.Lat - o.Lat) - (a.Lat - o.Lat) * (b.Lng - o.Lng);

    private static bool OnSegment((double Lng, double Lat) a, (double Lng, double Lat) b, (double Lng, double Lat) p)
        => p.Lng >= Math.Min(a.Lng, b.Lng) - Epsilon && p.Lng <= Math.Max(a.Lng, b.Lng) + Epsilon
        && p.Lat >= Math.Min(a.Lat, b.Lat) - Epsilon && p.Lat <= Math.Max(a.Lat, b.Lat) + Epsilon;

    // ── Area (for the polygon cap; equirectangular approx is plenty for a sanity bound) ──

    public static double PolygonAreaKm2(IReadOnlyList<(double Lng, double Lat)> verts)
    {
        var n = verts.Count;
        if (n < 3) return 0;
        var meanLat = verts.Average(v => v.Lat) * Math.PI / 180;
        var lngScale = 111.32 * Math.Cos(meanLat); // km per degree of longitude
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            var (lng1, lat1) = verts[i];
            var (lng2, lat2) = verts[(i + 1) % n];
            sum += (lng1 * lngScale) * (lat2 * 111.32) - (lng2 * lngScale) * (lat1 * 111.32);
        }
        return Math.Abs(sum / 2);
    }

    // ── Containment ───────────────────────────────────────────────────────────

    /// <summary>
    /// Point-in-geofence. Circle: haversine distance from center ≤ radius.
    /// Polygon: ray casting with explicit on-boundary handling (a point exactly
    /// on an edge counts as inside — the battle-tested convention for alerts).
    /// </summary>
    public static bool Contains(GeofenceGeometry g, double lat, double lng)
    {
        switch (g)
        {
            case Circle c:
                return HaversineMeters(c.CenterLat, c.CenterLng, lat, lng) <= c.RadiusMeters;
            case Polygon p:
                return PointInPolygon(p.Vertices, lat, lng);
            default:
                return false;
        }
    }

    public static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371000.0;
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var dPhi = (lat2 - lat1) * Math.PI / 180;
        var dLambda = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2)
                + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    public static bool PointInPolygon(IReadOnlyList<(double Lng, double Lat)> verts, double lat, double lng)
    {
        var n = verts.Count;
        if (n < 3) return false;
        // On-boundary first (ray casting is ambiguous for points on edges).
        for (var i = 0; i < n; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % n];
            if (PointOnSegment(a, b, (lng, lat))) return true;
        }
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var vi = verts[i];
            var vj = verts[j];
            var crosses = (vi.Lat > lat) != (vj.Lat > lat)
                          && lng < (vj.Lng - vi.Lng) * (lat - vi.Lat) / (vj.Lat - vi.Lat) + vi.Lng;
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private static bool PointOnSegment((double Lng, double Lat) a, (double Lng, double Lat) b, (double Lng, double Lat) p)
        => Math.Abs(Cross(a, b, p)) <= Epsilon && OnSegment(a, b, p);

    /// <summary>Whether a geometry needs to persist its flat circle fields (legacy consumers).</summary>
    public static bool IsCircle(GeofenceGeometry g) => g is Circle;
}