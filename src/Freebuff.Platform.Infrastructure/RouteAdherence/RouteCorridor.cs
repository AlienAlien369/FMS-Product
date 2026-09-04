using System.Text.Json;

namespace Freebuff.Platform.Infrastructure.RouteAdherence;

/// <summary>
/// Parsers for the two route path representations a Route stores:
///   Waypoints     — ordered [{name, lat, lng, ...}, ...] (what the user edits)
///   RouteGeometry — GeoJSON LineString {"type":"LineString","coordinates":[[lng,lat],...]}
///                   (what distance/ETA/deviation math runs against).
/// Both parse to the same internal (lng, lat) point list so corridor logic and
/// any future engine only ever consume one shape.
/// </summary>
public static class RoutePath
{
    public static List<(double Lng, double Lat)> ParseLineString(string? geojson)
    {
        var pts = new List<(double, double)>();
        if (string.IsNullOrWhiteSpace(geojson)) return pts;
        try
        {
            using var doc = JsonDocument.Parse(geojson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return pts;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "LineString") return pts;
            if (!root.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array) return pts;
            foreach (var c in coords.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2) continue;
                var lng = c[0].GetDouble();
                var lat = c[1].GetDouble();
                pts.Add((lng, lat));
            }
        }
        catch (JsonException) { /* not a linestring — empty path */ }
        return pts;
    }

    public static List<(double Lng, double Lat)> FromWaypoints(string? waypointsJson)
    {
        var pts = new List<(double, double)>();
        if (string.IsNullOrWhiteSpace(waypointsJson)) return pts;
        try
        {
            using var doc = JsonDocument.Parse(waypointsJson);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement : default;
            if (arr.ValueKind != JsonValueKind.Array) return pts;
            foreach (var w in arr.EnumerateArray())
            {
                var okLat = w.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number;
                var okLng = w.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number;
                if (!okLat || !okLng) continue;
                pts.Add((lngEl.GetDouble(), latEl.GetDouble()));
            }
        }
        catch (JsonException) { /* not a waypoint list — empty */ }
        return pts;
    }
}

/// <summary>
/// Corridor deviation primitive: shortest planar distance from a live position
/// to a route path, plus the "outside corridor" test. Planar (equirectangular)
/// projection is used — at corridor scales (≤ tens of km) the error versus a
/// great-circle distance is far below the buffer threshold this sanity check
/// needs. Evaluation against a telemetry stream is the caller's job; this is
/// the geometry the caller must use so every route uses one consistent math.
/// </summary>
public static class RouteCorridor
{
    public const double MetersPerDegree = 111_320.0;

    /// <summary>Shortest distance (metres) from a point to a route path.</summary>
    public static double DistanceMetersToPath(double lat, double lng, IReadOnlyList<(double Lng, double Lat)> path)
    {
        if (path.Count == 0) return double.PositiveInfinity;
        if (path.Count == 1) return PointDistanceMeters(lat, lng, path[0].Lng, path[0].Lat, lat);
        var min = double.MaxValue;
        for (var i = 0; i < path.Count - 1; i++)
            min = Math.Min(min, SegmentDistanceMeters(lat, lng, path[i], path[i + 1]));
        return min;
    }

    public static bool IsOutsideCorridor(double lat, double lng, IReadOnlyList<(double Lng, double Lat)> path, double bufferMeters)
        => DistanceMetersToPath(lat, lng, path) > bufferMeters;

    /// <summary>Distance from point to segment a→b, planar with lat/lng scaled at the test point's latitude.</summary>
    public static double SegmentDistanceMeters(double lat, double lng, (double Lng, double Lat) a, (double Lng, double Lat) b)
    {
        var scale = Math.Cos(lat * Math.PI / 180);
        var ax = a.Lng * scale * MetersPerDegree;
        var ay = a.Lat * MetersPerDegree;
        var bx = b.Lng * scale * MetersPerDegree;
        var by = b.Lat * MetersPerDegree;
        var px = lng * scale * MetersPerDegree;
        var py = lat * MetersPerDegree;

        var abx = bx - ax;
        var aby = by - ay;
        var len2 = abx * abx + aby * aby;
        var t = len2 < 1e-12 ? 0.0 : Math.Clamp(((px - ax) * abx + (py - ay) * aby) / len2, 0.0, 1.0);
        var dx = px - (ax + t * abx);
        var dy = py - (ay + t * aby);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double PointDistanceMeters(double lat1, double lng1, double lng2, double lat2, double refLat)
    {
        var scale = Math.Cos(refLat * Math.PI / 180);
        var dx = (lng2 - lng1) * scale * MetersPerDegree;
        var dy = (lat2 - lat1) * MetersPerDegree;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
