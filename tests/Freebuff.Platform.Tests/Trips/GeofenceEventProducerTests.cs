using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Trips;

/// <summary>
/// Containment primitive — the geometry math behind the zone-event producer.
/// Circles use haversine distance from the GeoJSON center; polygons use ray
/// casting over the GeoJSON coordinate ring [[lng,lat],...]. Boundary counts
/// as inside (a vehicle on the fence is effectively inside it).
/// </summary>
public class GeofenceContainmentTests
{
    private static Geofence Circle(double centerLat, double centerLng, double radiusMeters) => new()
    {
        Id = Guid.NewGuid(), Name = "Circle", Type = GeofenceType.Circle,
        CompanyId = Guid.NewGuid(),
        Geometry = $"{{\"type\":\"circle\",\"center\":[{centerLng},{centerLat}],\"radiusMeters\":{radiusMeters}}}"
    };

    private static Geofence Polygon(string coordsJson) => new()
    {
        Id = Guid.NewGuid(), Name = "Polygon", Type = GeofenceType.Polygon,
        CompanyId = Guid.NewGuid(),
        Geometry = $"{{\"type\":\"polygon\",\"coordinates\":{coordsJson}}}"
    };

    [Fact]
    public void Circle_CenterIsInside()
    {
        Assert.True(GeofenceContainment.IsInside(Circle(23.0, 72.5, 500), 23.0, 72.5));
    }

    [Fact]
    public void Circle_WithinRadiusIsInside()
    {
        // ~200 m north of the center (0.0018° lat) — well inside a 500 m circle.
        Assert.True(GeofenceContainment.IsInside(Circle(23.0, 72.5, 500), 23.0018, 72.5));
    }

    [Fact]
    public void Circle_BeyondRadiusIsOutside()
    {
        // ~1.1 km north of the center — outside a 500 m circle.
        Assert.False(GeofenceContainment.IsInside(Circle(23.0, 72.5, 500), 23.01, 72.5));
    }

    [Fact]
    public void Polygon_KnownInsidePoint_IsInside()
    {
        // Square around (72.5..72.6, 23.0..23.1).
        var fence = Polygon("[[72.5,23.0],[72.6,23.0],[72.6,23.1],[72.5,23.1]]");
        Assert.True(GeofenceContainment.IsInside(fence, 23.05, 72.55));
    }

    [Fact]
    public void Polygon_KnownOutsidePoint_IsOutside()
    {
        var fence = Polygon("[[72.5,23.0],[72.6,23.0],[72.6,23.1],[72.5,23.1]]");
        Assert.False(GeofenceContainment.IsInside(fence, 23.05, 72.7));
    }

    [Fact]
    public void LegacyCircle_FlatFields_StillWorks()
    {
        // Pre-geometry rows keep the flat fields — the containment check must
        // fall back to them instead of treating the geofence as empty.
        var fence = new Geofence
        {
            Id = Guid.NewGuid(), Name = "Legacy", Type = GeofenceType.Circle,
            CompanyId = Guid.NewGuid(), CenterLatitude = 23.0, CenterLongitude = 72.5, Radius = 500
        };
        Assert.True(GeofenceContainment.IsInside(fence, 23.0, 72.5));
        Assert.False(GeofenceContainment.IsInside(fence, 23.01, 72.5));
    }
}

