using System.ComponentModel.DataAnnotations;

namespace Freebuff.Platform.Application.DTOs;

public class TripDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public bool IsDelayed { get; set; }
    public string? DelayReason { get; set; }
    public string? CancelReason { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public Guid? RouteId { get; set; }
    public string? RouteName { get; set; }

    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    public decimal? PlannedDistance { get; set; }
    public decimal? ActualDistance { get; set; }
    public TimeSpan? PlannedDuration { get; set; }
    public TimeSpan? ActualDuration { get; set; }
    public decimal? MaxSpeed { get; set; }
    public decimal? AverageSpeed { get; set; }
    public decimal? FuelUsedLiters { get; set; }
    public int? IdleMinutes { get; set; }

    public string? RouteGeometry { get; set; }
    public bool CorridorEnabled { get; set; }
    public double? CorridorBufferMeters { get; set; }
    public int? DeviationThresholdMinutes { get; set; }

    public int WaypointCount { get; set; }
    public int GeofenceCount { get; set; }
    public int CheckpointCount { get; set; }
    public int RestrictedZoneCount { get; set; }
    public int BoundaryZoneCount { get; set; }

    public List<TripWaypointDto>? Waypoints { get; set; }
    public List<TripGeofenceDto>? TripGeofences { get; set; }
    public List<TripStatusHistoryDto>? StatusHistory { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TripWaypointDto
{
    public Guid Id { get; set; }
    public int SequenceOrder { get; set; }
    public int LegType { get; set; }
    public string LegTypeName { get; set; } = string.Empty;
    public int WaypointType { get; set; }
    public string WaypointTypeName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public DateTime? ExpectedArrival { get; set; }
    public DateTime? ActualArrival { get; set; }
    public Guid? LinkedGeofenceId { get; set; }
}

public class TripGeofenceDto
{
    public Guid Id { get; set; }
    public Guid TripId { get; set; }
    public Guid GeofenceId { get; set; }
    public string GeofenceName { get; set; } = string.Empty;
    public int GeofenceType { get; set; }
    public string GeofenceTypeName { get; set; } = string.Empty;
    public string? Geometry { get; set; }
    public double? CenterLatitude { get; set; }
    public double? CenterLongitude { get; set; }
    public double? Radius { get; set; }
    public int Role { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int? SequenceOrder { get; set; }
    public bool? Visited { get; set; }
    public DateTime? VisitedAt { get; set; }
}

public class TripStatusHistoryDto
{
    public int FromStatus { get; set; }
    public int ToStatus { get; set; }
    public string? Reason { get; set; }
    public string Source { get; set; } = "manual";
    public DateTime ChangedAt { get; set; }
}

/// <summary>A single link entry for the replace-all trip geofence endpoint.</summary>
public class TripGeofenceLinkDto
{
    public Guid GeofenceId { get; set; }
    public int Role { get; set; }
    public int? SequenceOrder { get; set; }
}

public class CreateTripDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, 1)]
    public int Type { get; set; }

    [Required]
    public Guid VehicleId { get; set; }

    [Required]
    public Guid DriverId { get; set; }

    /// <summary>Optional linked reusable route — trip inherits its path, geofences and corridor settings.</summary>
    public Guid? RouteId { get; set; }

    public DateTime? ScheduledStartTime { get; set; }
    public List<TripWaypointDto>? Waypoints { get; set; }

    [StringLength(50000)]
    public string? RouteGeometry { get; set; }

    public bool? CorridorEnabled { get; set; }

    [Range(50, 10000)]
    public double? CorridorBufferMeters { get; set; }

    [Range(1, 60)]
    public int? DeviationThresholdMinutes { get; set; }

    public List<TripGeofenceLinkDto>? GeofenceLinks { get; set; }
}

public class UpdateTripDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(0, 1)]
    public int? Type { get; set; }

    public Guid? VehicleId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? RouteId { get; set; }
    public DateTime? ScheduledStartTime { get; set; }
    public List<TripWaypointDto>? Waypoints { get; set; }

    [StringLength(50000)]
    public string? RouteGeometry { get; set; }

    public bool? CorridorEnabled { get; set; }

    [Range(50, 10000)]
    public double? CorridorBufferMeters { get; set; }

    [Range(1, 60)]
    public int? DeviationThresholdMinutes { get; set; }
}

/// <summary>One zone event from the geofence/telemetry pipeline: entry/exit of a linked geofence.</summary>
public class TripZoneEventDto
{
    public Guid GeofenceId { get; set; }

    [Range(0, 1)]
    public int Kind { get; set; }

    /// <summary>Event timestamp — the trip records it verbatim. Defaults to now.</summary>
    public DateTime? At { get; set; }
}

public class UpdateTripStatusDto
{
    [Range(0, 5)]
    public int Status { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    /// <summary>manual | geofence_event | telemetry — who/what triggered the transition.</summary>
    [StringLength(30)]
    public string? Source { get; set; }
}

/// <summary>One replay sample — normalized telemetry projected for the trip timeline.</summary>
public class TripReplayPointDto
{
    public DateTime EventTimeUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDeg { get; set; }
    public bool? Ignition { get; set; }
}

/// <summary>Live position for the trip detail map (from the telemetry state stream).</summary>
public class TripLivePositionDto
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDeg { get; set; }
    public DateTime? UpdatedAt { get; set; }
}