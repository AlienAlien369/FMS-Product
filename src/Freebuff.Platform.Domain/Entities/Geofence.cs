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
    public string Coordinates { get; set; } = string.Empty; // JSON
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; } // For circle type

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
