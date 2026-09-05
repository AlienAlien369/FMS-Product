using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Trip lifecycle rules — the single place status transitions, scheduling
/// preconditions, double-booking checks and completion metrics live. The
/// controller is a thin shell over these rules, and the geofence/telemetry
/// event pipeline (when one exists) calls the same service instead of
/// re-implementing the policy.
/// </summary>
public class TripLifecycleService
{
    private readonly ApplicationDbContext _db;
    public TripLifecycleService(ApplicationDbContext db) { _db = db; }

    // ── Pure validation (unit-testable without a database) ─────────────────

    /// <summary>
    /// Waypoint shape rules: ≥ 2 waypoints (origin + destination), unique
    /// sequence numbers, and for round trips at least one return leg with
    /// contiguous legs (no outbound waypoint after a return one — the
    /// turnaround is the last Outbound waypoint before the first Return one).
    /// </summary>
    public static List<string> ValidateWaypoints(IEnumerable<TripWaypoint> waypoints, TripType type)
    {
        var list = waypoints.ToList();
        var errors = new List<string>();
        if (list.Count < 2)
        {
            errors.Add("A trip needs at least an origin and a destination waypoint.");
            return errors;
        }
        var seqs = new HashSet<int>();
        foreach (var w in list)
        {
            if (!seqs.Add(w.SequenceOrder))
                errors.Add($"Duplicate waypoint sequence number {w.SequenceOrder}.");
            if (!double.IsFinite(w.Latitude) || !double.IsFinite(w.Longitude))
                errors.Add($"Waypoint '{w.Name}' has an invalid position.");
        }
        if (type == TripType.Round)
        {
            if (list.All(w => w.LegType == TripLegType.Outbound))
                errors.Add("Round trip needs at least one return-leg waypoint after the turnaround point.");
            var seenReturn = false;
            foreach (var w in list.OrderBy(w => w.SequenceOrder))
            {
                if (w.LegType == TripLegType.Return) seenReturn = true;
                else if (seenReturn)
                {
                    errors.Add("Round trip legs must be contiguous — no outbound waypoint after a return-leg waypoint.");
                    break;
                }
            }
        }
        return errors;
    }

    /// <summary>
    /// Duplicate-sequence check for the replace-all geofence link endpoint —
    /// mirrors the RouteGeofence rule (checkpoint sequence numbers must be unique).
    /// </summary>
    public static List<string> ValidateGeofenceLinks(IReadOnlyList<(Guid GeofenceId, int Role, int? SequenceOrder)> links)
    {
        var errors = new List<string>();
        var unique = new HashSet<Guid>();
        var seqs = new HashSet<int>();
        foreach (var l in links)
        {
            if (!Enum.IsDefined(typeof(TripGeofenceRole), l.Role))
            {
                errors.Add($"Invalid role value {l.Role}.");
                continue;
            }
            if (!unique.Add(l.GeofenceId))
                errors.Add("A geofence can only be linked once per trip — pick a single role for it.");
            if (l.Role == (int)TripGeofenceRole.Checkpoint && l.SequenceOrder.HasValue && !seqs.Add(l.SequenceOrder.Value))
                errors.Add($"Duplicate checkpoint sequence number {l.SequenceOrder.Value}.");
        }
        return errors;
    }

    // ── Database-backed preconditions ─────────────────────────────────────

    /// <summary>
    /// Double-booking guard: vehicle or driver already on another in-progress
    /// trip in this company → hard error (genuine operational conflict).
    /// </summary>
    public async Task<List<string>> AssignmentConflictsAsync(Guid companyId, Guid vehicleId, Guid driverId, Guid? excludeTripId = null)
    {
        var inProgress = await _db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status == TripStatus.InProgress && t.CompanyId == companyId
                && (excludeTripId == null || t.Id != excludeTripId.Value))
            .Select(t => new { t.Id, t.VehicleId, t.DriverId })
            .ToListAsync();

