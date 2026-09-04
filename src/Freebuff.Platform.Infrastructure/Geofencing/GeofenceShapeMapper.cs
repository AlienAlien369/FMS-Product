using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using System.Text.Json;

namespace Freebuff.Platform.Infrastructure.Geofencing;

/// <summary>
/// Maps between the canonical GeoJSON geometry and the Geofence entity.
/// The Geometry column is the source of truth for shape; the legacy flat
/// circle columns (CenterLatitude/Longitude/Radius) are kept in sync for
/// circles only, so existing list/export consumers keep working. Polygon
/// records clear those columns — no code path may assume "all geofences are
/// circles" from flat fields alone.
/// </summary>
public static class GeofenceShapeMapper
{
    /// <summary>
    /// Applies a client-supplied GeoJSON geometry to an entity (create or
    /// update). Returns null on success, or a user-facing validation error.
    /// </summary>
    public static string? ApplyGeometry(Geofence g, string? geometryJson)
    {
        if (string.IsNullOrWhiteSpace(geometryJson))
            return "Geometry is required.";

        var geom = GeofenceGeometry.TryParse(geometryJson, out var parseError);
        if (geom == null) return parseError ?? "Invalid geometry.";
        var validationError = geom.Validate();
        if (validationError != null) return validationError;

        if (geom is GeofenceGeometry.Circle c)
        {
            g.Type = GeofenceType.Circle;
            g.CenterLatitude = c.CenterLat;
            g.CenterLongitude = c.CenterLng;
            g.Radius = c.RadiusMeters;
            g.Coordinates = "[]";
        }
        else if (geom is GeofenceGeometry.Polygon p)
        {
            g.Type = GeofenceType.Polygon;
            g.CenterLatitude = null;
            g.CenterLongitude = null;
            g.Radius = null;
            // Keep the legacy ring JSON in sync for any legacy consumer.
            g.Coordinates = JsonSerializer.Serialize(p.Vertices.Select(v => new[] { v.Lng, v.Lat }).ToArray());
        }

        g.Geometry = GeofenceGeometry.ToGeoJson(geom);
        return null;
    }

    /// <summary>
    /// Builds a canonical circle entity from the legacy flat fields (the old
    /// radius-based form flow). Returns null on success, or an error.
    /// </summary>
    public static string? ApplyLegacyCircle(Geofence g, double? centerLat, double? centerLng, double? radius)
    {
        if (!centerLat.HasValue || !centerLng.HasValue || !radius.HasValue)
            return "Circle geofences need a center (latitude/longitude) and a radius.";
        var circle = new GeofenceGeometry.Circle(centerLng.Value, centerLat.Value, radius.Value);
        var error = circle.Validate();
        if (error != null) return error;
        return ApplyGeometry(g, GeofenceGeometry.ToGeoJson(circle));
    }

    /// <summary>
    /// Derives the canonical geometry of a persisted entity. Geometry column
    /// wins; pre-migration rows fall back to their flat circle fields, then to
    /// a best-effort polygon parse of the legacy Coordinates ring. Returns null
    /// when nothing resolvable is present.
    /// </summary>
    public static GeofenceGeometry? FromEntity(Geofence g)
    {
        var parsed = GeofenceGeometry.TryParse(g.Geometry, out _);
        if (parsed != null) return parsed;
        if (g.Type == GeofenceType.Circle && g.CenterLatitude.HasValue && g.CenterLongitude.HasValue && g.Radius.HasValue)
            return new GeofenceGeometry.Circle(g.CenterLongitude.Value, g.CenterLatitude.Value, g.Radius.Value);
        if (!string.IsNullOrWhiteSpace(g.Coordinates) && g.Coordinates != "[]")
        {
            try
            {
                var arr = JsonSerializer.Deserialize<double[][]>(g.Coordinates);
                if (arr is { Length: >= 3 })
                    return new GeofenceGeometry.Polygon(arr.Select(a => (a[0], a[1])).ToList());
            }
            catch (JsonException) { /* not a ring — treat as unresolvable */ }
        }
        return null;
    }
}