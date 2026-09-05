using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// The zone-event producer: after each accepted telemetry fix it evaluates the
/// vehicle's active (Scheduled/InProgress) trips against their linked geofence
/// geometry and fires entry/exit events into TripLifecycleService — so trips
/// auto-start on origin departure and auto-complete on end-zone arrival without
/// a dispatcher. Stateless with respect to new tables: entry/exit is derived by
/// comparing the current fix against the vehicle's previous telemetry fix.
/// A fix with no predecessor establishes no baseline, so nothing fires — a
/// vehicle that was already inside a zone before telemetry started must not
/// trigger spurious completions or starts on the first fix.
/// </summary>
public class TripGeofenceEventProducer
{
    private readonly ApplicationDbContext _db;
    private readonly TripLifecycleService _lifecycle;

    public TripGeofenceEventProducer(ApplicationDbContext db, TripLifecycleService lifecycle)
    {
        _db = db;
        _lifecycle = lifecycle;
    }

    /// <summary>
    /// Evaluate one telemetry fix against the vehicle's active trips. Fires zone
    /// events via the lifecycle (which stages history/alerts); the caller owns
    /// SaveChanges so a position fix and its effects commit atomically.
    /// </summary>
    public async Task ProcessPositionAsync(Guid vehicleId, double latitude, double longitude, DateTime at)
    {
        var previous = await _db.TelemetryEvents.AsNoTracking()
            .Where(e => e.VehicleId == vehicleId && e.Latitude.HasValue && e.Longitude.HasValue)
            .OrderByDescending(e => e.EventTimeUtc).ThenByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        if (previous == null) return; // no baseline — first fix establishes nothing

        var trips = await _db.Trips
            .Include(t => t.TripGeofences).ThenInclude(g => g.Geofence)
            .Where(t => !t.IsDeleted && t.VehicleId == vehicleId
                && (t.Status == TripStatus.Scheduled || t.Status == TripStatus.InProgress))
            .ToListAsync();
        if (trips.Count == 0) return;

        foreach (var trip in trips)
        {
            foreach (var link in trip.TripGeofences.Where(g => !g.IsDeleted && g.Geofence != null))
            {
                var nowInside = GeofenceContainment.IsInside(link.Geofence, latitude, longitude);
                var wasInside = GeofenceContainment.IsInside(link.Geofence, previous.Latitude!.Value, previous.Longitude!.Value);
                if (nowInside == wasInside) continue;
                await _lifecycle.HandleZoneEventAsync(trip.Id, link.GeofenceId,
                    nowInside ? TripZoneEventKind.Entry : TripZoneEventKind.Exit, at);
            }
            // Corridor deviation is a corridor problem, not a zone problem —
            // evaluate every fix regardless of geofence boundaries.
            _lifecycle.EvaluateCorridorDeviation(trip, latitude, longitude, at);
        }
    }
}