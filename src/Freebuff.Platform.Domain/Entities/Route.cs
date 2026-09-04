using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Represents an optimized or planned route for fleet operations.
/// </summary>
public class Route : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RouteStatus Status { get; set; } = RouteStatus.Draft;
    public RouteType Type { get; set; } = RouteType.Standard;
    public bool IsOptimized { get; set; }
    public bool IsTemplate { get; set; } // Reusable route templates

    // Origin & Destination
    public string OriginName { get; set; } = string.Empty;
    public double OriginLatitude { get; set; }
    public double OriginLongitude { get; set; }
    public string? DestinationName { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }

    // Waypoints (JSON array of {name, lat, lng, stopDuration, sequenceOrder})
    public string? Waypoints { get; set; }

    // Route geometry (JSON polyline or coordinate array)
    public string? RouteGeometry { get; set; }

    // Metrics
    public decimal? TotalDistance { get; set; } // km or miles
    public string? DistanceUnit { get; set; } = "km";
    public TimeSpan? EstimatedDuration { get; set; }
    public decimal? EstimatedFuelCost { get; set; }
    public decimal? EstimatedTollCost { get; set; }
    public string? Currency { get; set; } = "USD";
    public int? TrafficLevel { get; set; } // 0-100, 0=no traffic, 100=worst

    // Constraints
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxVehicles { get; set; }
    public int? Priority { get; set; } // 1=low, 5=critical

    // Scheduling
    public string? RecurrenceRule { get; set; } // iCal RRULE for recurring routes
    public int? DayOfWeek { get; set; } // 0=Sunday, 6=Saturday
    public TimeSpan? PreferredStartTime { get; set; }

    // Path source: Directions (road-snapped polyline) or Manual (hand-drawn override)
    public RoutePathSource PathSource { get; set; } = RoutePathSource.Directions;

    // Corridor deviation config (per-route, separately toggleable). Evaluated by
    // the trip/position engine when a live telemetry stream exists; stored here
    // so enabling it is pure configuration, not a deploy.
    public bool CorridorEnabled { get; set; }
    public double? CorridorBufferMeters { get; set; }
    public int? DeviationThresholdMinutes { get; set; }

    // Company
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Navigation
    public ICollection<RouteVehicle> RouteVehicles { get; set; } = new List<RouteVehicle>();
    public ICollection<RouteGeofence> RouteGeofences { get; set; } = new List<RouteGeofence>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}

/// <summary>
/// Junction: Route ↔ Geofence with a semantic role on that route.
/// Checkpoint — vehicle is expected to pass through; verified per trip.
/// RestrictedZone — vehicle must not enter while on this route.
/// StartZone / EndZone — optional explicit marking when a route terminus is
/// itself a known geofence. SequenceOrder is only meaningful for Checkpoint
/// (validated in the expected order); one geofence may not be linked twice.
/// </summary>
public class RouteGeofence : BaseEntity
{
    public Guid RouteId { get; set; }
    public Route Route { get; set; } = null!;

    public Guid GeofenceId { get; set; }
    public Geofence Geofence { get; set; } = null!;

    public RouteGeofenceRole Role { get; set; } = RouteGeofenceRole.Checkpoint;
    public int? SequenceOrder { get; set; }
}

/// <summary>
/// Junction: Route ↔ Vehicle with scheduling
/// </summary>
public class RouteVehicle : BaseEntity
{
    public Guid RouteId { get; set; }
    public Route Route { get; set; } = null!;

    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public Guid? DriverId { get; set; }
    public Driver? Driver { get; set; }

    public DateTime? AssignedDate { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public decimal? ActualDistance { get; set; }
    public decimal? ActualFuelUsed { get; set; }
    public int SequenceOrder { get; set; }
    public string? Notes { get; set; }
}