        var errors = new List<string>();
        if (inProgress.Any(t => t.VehicleId == vehicleId))
            errors.Add("Vehicle is already assigned to another in-progress trip.");
        if (inProgress.Any(t => t.DriverId == driverId))
            errors.Add("Driver is already assigned to another in-progress trip.");
        return errors;
    }

    /// <summary>
    /// A trip may only move from draft to scheduled when it has at least one
    /// linked geofence — directly, or inherited from its linked route.
    /// </summary>
    public async Task<List<string>> SchedulingPreconditionsAsync(Guid tripId)
    {
        var errors = new List<string>();
        var trip = await _db.Trips.AsNoTracking()
            .Include(t => t.TripGeofences)
            .Include(t => t.TripWaypoints)
            .Include(t => t.Route).ThenInclude(r => r!.RouteGeofences)
            .FirstOrDefaultAsync(t => t.Id == tripId && !t.IsDeleted);
        if (trip == null) { errors.Add("Trip not found."); return errors; }

        var direct = trip.TripGeofences.Count;
        var viaRoute = trip.RouteId.HasValue && trip.Route != null && trip.Route.RouteGeofences.Count > 0;
        if (direct == 0 && !viaRoute)
            errors.Add("A trip needs at least one linked geofence (directly or via its route) before it can be scheduled.");

        var waypointErrors = ValidateWaypoints(trip.TripWaypoints, trip.Type);
        errors.AddRange(waypointErrors);
        return errors;
    }

    // ── Transitions ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a status transition with all business rules, writing a
    /// TripStatusHistory row for every change. Returns errors (does not throw)
    /// so the controller can 400 cleanly.
    /// </summary>
    public async Task<List<string>> TransitionAsync(Trip trip, TripStatus target, string? reason, string source, string actorId, DateTime? at = null)
    {
        var errors = new List<string>();
        if (trip.Status == target) return errors; // idempotent no-op

        var terminal = trip.Status is TripStatus.Completed or TripStatus.Cancelled or TripStatus.Aborted;
        if (terminal)
        {
            errors.Add($"Trip is already {trip.Status} — no further transitions.");
            return errors;
        }

        switch (target)
        {
            case TripStatus.Scheduled:
            {
                var pre = await SchedulingPreconditionsAsync(trip.Id);
                errors.AddRange(pre);
                if (errors.Count > 0) return errors;
                var conflicts = await AssignmentConflictsAsync(trip.CompanyId, trip.VehicleId, trip.DriverId, trip.Id);
                errors.AddRange(conflicts);
                if (errors.Count > 0) return errors;
                break;
            }
            case TripStatus.InProgress:
            {
                if (trip.Status is not (TripStatus.Draft or TripStatus.Scheduled))
                {
                    errors.Add("A trip can only start from Draft or Scheduled.");
                    return errors;
                }
                var conflicts = await AssignmentConflictsAsync(trip.CompanyId, trip.VehicleId, trip.DriverId, trip.Id);
                errors.AddRange(conflicts);
                if (errors.Count > 0) return errors;
                trip.ActualStartTime ??= at ?? DateTime.UtcNow;
                break;
            }
            case TripStatus.Completed:
            {
                if (trip.Status is not (TripStatus.InProgress or TripStatus.Scheduled))
                {
                    errors.Add("A trip can only be completed from InProgress or Scheduled.");
                    return errors;
                }
                trip.ActualEndTime ??= at ?? DateTime.UtcNow;
                trip.ActualStartTime ??= trip.ActualEndTime;
                await AggregateMetricsAsync(trip);
                RaiseMissedCheckpointAlerts(trip);
                break;
            }
            case TripStatus.Cancelled:
            case TripStatus.Aborted:
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    errors.Add($"A reason is required when a trip is {target.ToString().ToLowerInvariant()} — dispatchers need to know why.");
                    return errors;
                }
                trip.CancelReason = reason;
                if (target == TripStatus.Aborted && trip.Status == TripStatus.Completed)
                {
                    errors.Add("A completed trip cannot be aborted.");
                    return errors;
                }
                break;
            }
            default:
                errors.Add($"Transition to {target} is not supported.");
                return errors;
        }

        var from = trip.Status;
        trip.Status = target;
        if (target == TripStatus.InProgress)
        {
            // Missed expected arrivals flag the trip delayed — never changes status.
            var delayed = await EvaluateDelayAsync(trip);
            if (delayed && string.IsNullOrWhiteSpace(reason))
                reason = "One or more expected arrivals were missed.";
        }

        _db.TripStatusHistories.Add(new TripStatusHistory
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            TenantId = trip.CompanyId,
            FromStatus = from,
            ToStatus = target,
            Reason = reason,
            Source = source,
            ChangedAt = DateTime.UtcNow
        });
        return errors;
    }

    /// <summary>
    /// Flags the trip delayed (sub-state) when any waypoint's expected arrival
    /// is in the past and it was never reached. Does not change Status.
    /// </summary>
    public async Task<bool> EvaluateDelayAsync(Trip trip)
    {
        var now = DateTime.UtcNow;
        var waypoints = trip.TripWaypoints.Count > 0
            ? trip.TripWaypoints
            : await _db.TripWaypoints.AsNoTracking().Where(w => w.TripId == trip.Id && !w.IsDeleted).ToListAsync();

        var missed = waypoints
            .Where(w => w.ExpectedArrival.HasValue && w.ActualArrival == null && w.ExpectedArrival.Value < now)
            .OrderBy(w => w.SequenceOrder)
            .FirstOrDefault();
        if (missed != null)
        {
            trip.IsDelayed = true;
            trip.DelayReason ??= $"Missed expected arrival at '{missed.Name}' (was {missed.ExpectedArrival:u}).";
            return true;
        }
        return trip.IsDelayed;
    }

    // ── Zone-event pipeline (geofence/telemetry events → trip automation) ──

    /// <summary>Outcome of processing one geofence zone event against a trip.</summary>
    public class TripZoneEventResult
    {
        public bool StatusChanged { get; set; }
        public TripStatus? NewStatus { get; set; }
        /// <summary>Soft warnings (e.g. out-of-order checkpoint visit) — informational, never blocking.</summary>
        public List<string> Warnings { get; set; } = new();
        /// <summary>Hard failure that prevented processing (unknown trip, wrong event for this state).</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Entry point for the geofence/telemetry event pipeline: one zone event
    /// (entry/exit) for one geofence, applied to the trip that links it.
    /// Exit of the origin zone auto-starts a scheduled trip; entry of the end
    /// zone auto-completes an in-progress one; checkpoint entry marks visits;
    /// restricted-zone entry raises a distinct alert. Source is always
    /// "geofence_event" so manual vs automated transitions stay separable.
    /// </summary>
    public async Task<TripZoneEventResult> HandleZoneEventAsync(Guid tripId, Guid geofenceId, TripZoneEventKind kind, DateTime at)
    {
        var result = new TripZoneEventResult();
        var trip = await _db.Trips
            .Include(t => t.TripWaypoints)
            .Include(t => t.TripGeofences)
            .FirstOrDefaultAsync(t => t.Id == tripId && !t.IsDeleted);
        if (trip == null) { result.Error = "Trip not found."; return result; }

        // Origin zone = explicit StartZone link, else the first waypoint's geofence;
        // end zone = explicit EndZone link, else the last waypoint's geofence.
        var originGeofenceId = trip.TripGeofences.FirstOrDefault(g => g.Role == TripGeofenceRole.StartZone)?.GeofenceId
            ?? trip.TripWaypoints.OrderBy(w => w.SequenceOrder).FirstOrDefault(w => w.LinkedGeofenceId.HasValue)?.LinkedGeofenceId;
        var endGeofenceId = trip.TripGeofences.FirstOrDefault(g => g.Role == TripGeofenceRole.EndZone)?.GeofenceId
            ?? trip.TripWaypoints.OrderByDescending(w => w.SequenceOrder).FirstOrDefault(w => w.LinkedGeofenceId.HasValue)?.LinkedGeofenceId;

        if (kind == TripZoneEventKind.Exit && geofenceId == originGeofenceId && trip.Status == TripStatus.Scheduled)
        {
            var errors = await TransitionAsync(trip, TripStatus.InProgress, "Vehicle departed the origin zone", "geofence_event", "system", at);
            if (errors.Count == 0)
            {
                result.StatusChanged = true;
                result.NewStatus = trip.Status;
            }
            else result.Error = string.Join(" ", errors);
        }
        else if (kind == TripZoneEventKind.Entry && geofenceId == endGeofenceId && trip.Status == TripStatus.InProgress)
        {
            var errors = await TransitionAsync(trip, TripStatus.Completed, "Vehicle reached the destination zone", "geofence_event", "system", at);
            if (errors.Count == 0)
            {
                result.StatusChanged = true;
                result.NewStatus = trip.Status;
            }
            else result.Error = string.Join(" ", errors);
        }
        else if (kind == TripZoneEventKind.Entry)
        {
            var checkpoint = trip.TripGeofences.FirstOrDefault(g => g.Role == TripGeofenceRole.Checkpoint && g.GeofenceId == geofenceId);
            if (checkpoint != null)
            {
                checkpoint.Visited = true;
                checkpoint.VisitedAt = at;
                if (checkpoint.SequenceOrder.HasValue
                    && trip.TripGeofences.Any(g => g.Role == TripGeofenceRole.Checkpoint && g.Id != checkpoint.Id
                        && g.SequenceOrder.HasValue && g.SequenceOrder < checkpoint.SequenceOrder && g.Visited != true))
                {
                    result.Warnings.Add($"Checkpoint '{checkpoint.GeofenceId}' visited out of order — an earlier checkpoint is still unvisited.");
                }
            }
            else if (trip.TripGeofences.Any(g => g.Role == TripGeofenceRole.RestrictedZone && g.GeofenceId == geofenceId))
            {
                _db.Alerts.Add(new Alert
                {
                    Id = Guid.NewGuid(),
                    AlertType = "TripRestrictedZoneViolation",
                    Severity = AlertSeverity.High,
                    Title = $"Vehicle entered a restricted zone on trip '{trip.Name}'",
                    Message = $"Trip '{trip.Name}' entered restricted-zone geofence {geofenceId} at {at:u}.",
                    CompanyId = trip.CompanyId,
                    TenantId = trip.CompanyId,
                    VehicleId = trip.VehicleId,
                    DriverId = trip.DriverId,
                    Latitude = trip.StartLatitude,
                    Longitude = trip.StartLongitude,
                });
                result.Warnings.Add($"Restricted-zone violation on trip '{trip.Name}': vehicle entered a do-not-enter geofence.");
            }
        }
        return result;
    }

    /// <summary>Marks a waypoint actually-arrived (geofence event / manual). Returns false when the waypoint is unknown.</summary>
    public async Task<bool> RecordWaypointArrivalAsync(Guid tripId, Guid waypointId, DateTime? at = null)
    {
        var wp = await _db.TripWaypoints.FirstOrDefaultAsync(w => w.Id == waypointId && w.TripId == tripId && !w.IsDeleted);
        if (wp == null) return false;
        wp.ActualArrival = at ?? DateTime.UtcNow;

        // A geofence-linked waypoint also marks its trip-geofence checkpoint visited.
        if (wp.LinkedGeofenceId.HasValue)
        {
            var link = await _db.TripGeofences.FirstOrDefaultAsync(g => g.TripId == tripId && g.GeofenceId == wp.LinkedGeofenceId.Value && !g.IsDeleted);
            if (link != null && link.Role == TripGeofenceRole.Checkpoint)
            {
                link.Visited = true;
                link.VisitedAt = wp.ActualArrival;
            }
        }
        return true;
    }

    // ── Completion metrics (telemetry-derived) ─────────────────────────────

    /// <summary>
    /// Aggregates actual distance / max+avg speed / fuel used / idle time onto
    /// the trip record from the normalized telemetry stream for the trip window.
    /// No telemetry rows → metrics stay null (the trip still completes).
    /// </summary>
    private async Task AggregateMetricsAsync(Trip trip)
    {
        var start = trip.ActualStartTime;
        var end = trip.ActualEndTime;
        if (start == null || end == null) return;

        var events = await _db.TelemetryEvents.AsNoTracking()
            .Where(e => e.VehicleId == trip.VehicleId && e.EventTimeUtc >= start.Value && e.EventTimeUtc <= end.Value
                && e.Latitude.HasValue && e.Longitude.HasValue)
            .OrderBy(e => e.EventTimeUtc)
            .Select(e => new { e.EventTimeUtc, e.Latitude, e.Longitude, e.SpeedKmh, e.FuelLevelLiters })
            .ToListAsync();
        if (events.Count == 0) return;

        double distance = 0;
        double maxSpeed = 0;
        double speedSum = 0;
        int speedCount = 0;
        for (var i = 1; i < events.Count; i++)
        {
            distance += HaversineKm(events[i - 1].Latitude!.Value, events[i - 1].Longitude!.Value,
                events[i].Latitude!.Value, events[i].Longitude!.Value);
        }
        foreach (var e in events)
        {
            if (e.SpeedKmh.HasValue)
            {
                maxSpeed = Math.Max(maxSpeed, e.SpeedKmh.Value);
                speedSum += e.SpeedKmh.Value;
                speedCount++;
            }
        }
        var fuelStart = events.FirstOrDefault(e => e.FuelLevelLiters.HasValue)?.FuelLevelLiters;
        var fuelEnd = events.LastOrDefault(e => e.FuelLevelLiters.HasValue)?.FuelLevelLiters;
        var fuelUsed = fuelStart.HasValue && fuelEnd.HasValue ? fuelStart.Value - fuelEnd.Value : (double?)null;

        trip.ActualDistance = Math.Round((decimal)distance, 2);
        trip.MaxSpeed = Math.Round((decimal)maxSpeed, 1);
        trip.AverageSpeed = speedCount > 0 ? Math.Round((decimal)(speedSum / speedCount), 1) : null;
        trip.FuelUsedLiters = fuelUsed.HasValue ? Math.Round((decimal)fuelUsed.Value, 2) : null;
        trip.ActualDuration = end.Value - start.Value;
        trip.IdleMinutes = events.Count(e => e.SpeedKmh.HasValue && e.SpeedKmh.Value < 5) > 0
            ? (int?)Math.Round(events.Count(e => e.SpeedKmh.HasValue && e.SpeedKmh.Value < 5) * 1.0) // coarse proxy
            : null;
    }

    /// <summary>
    /// Flags every checkpoint the trip never visited as a TripMissedCheckpoint
    /// alert (distinct alert type, filterable separately from geofence breaches).
    /// Runs on completion — manual and zone-event paths both pass through here.
    /// </summary>
    private void RaiseMissedCheckpointAlerts(Trip trip)
    {
        foreach (var ckpt in trip.TripGeofences.Where(g => g.Role == TripGeofenceRole.Checkpoint && g.Visited != true))
        {
            _db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                AlertType = "TripMissedCheckpoint",
                Severity = AlertSeverity.Medium,
                Title = $"Trip '{trip.Name}' completed without visiting checkpoint '{ckpt.GeofenceId}'",
                Message = $"Checkpoint geofence {ckpt.GeofenceId} was never visited before trip '{trip.Name}' completed.",
                CompanyId = trip.CompanyId,
                TenantId = trip.CompanyId,
                VehicleId = trip.VehicleId,
                DriverId = trip.DriverId
            });
        }
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}