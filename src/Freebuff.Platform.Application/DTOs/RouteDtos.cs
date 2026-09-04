using System.ComponentModel.DataAnnotations;

namespace Freebuff.Platform.Application.DTOs;

public class RouteDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public bool IsOptimized { get; set; }
    public bool IsTemplate { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    // Origin & Destination
    public string OriginName { get; set; } = string.Empty;
    public double OriginLatitude { get; set; }
    public double OriginLongitude { get; set; }
    public string? DestinationName { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    // Waypoints & Geometry
    public string? Waypoints { get; set; }
    public string? RouteGeometry { get; set; }

    // Metrics
    public decimal? TotalDistance { get; set; }
    public string? DistanceUnit { get; set; }
    public TimeSpan? EstimatedDuration { get; set; }
    public decimal? EstimatedFuelCost { get; set; }
    public decimal? EstimatedTollCost { get; set; }
    public string? Currency { get; set; }
    public int? TrafficLevel { get; set; }

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxVehicles { get; set; }
    public int? Priority { get; set; }

    // Scheduling
    public string? RecurrenceRule { get; set; }
    public int? DayOfWeek { get; set; }
    public TimeSpan? PreferredStartTime { get; set; }

    // Assignments
    public int AssignedVehicleCount { get; set; }
    public int CompletedTripCount { get; set; }

    // Geofence linkage summary (counts on every row; full rows on detail)
    public int GeofenceCount { get; set; }
    public int CheckpointCount { get; set; }
    public int RestrictedZoneCount { get; set; }
    public int BoundaryZoneCount { get; set; }
    public List<RouteGeofenceDto>? RouteGeofences { get; set; }

    // Path + corridor configuration
    public int PathSource { get; set; }
    public string PathSourceName { get; set; } = string.Empty;
    public bool CorridorEnabled { get; set; }
    public double? CorridorBufferMeters { get; set; }
    public int? DeviationThresholdMinutes { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class RouteGeofenceDto
{
    public Guid Id { get; set; }
    public Guid RouteId { get; set; }
    public Guid GeofenceId { get; set; }
    public string GeofenceName { get; set; } = string.Empty;
    public int GeofenceType { get; set; }
    public string GeofenceTypeName { get; set; } = string.Empty;
    public int Role { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int? SequenceOrder { get; set; }
}

/// <summary>A single link entry for the replace-all route geofence endpoint.</summary>
public class RouteGeofenceLinkDto
{
    public Guid GeofenceId { get; set; }
    public int Role { get; set; }
    public int? SequenceOrder { get; set; }
}

public class CreateRouteDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, 4)]
    public int Type { get; set; }

    public bool IsTemplate { get; set; }

    // Origin & Destination
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string OriginName { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double OriginLatitude { get; set; }

    [Range(-180, 180)]
    public double OriginLongitude { get; set; }

    [StringLength(200)]
    public string? DestinationName { get; set; }

    [Range(-90, 90)]
    public double? DestinationLatitude { get; set; }

    [Range(-180, 180)]
    public double? DestinationLongitude { get; set; }

    // Waypoints & Geometry
    [StringLength(10000)]
    public string? Waypoints { get; set; }

    [StringLength(50000)]
    public string? RouteGeometry { get; set; }

    // Metrics
    [Range(0, 999999.99)]
    public decimal? TotalDistance { get; set; }

    [StringLength(5)]
    public string? DistanceUnit { get; set; } = "km";

    public TimeSpan? EstimatedDuration { get; set; }

    [Range(0, 999999.99)]
    public decimal? EstimatedFuelCost { get; set; }

    [Range(0, 999999.99)]
    public decimal? EstimatedTollCost { get; set; }

    [StringLength(3)]
    public string? Currency { get; set; } = "USD";

    [Range(0, 100)]
    public int? TrafficLevel { get; set; }

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    [Range(1, 10000)]
    public int? MaxVehicles { get; set; }

    [Range(1, 5)]
    public int? Priority { get; set; }

    // Scheduling
    [StringLength(200)]
    public string? RecurrenceRule { get; set; }

    [Range(0, 6)]
    public int? DayOfWeek { get; set; }

    public TimeSpan? PreferredStartTime { get; set; }

    // Path + corridor configuration
    [Range(0, 1)]
    public int? PathSource { get; set; }

    public bool? CorridorEnabled { get; set; }

    [Range(50, 10000)]
    public double? CorridorBufferMeters { get; set; }

    [Range(1, 60)]
    public int? DeviationThresholdMinutes { get; set; }
}

public class UpdateRouteDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, 5)]
    public int? Status { get; set; }

    [Range(0, 4)]
    public int? Type { get; set; }

    public bool? IsOptimized { get; set; }
    public bool? IsTemplate { get; set; }

    // Origin & Destination
    [StringLength(200, MinimumLength = 1)]
    public string? OriginName { get; set; }

    [Range(-90, 90)]
    public double? OriginLatitude { get; set; }

    [Range(-180, 180)]
    public double? OriginLongitude { get; set; }

    [StringLength(200)]
    public string? DestinationName { get; set; }

    [Range(-90, 90)]
    public double? DestinationLatitude { get; set; }

    [Range(-180, 180)]
    public double? DestinationLongitude { get; set; }

    // Waypoints & Geometry
    [StringLength(10000)]
    public string? Waypoints { get; set; }

    [StringLength(50000)]
    public string? RouteGeometry { get; set; }

    // Metrics
    [Range(0, 999999.99)]
    public decimal? TotalDistance { get; set; }

    public TimeSpan? EstimatedDuration { get; set; }

    [Range(0, 999999.99)]
    public decimal? EstimatedFuelCost { get; set; }

    [Range(0, 999999.99)]
    public decimal? EstimatedTollCost { get; set; }

    [Range(0, 100)]
    public int? TrafficLevel { get; set; }

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    [Range(1, 10000)]
    public int? MaxVehicles { get; set; }

    [Range(1, 5)]
    public int? Priority { get; set; }

    // Scheduling
    [StringLength(200)]
    public string? RecurrenceRule { get; set; }

    [Range(0, 6)]
    public int? DayOfWeek { get; set; }

    public TimeSpan? PreferredStartTime { get; set; }

    // Path + corridor configuration
    [Range(0, 1)]
    public int? PathSource { get; set; }

    public bool? CorridorEnabled { get; set; }

    [Range(50, 10000)]
    public double? CorridorBufferMeters { get; set; }

    [Range(1, 60)]
    public int? DeviationThresholdMinutes { get; set; }
}