/// <summary>
/// The event producer: after each accepted telemetry fix, evaluate the vehicle's
/// active trips against their linked geofences and fire entry/exit zone events
/// into the lifecycle (auto-start on origin exit, auto-complete on end-zone
/// entry, checkpoint visits, restricted-zone alerts).
/// </summary>
public class TripGeofenceEventProducerTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Geofence CircleGeofence(double centerLat, double centerLng, double radiusMeters = 500) => new()
    {
        Id = Guid.NewGuid(), Name = $"F {centerLat},{centerLng}", Type = GeofenceType.Circle,
        CompanyId = Guid.NewGuid(),
        Geometry = $"{{\"type\":\"circle\",\"center\":[{centerLng},{centerLat}],\"radiusMeters\":{radiusMeters}}}"
    };

    private static Trip TripFor(Guid company, Guid vehicle, TripStatus status, string name = "Run") => new()
    {
        Id = Guid.NewGuid(), Name = name, Status = status,
        CompanyId = company, TenantId = company,
        VehicleId = vehicle, DriverId = Guid.NewGuid(),
        StartLocation = "Depot", StartLatitude = 23.0, StartLongitude = 72.5,
        EndLocation = "Customer", EndLatitude = 23.2, EndLongitude = 72.7,
    };

    private static void Link(ApplicationDbContext db, Trip trip, Guid company, Geofence fence, TripGeofenceRole role, int? seq = null)
    {
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = fence.Id, Role = role, SequenceOrder = seq
        });
    }

    private static async Task<TripGeofenceEventProducer> SeedAsync(
        ApplicationDbContext db, Guid company, Guid vehicle, Trip trip, Geofence[] fences,
        (double Lat, double Lng)? previousFix = null)
    {
        db.Geofences.AddRange(fences);
        db.Trips.Add(trip);
        if (previousFix.HasValue)
        {
            db.TelemetryEvents.Add(new TelemetryEvent
            {
                Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle,
                EventTimeUtc = DateTime.UtcNow.AddMinutes(-5),
                Latitude = previousFix.Value.Lat, Longitude = previousFix.Value.Lng
            });
        }
        await db.SaveChangesAsync();
        return new TripGeofenceEventProducer(db, new TripLifecycleService(db));
    }

    [Fact]
    public async Task ExitOriginZone_StartsScheduledTrip()
    {
        using var db = NewDb("prod_exit_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var origin = CircleGeofence(23.0, 72.5);
        var trip = TripFor(company, vehicle, TripStatus.Scheduled);
        Link(db, trip, company, origin, TripGeofenceRole.StartZone);
        // Previous fix was INSIDE the origin circle; the new fix is ~2 km away.
        var producer = await SeedAsync(db, company, vehicle, trip, new[] { origin }, (23.0, 72.5));
        await db.SaveChangesAsync();

        await producer.ProcessPositionAsync(vehicle, 23.01, 72.5, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(TripStatus.InProgress, trip.Status);
        Assert.Contains(db.TripStatusHistories,
            h => h.TripId == trip.Id && h.ToStatus == TripStatus.InProgress && h.Source == "geofence_event");
    }

    [Fact]
    public async Task EntryEndZone_CompletesInProgressTrip()
    {
        using var db = NewDb("prod_entry_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var end = CircleGeofence(23.2, 72.7);
        var trip = TripFor(company, vehicle, TripStatus.InProgress, "Arrive");
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        Link(db, trip, company, end, TripGeofenceRole.EndZone);
        // Previous fix was ~2 km from the end zone; the new fix is inside it.
        var producer = await SeedAsync(db, company, vehicle, trip, new[] { end }, (23.2, 72.68));
        await db.SaveChangesAsync();

        await producer.ProcessPositionAsync(vehicle, 23.2, 72.7, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(TripStatus.Completed, trip.Status);
        Assert.NotNull(trip.ActualEndTime);
    }

    [Fact]
    public async Task EntryCheckpoint_MarksVisited()
    {
        using var db = NewDb("prod_ckpt_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var ckpt = CircleGeofence(23.1, 72.6);
        var trip = TripFor(company, vehicle, TripStatus.InProgress, "Ckpt");
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        Link(db, trip, company, ckpt, TripGeofenceRole.Checkpoint, 1);
        var producer = await SeedAsync(db, company, vehicle, trip, new[] { ckpt }, (23.1, 72.58));
        await db.SaveChangesAsync();

        await producer.ProcessPositionAsync(vehicle, 23.1, 72.6, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var link = db.TripGeofences.Single(g => g.TripId == trip.Id);
        Assert.True(link.Visited, "checkpoint entry from telemetry should mark the link visited");
        Assert.NotNull(link.VisitedAt);
    }

    [Fact]
    public async Task FirstFix_NoPreviousPosition_FiresNothing()
    {
        using var db = NewDb("prod_first_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var end = CircleGeofence(23.2, 72.7);
        var trip = TripFor(company, vehicle, TripStatus.InProgress, "First");
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        Link(db, trip, company, end, TripGeofenceRole.EndZone);
        // No previous telemetry — the first fix must not fire spurious events.
        var producer = await SeedAsync(db, company, vehicle, trip, new[] { end });

        await producer.ProcessPositionAsync(vehicle, 23.2, 72.7, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(TripStatus.InProgress, trip.Status);
        Assert.Empty(db.TripStatusHistories.Where(h => h.TripId == trip.Id));
    }
}