using System.Text.Json;
using Freebuff.Platform.Domain.Entities;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Pure containment math shared by every geofence consumer (trip zone events,
/// breach checks). Branches on the canonical GeoJSON geometry only; legacy
/// circle rows (flat Center*/Radius fields, no Geometry) fall back to the flat
/// fields. Boundary counts as inside.
/// </summary>
public static class GeofenceContainment
{
    /// <summary>True when the point is on or inside the geofence's shape.</summary>
    public static bool IsInside(Geofence geofence, double latitude, double longitude)
    {
        if (geofence.Geometry != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(geofence.Geometry);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "circle"
                    && root.TryGetProperty("center", out var center) && center.ValueKind == JsonValueKind.Array && center.GetArrayLength() >= 2
                    && root.TryGetProperty("radiusMeters", out var radius) && radius.ValueKind == JsonValueKind.Number)
                {
                    var cLng = center[0].GetDouble();
                    var cLat = center[1].GetDouble();
                    return HaversineM(cLat, cLng, latitude, longitude) <= radius.GetDouble();
                }
                if (type == "polygon" && root.TryGetProperty("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Array)
                {
                    var ring = new List<(double Lat, double Lng)>();
                    foreach (var c in coords.EnumerateArray())
                    {
                        if (c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 2)
                            ring.Add((c[1].GetDouble(), c[0].GetDouble()));
                    }
                    return PointInPolygon(latitude, longitude, ring);
                }
            }
            catch (JsonException)
            {
                // Malformed geometry — fall through to the legacy flat fields.
            }
        }

        // Legacy circle rows predating the geometry column.
        if (geofence.CenterLatitude.HasValue && geofence.CenterLongitude.HasValue && geofence.Radius.HasValue)
            return HaversineM(geofence.CenterLatitude.Value, geofence.CenterLongitude.Value, latitude, longitude) <= geofence.Radius.Value;

        return false;
    }

    private static bool PointInPolygon(double lat, double lng, List<(double Lat, double Lng)> ring)
    {
        if (ring.Count < 3) return false;
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (latI, lngI) = ring[i];
            var (latJ, lngJ) = ring[j];
            if ((latI > lat) != (latJ > lat)
                && lng < (lngJ - lngI) * (lat - latI) / (latJ - latI) + lngI)
                inside = !inside;
        }
        return inside;
    }

    private static double HaversineM(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371000.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}