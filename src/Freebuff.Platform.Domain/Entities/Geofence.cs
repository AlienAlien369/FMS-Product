using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Geofence entity supporting circle, rectangle, and polygon shapes.
/// </summary>
public class Geofence : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GeofenceType Type { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    // Shape data
    public string Coordinates { get; set; } = string.Empty; // JSON (legacy ring/rect data)
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; } // For circle type

    /// <summary>
    /// Canonical geometry, GeoJSON:
    ///   {"type":"circle",  "center":[lng,lat], "radiusMeters":number}
    ///   {"type":"polygon", "coordinates":[[lng,lat], ...]}
    /// Null only for legacy rows predating the geometry column (circles stored
    /// in the flat CenterLatitude/CenterLongitude/Radius fields). New code
    /// paths branch on this one field — never on which flat columns are set.
    /// </summary>
    public string? Geometry { get; set; }

    // Style
    public string? FillColor { get; set; }
    public string? BorderColor { get; set; }
    public int? BorderWidth { get; set; }

    // Violation tracking
    public int ViolationCount { get; set; }
    public DateTime? LastViolationAt { get; set; }

    // Company
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Navigation
    public ICollection<VehicleGeofence> VehicleGeofences { get; set; } = new List<VehicleGeofence>();
}
