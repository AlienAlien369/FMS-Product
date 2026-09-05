using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Trip — orchestrates Vehicle + Driver + Route (optional) + Geofences
/// (mandatory before scheduling) into a trackable journey.
///
/// Reuses the primitives built elsewhere — nothing here reimplements them:
///   - Route linkage: when RouteId is set the trip inherits the route's
///     waypoints/geometry, linked geofences (as trip links) and corridor
///     settings as a starting template; a dynamic trip defines them directly.
///   - Geofence containment / checkpoint / restricted-zone / corridor logic:
///     consumers branch on the same TripGeofenceRole values the Route engine
///     uses; the shared RouteCorridor geometry primitive applies unchanged.
///   - Telemetry: live position and trip replay read the normalized
///     TelemetryEvents / TelemetryStates stream — no parallel ingestion.
///
/// Delayed is a sub-state flag (IsDelayed), not a status replacement: an
/// in-progress trip that misses an expected arrival stays InProgress and is
/// flagged, so ETA/alerting semantics are not corrupted by the flag.
/// </summary>
public class Trip : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Draft;

    /// <summary>Sub-state: expected arrival missed while in progress — does not replace Status.</summary>
    public bool IsDelayed { get; set; }
    public string? DelayReason { get; set; }
    public string? CancelReason { get; set; } // required for cancelled/aborted

    public TripType Type { get; set; } = TripType.Single;

    /// <summary>Denormalized legacy column (predates Type) — kept mapped and
    /// written in sync because pre-existing databases declare it NOT NULL and
    /// every insert would otherwise fail. Type is authoritative.</summary>
    public bool IsRoundTrip { get; set; }

    // Locations (legacy flat fields kept for backward compat; canonical path
    // lives in Waypoints/RouteGeometry below)
    public string StartLocation { get; set; } = string.Empty;
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? EndLocation { get; set; }
    public double? EndLatitude { get; set; }
    public double? EndLongitude { get; set; }
    public string? ViaPoints { get; set; }
    public string? Stops { get; set; }

    // Scheduling
    public DateTime? ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    // Path — ordered waypoints (TripWaypoints rows, what the user edits) plus
    // the resolved polyline geometry (GeoJSON LineString, what distance/ETA/
    // deviation math runs against). Both kept so edits stay in sync.
    public string? RouteGeometry { get; set; }

    // Corridor deviation config (per-trip, separately toggleable). Inherited
    // from the linked route at create time; editable on the trip itself.
    public bool CorridorEnabled { get; set; }
    public double? CorridorBufferMeters { get; set; }
    public int? DeviationThresholdMinutes { get; set; }

    // Metrics — aggregated onto the record at completion time from telemetry.
    public decimal? PlannedDistance { get; set; }
    public decimal? ActualDistance { get; set; }
    public TimeSpan? PlannedDuration { get; set; }
    public TimeSpan? ActualDuration { get; set; }
    public decimal? MaxSpeed { get; set; }
    public decimal? AverageSpeed { get; set; }
    public decimal? FuelUsedLiters { get; set; }
    public int? IdleMinutes { get; set; }

    // Associations
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;

    public Guid DriverId { get; set; }
    public Driver Driver { get; set; } = null!;

    /// <summary>Optional linked reusable route — trip inherits its path, geofences and corridor settings.</summary>
    public Guid? RouteId { get; set; }
    public Route? Route { get; set; }

    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }

    public string? RouteData { get; set; } // legacy JSON route geometry

    // Navigation
    public ICollection<TripWaypoint> TripWaypoints { get; set; } = new List<TripWaypoint>();
    public ICollection<TripGeofence> TripGeofences { get; set; } = new List<TripGeofence>();
    public ICollection<TripStatusHistory> StatusHistory { get; set; } = new List<TripStatusHistory>();
}

/// <summary>
/// An ordered waypoint on a trip. Round trips are modeled as ONE ordered
/// sequence with a per-waypoint LegType (outbound/return) — the turnaround is
/// the last Outbound waypoint before the first Return one. waypointType feeds
/// reporting (\"how many delivery stops did this driver complete\"), not just
/// decoration. proofOfCompletion is reserved for photo/signature/OTP evidence
/// (nullable today — no migration needed to bolt it on).
/// </summary>
public class TripWaypoint : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public int SequenceOrder { get; set; }
    public TripLegType LegType { get; set; } = TripLegType.Outbound;
    public TripWaypointType WaypointType { get; set; } = TripWaypointType.Other;
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public DateTime? ExpectedArrival { get; set; }
    public DateTime? ActualArrival { get; set; }

    /// <summary>Optional geofence this waypoint corresponds to (a geofence-defined stop).</summary>
    public Guid? LinkedGeofenceId { get; set; }

    /// <summary>Proof-of-completion evidence (photo URL / signature ref / OTP) — reserved, nullable.</summary>
    public string? ProofOfCompletion { get; set; }
}

/// <summary>
/// Geofence linked directly to a trip (same role model as RouteGeofence).
/// When the trip links a Route, its geofences are copied here at create time so
/// one consumer (the trip's own link set) drives checkpoint/restricted logic.
/// Visited/VisitedAt track checkpoint visits per trip.
/// </summary>
public class TripGeofence : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public Guid GeofenceId { get; set; }
    public Geofence Geofence { get; set; } = null!;

    public TripGeofenceRole Role { get; set; } = TripGeofenceRole.Checkpoint;
    public int? SequenceOrder { get; set; }

    public bool? Visited { get; set; }
    public DateTime? VisitedAt { get; set; }
}

/// <summary>
/// Timestamped status-history log — the audit trail of every transition
/// (who/what changed it, when, from/to, why). Trip reporting depends on knowing
/// when each transition happened, not just the current value.
/// </summary>
public class TripStatusHistory : BaseEntity
{
    public Guid TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public TripStatus FromStatus { get; set; }
    public TripStatus ToStatus { get; set; }
    public string? Reason { get; set; }

    /// <summary>manual | geofence_event | telemetry | system</summary>
    public string Source { get; set; } = "manual";
    public DateTime ChangedAt { get; set; }
}