public class GeofenceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Status { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    // Shape data — canonical GeoJSON geometry ("circle" | "polygon").
    public string? Geometry { get; set; }
    // Legacy flat fields (kept in sync for circles for backward compatibility).
    public string Coordinates { get; set; } = string.Empty;
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; }

    // Style
    public string? FillColor { get; set; }
    public string? BorderColor { get; set; }
    public int? BorderWidth { get; set; }

    // Alerts
    public bool AlertOnEntry { get; set; } = true;
    public bool AlertOnExit { get; set; } = true;
    public bool AlertOnDwell { get; set; }
    public int? DwellTimeMinutes { get; set; }

    // Stats
    public int AssignedVehicleCount { get; set; }
    public int ViolationCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateGeofenceDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required]
    [Range(0, 2)]
    public int Type { get; set; }

    // Shape data — canonical GeoJSON geometry; authoritative when provided.
    [StringLength(50000)]
    public string? Geometry { get; set; }

    // Legacy ring/coordinate blob (optional — canonical geometry is preferred).
    [StringLength(50000)]
    public string? Coordinates { get; set; }

    [Range(-90, 90)]
    public double? CenterLatitude { get; set; }

    [Range(-180, 180)]
    public double? CenterLongitude { get; set; }

    [Range(1, 100000)]
    public double? Radius { get; set; }

    // Style
    [StringLength(9)]
    public string? FillColor { get; set; }

    [StringLength(7)]
    public string? BorderColor { get; set; }

    [Range(1, 20)]
    public int? BorderWidth { get; set; }

    // Alerts
    public bool AlertOnEntry { get; set; } = true;
    public bool AlertOnExit { get; set; } = true;
    public bool AlertOnDwell { get; set; }

    [Range(1, 1440)]
    public int? DwellTimeMinutes { get; set; }

    // Vehicle assignments
    public List<Guid>? AssignedVehicleIds { get; set; }
}

public class UpdateGeofenceDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, 4)]
    public int? Status { get; set; }

    [Range(0, 2)]
    public int? Type { get; set; }

    // Shape data — canonical GeoJSON geometry; replaces the shape when provided.
    [StringLength(50000)]
    public string? Geometry { get; set; }

    [StringLength(50000, MinimumLength = 2)]
    public string? Coordinates { get; set; }

    [Range(-90, 90)]
    public double? CenterLatitude { get; set; }

    [Range(-180, 180)]
    public double? CenterLongitude { get; set; }

    [Range(1, 100000)]
    public double? Radius { get; set; }

    // Style
    [StringLength(9)]
    public string? FillColor { get; set; }

    [StringLength(7)]
    public string? BorderColor { get; set; }

    [Range(1, 20)]
    public int? BorderWidth { get; set; }

    // Alerts
    public bool? AlertOnEntry { get; set; }
    public bool? AlertOnExit { get; set; }
    public bool? AlertOnDwell { get; set; }

    [Range(1, 1440)]
    public int? DwellTimeMinutes { get; set; }
}

/// <summary>Bulk geofence import: "csv" (name,latitude,longitude,radius) or "geojson" (FeatureCollection).</summary>
public class ImportGeofencesDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    public string Format { get; set; } = "csv"; // "csv" | "geojson"

    [Required]
    public string Content { get; set; } = string.Empty;
}
