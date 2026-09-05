using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Trips;

public class TripWaypointValidationTests
{
    private static TripWaypoint Wp(int seq, double lat = 23.0, double lng = 72.0, TripLegType leg = TripLegType.Outbound)
        => new() { Id = Guid.NewGuid(), SequenceOrder = seq, Name = $"Stop {seq}", Latitude = lat, Longitude = lng, LegType = leg };

    [Fact]
    public void FewerThanTwoWaypoints_IsRejected()
    {
        var errors = TripLifecycleService.ValidateWaypoints(new[] { Wp(1) }, TripType.Single);
        Assert.Contains(errors, e => e.Contains("origin and a destination"));
    }

    [Fact]
    public void DuplicateSequence_IsRejected()
    {
        var errors = TripLifecycleService.ValidateWaypoints(new[] { Wp(1), Wp(1) }, TripType.Single);
        Assert.Contains(errors, e => e.Contains("Duplicate waypoint sequence"));
    }

    [Fact]
    public void SingleTrip_TwoOrderedWaypoints_IsValid()
    {
        var errors = TripLifecycleService.ValidateWaypoints(new[] { Wp(1), Wp(2) }, TripType.Single);
        Assert.Empty(errors);
    }

    [Fact]
    public void RoundTrip_WithoutReturnLeg_IsRejected()
    {
        var errors = TripLifecycleService.ValidateWaypoints(new[] { Wp(1), Wp(2), Wp(3) }, TripType.Round);
        Assert.Contains(errors, e => e.Contains("return-leg waypoint"));
    }

    [Fact]
    public void RoundTrip_ReturnLegThenOutboundLeg_IsRejected()
    {
        var errors = TripLifecycleService.ValidateWaypoints(
            new[]
            {
                Wp(1, leg: TripLegType.Outbound),
                Wp(2, leg: TripLegType.Return),
                Wp(3, leg: TripLegType.Outbound), // outbound after return — must not happen
            }, TripType.Round);
        Assert.Contains(errors, e => e.Contains("contiguous"));
    }

    [Fact]
    public void RoundTrip_OutboundThenReturn_IsValid()
    {
        var errors = TripLifecycleService.ValidateWaypoints(
            new[]
            {
                Wp(1, leg: TripLegType.Outbound),
                Wp(2, leg: TripLegType.Outbound), // turnaround
                Wp(3, leg: TripLegType.Return),
            }, TripType.Round);
        Assert.Empty(errors);
    }

    [Fact]
    public void NaNPosition_IsRejected()
    {
        var errors = TripLifecycleService.ValidateWaypoints(new[] { Wp(1, lat: double.NaN), Wp(2) }, TripType.Single);
        Assert.Contains(errors, e => e.Contains("invalid position"));
    }
}

public class TripGeofenceLinkValidationTests
{
    [Fact]
    public void DuplicateGeofence_IsRejected()
    {
        var gid = Guid.NewGuid();
        var errors = TripLifecycleService.ValidateGeofenceLinks(new[]
        {
            (gid, (int)TripGeofenceRole.Checkpoint, (int?)1),
            (gid, (int)TripGeofenceRole.RestrictedZone, (int?)null),
        });
        Assert.Contains(errors, e => e.Contains("only be linked once"));
    }

    [Fact]
    public void DuplicateCheckpointSequence_IsRejected()
    {
        var errors = TripLifecycleService.ValidateGeofenceLinks(new[]
        {
            (Guid.NewGuid(), (int)TripGeofenceRole.Checkpoint, (int?)2),
            (Guid.NewGuid(), (int)TripGeofenceRole.Checkpoint, (int?)2),
        });
        Assert.Contains(errors, e => e.Contains("Duplicate checkpoint sequence"));
    }

    [Fact]
    public void InvalidRole_IsRejected()
    {
        var errors = TripLifecycleService.ValidateGeofenceLinks(new[]
        {
            (Guid.NewGuid(), 99, (int?)null),
        });
        Assert.Contains(errors, e => e.Contains("Invalid role value"));
    }

    [Fact]
    public void ValidLinks_Pass()
    {
        var errors = TripLifecycleService.ValidateGeofenceLinks(new[]
        {
            (Guid.NewGuid(), (int)TripGeofenceRole.Checkpoint, (int?)1),
            (Guid.NewGuid(), (int)TripGeofenceRole.RestrictedZone, (int?)null),
        });
        Assert.Empty(errors);
    }
}

/// <summary>
/// Zone-event driven lifecycle (the geofence/telemetry event pipeline's entry
/// point): exit-origin auto-starts, end-zone entry auto-completes, checkpoint
/// entry marks visits (with out-of-order soft warnings), restricted-zone entry
/// raises a distinct alert. All on the in-memory store.
/// </summary>
public class TripZoneEventTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Trip NewScheduledTrip(Guid company, Guid vehicle, Guid driver, Guid? originGeofence = null)
    {
        var trip = new Trip
        {
            Id = Guid.NewGuid(), Name = "Zone run", Status = TripStatus.Scheduled,
            CompanyId = company, TenantId = company,
            VehicleId = vehicle, DriverId = driver,
            StartLocation = "Depot", StartLatitude = 23.0, StartLongitude = 72.5,
            EndLocation = "Customer", EndLatitude = 23.2, EndLongitude = 72.7,
        };
        trip.TripWaypoints.Add(new TripWaypoint { Id = Guid.NewGuid(), TripId = trip.Id, SequenceOrder = 1, Name = "Depot", Latitude = 23.0, Longitude = 72.5, LinkedGeofenceId = originGeofence });
        trip.TripWaypoints.Add(new TripWaypoint { Id = Guid.NewGuid(), TripId = trip.Id, SequenceOrder = 2, Name = "Customer", Latitude = 23.2, Longitude = 72.7 });
        return trip;
    }

    [Fact]
    public async Task EntryEndZone_CompletesInProgressTrip_WithGeofenceEventSource()
    {
        using var db = NewDb("zone_end_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var endGeofence = Guid.NewGuid();
        var trip = NewScheduledTrip(company, Guid.NewGuid(), Guid.NewGuid());
        trip.Status = TripStatus.InProgress;
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = endGeofence, Role = TripGeofenceRole.EndZone
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var at = DateTime.UtcNow;

        var result = await service.HandleZoneEventAsync(trip.Id, endGeofence, TripZoneEventKind.Entry, at);

        Assert.True(result.StatusChanged, "reaching the end zone should change status");
        Assert.Equal(TripStatus.Completed, trip.Status);
        Assert.Equal(at, trip.ActualEndTime);
        Assert.True(string.IsNullOrEmpty(result.Error));
        await db.SaveChangesAsync();
        Assert.Contains(db.TripStatusHistories, h => h.TripId == trip.Id && h.ToStatus == TripStatus.Completed && h.Source == "geofence_event");
    }

    [Fact]
    public async Task EntryCheckpoint_MarksVisited_WithoutChangingStatus()
    {
        using var db = NewDb("zone_ckpt_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var checkpointGeofence = Guid.NewGuid();
        var trip = NewScheduledTrip(company, Guid.NewGuid(), Guid.NewGuid());
        trip.Status = TripStatus.InProgress;
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = checkpointGeofence, Role = TripGeofenceRole.Checkpoint, SequenceOrder = 1
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var at = DateTime.UtcNow;

        var result = await service.HandleZoneEventAsync(trip.Id, checkpointGeofence, TripZoneEventKind.Entry, at);

        Assert.False(result.StatusChanged);
        Assert.True(string.IsNullOrEmpty(result.Error));
        var link = db.TripGeofences.Single(g => g.TripId == trip.Id);
        Assert.True(link.Visited, "checkpoint entry should mark the link visited");
        Assert.Equal(at, link.VisitedAt);
    }

    [Fact]
    public async Task OutOfOrderCheckpointEntry_SoftWarns_StillMarksVisited()
    {
        using var db = NewDb("zone_ckpt_order_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var ckptA = Guid.NewGuid();
        var ckptB = Guid.NewGuid();
        var trip = NewScheduledTrip(company, Guid.NewGuid(), Guid.NewGuid());
        trip.Status = TripStatus.InProgress;
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = ckptA, Role = TripGeofenceRole.Checkpoint, SequenceOrder = 1
        });
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = ckptB, Role = TripGeofenceRole.Checkpoint, SequenceOrder = 2
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var at = DateTime.UtcNow;

        var result = await service.HandleZoneEventAsync(trip.Id, ckptB, TripZoneEventKind.Entry, at);

        Assert.True(string.IsNullOrEmpty(result.Error));
        Assert.Contains(result.Warnings, w => w.Contains("out of order", StringComparison.OrdinalIgnoreCase));
        var links = db.TripGeofences.Where(g => g.TripId == trip.Id).ToList();
        Assert.True(links.Single(l => l.GeofenceId == ckptB).Visited);
        Assert.NotEqual(true, links.Single(l => l.GeofenceId == ckptA).Visited);
    }

    [Fact]
    public async Task EntryRestrictedZone_RaisesDistinctTenantScopedAlert()
    {
        using var db = NewDb("zone_restricted_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var restrictedGeofence = Guid.NewGuid();
        var trip = NewScheduledTrip(company, vehicle, Guid.NewGuid());
        trip.Status = TripStatus.InProgress;
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = restrictedGeofence, Role = TripGeofenceRole.RestrictedZone
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var at = DateTime.UtcNow;

        var result = await service.HandleZoneEventAsync(trip.Id, restrictedGeofence, TripZoneEventKind.Entry, at);

        Assert.True(string.IsNullOrEmpty(result.Error));
        Assert.False(result.StatusChanged);
        await db.SaveChangesAsync();
        var alert = Assert.Single(db.Alerts);
        Assert.Equal("TripRestrictedZoneViolation", alert.AlertType);
        Assert.Equal(AlertSeverity.High, alert.Severity);
        Assert.Equal(company, alert.CompanyId);
        Assert.Equal(vehicle, alert.VehicleId);
        Assert.Equal(trip.DriverId, alert.DriverId);
    }

    [Fact]
    public async Task CompletingWithUnvisitedCheckpoint_RaisesMissedCheckpointAlert()
    {
        using var db = NewDb("zone_missed_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var checkpointGeofence = Guid.NewGuid();
        var endGeofence = Guid.NewGuid();
        var trip = NewScheduledTrip(company, vehicle, Guid.NewGuid());
        trip.Status = TripStatus.InProgress;
        trip.ActualStartTime = DateTime.UtcNow.AddHours(-1);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = checkpointGeofence, Role = TripGeofenceRole.Checkpoint, SequenceOrder = 1
        });
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = endGeofence, Role = TripGeofenceRole.EndZone
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var at = DateTime.UtcNow;

        var result = await service.HandleZoneEventAsync(trip.Id, endGeofence, TripZoneEventKind.Entry, at);

        Assert.True(result.StatusChanged);
        Assert.Equal(TripStatus.Completed, trip.Status);
        await db.SaveChangesAsync();
        var alert = Assert.Single(db.Alerts);
        Assert.Equal("TripMissedCheckpoint", alert.AlertType);
        Assert.Contains("checkpoint", alert.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(company, alert.CompanyId);
        Assert.Equal(vehicle, alert.VehicleId);
    }

    [Fact]
    public async Task UnrelatedOrWrongDirectionEvents_AreCleanNoOps()
    {
        using var db = NewDb("zone_noop_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var originGeofence = Guid.NewGuid();
        var checkpointGeofence = Guid.NewGuid();
        var trip = NewScheduledTrip(company, Guid.NewGuid(), Guid.NewGuid(), originGeofence);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = originGeofence, Role = TripGeofenceRole.StartZone
        });
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = checkpointGeofence, Role = TripGeofenceRole.Checkpoint, SequenceOrder = 1
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var now = DateTime.UtcNow;

        // Entry (wrong direction) into the origin — only an EXIT starts the trip.
        var r1 = await service.HandleZoneEventAsync(trip.Id, originGeofence, TripZoneEventKind.Entry, now);
        Assert.False(r1.StatusChanged);
        Assert.True(string.IsNullOrEmpty(r1.Error));
        Assert.Empty(r1.Warnings);

        // Exit from a checkpoint — exits only matter for the origin zone.
        var r2 = await service.HandleZoneEventAsync(trip.Id, checkpointGeofence, TripZoneEventKind.Exit, now);
        Assert.False(r2.StatusChanged);
        Assert.True(string.IsNullOrEmpty(r2.Error));

        // A geofence the trip does not link at all.
        var r3 = await service.HandleZoneEventAsync(trip.Id, Guid.NewGuid(), TripZoneEventKind.Entry, now);
        Assert.False(r3.StatusChanged);
        Assert.True(string.IsNullOrEmpty(r3.Error));
        Assert.Empty(r3.Warnings);

        // An unknown trip.
        var r4 = await service.HandleZoneEventAsync(Guid.NewGuid(), originGeofence, TripZoneEventKind.Exit, now);
        Assert.Equal("Trip not found.", r4.Error);
        Assert.False(r4.StatusChanged);
    }

    [Fact]
    public async Task ExitOriginZone_StartsScheduledTrip_WithGeofenceEventSource()
    {
        using var db = NewDb("zone_start_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var originGeofence = Guid.NewGuid();
        var trip = NewScheduledTrip(company, Guid.NewGuid(), Guid.NewGuid(), originGeofence);
        db.Trips.Add(trip);
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = trip.Id, TenantId = company,
            GeofenceId = originGeofence, Role = TripGeofenceRole.StartZone
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);
        var at = DateTime.UtcNow;

        var result = await service.HandleZoneEventAsync(trip.Id, originGeofence, TripZoneEventKind.Exit, at);

        Assert.True(result.StatusChanged, "departing the origin zone should change status");
        Assert.Equal(TripStatus.InProgress, trip.Status);
        Assert.Equal(at, trip.ActualStartTime);
        Assert.True(string.IsNullOrEmpty(result.Error));
        await db.SaveChangesAsync();
        Assert.Contains(db.TripStatusHistories, h => h.TripId == trip.Id && h.ToStatus == TripStatus.InProgress && h.Source == "geofence_event");
    }
}

/// <summary>DB-backed service rules (assignments + scheduling preconditions) on an in-memory store.</summary>
public class TripLifecycleServiceDbTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (Guid Company, Guid Vehicle, Guid Driver, Guid OtherVehicle, Trip Active) SeedActiveTrip(ApplicationDbContext db)
    {
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var driver = Guid.NewGuid();
        var otherVehicle = Guid.NewGuid();
        var active = new Trip
        {
            Id = Guid.NewGuid(), Name = "Active run", Status = TripStatus.InProgress,
            CompanyId = company, TenantId = company,
            VehicleId = vehicle, DriverId = driver,
            StartLocation = "Origin", StartLatitude = 1, StartLongitude = 1,
        };
        db.Trips.Add(active);
        db.SaveChanges();
        return (company, vehicle, driver, otherVehicle, active);
    }

    [Fact]
    public async Task VehicleOnInProgressTrip_IsHardConflict()
    {
        using var db = NewDb("conflict_" + Guid.NewGuid());
        var (company, vehicle, driver, _, _) = SeedActiveTrip(db);
        var service = new TripLifecycleService(db);

        var errors = await service.AssignmentConflictsAsync(company, vehicle, driver);
        Assert.Contains(errors, e => e.Contains("Vehicle is already assigned"));
        Assert.Contains(errors, e => e.Contains("Driver is already assigned"));
    }

    [Fact]
    public async Task FreeVehicleAndDriver_NoConflict()
    {
        using var db = NewDb("free_" + Guid.NewGuid());
        var (company, _, _, otherVehicle, _) = SeedActiveTrip(db);
        var service = new TripLifecycleService(db);
        var freeDriver = Guid.NewGuid();

        var errors = await service.AssignmentConflictsAsync(company, otherVehicle, freeDriver);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Conflict_ExcludingSelf_IsAllowed()
    {
        using var db = NewDb("self_" + Guid.NewGuid());
        var (company, vehicle, driver, _, active) = SeedActiveTrip(db);
        var service = new TripLifecycleService(db);

        var errors = await service.AssignmentConflictsAsync(company, vehicle, driver, active.Id);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SchedulingWithoutGeofence_IsBlocked()
    {
        using var db = NewDb("nogeo_" + Guid.NewGuid());
        var (company, vehicle, driver, _, _) = SeedActiveTrip(db);
        var draft = new Trip
        {
            Id = Guid.NewGuid(), Name = "No-geofence draft", Status = TripStatus.Draft,
            CompanyId = company, TenantId = company,
            VehicleId = vehicle, DriverId = driver, // the active trip holds these — irrelevant here
            StartLocation = "A", StartLatitude = 1, StartLongitude = 1,
        };
        db.Trips.Add(draft);
        db.TripWaypoints.AddRange(
            new TripWaypoint { Id = Guid.NewGuid(), TripId = draft.Id, SequenceOrder = 1, Name = "A", Latitude = 1, Longitude = 1 },
            new TripWaypoint { Id = Guid.NewGuid(), TripId = draft.Id, SequenceOrder = 2, Name = "B", Latitude = 2, Longitude = 2 });
        db.SaveChanges();
        var service = new TripLifecycleService(db);

        var errors = await service.SchedulingPreconditionsAsync(draft.Id);
        Assert.Contains(errors, e => e.Contains("at least one linked geofence"));
    }

    [Fact]
    public async Task SchedulingWithDirectGeofence_PassesPreconditions()
    {
        using var db = NewDb("withgeo_" + Guid.NewGuid());
        var (company, vehicle, driver, _, _) = SeedActiveTrip(db);
        var draft = new Trip
        {
            Id = Guid.NewGuid(), Name = "Ready draft", Status = TripStatus.Draft,
            CompanyId = company, TenantId = company,
            VehicleId = Guid.NewGuid(), DriverId = Guid.NewGuid(), // free pair
            StartLocation = "A", StartLatitude = 1, StartLongitude = 1,
        };
        db.Trips.Add(draft);
        db.TripWaypoints.AddRange(
            new TripWaypoint { Id = Guid.NewGuid(), TripId = draft.Id, SequenceOrder = 1, Name = "A", Latitude = 1, Longitude = 1 },
            new TripWaypoint { Id = Guid.NewGuid(), TripId = draft.Id, SequenceOrder = 2, Name = "B", Latitude = 2, Longitude = 2 });
        db.TripGeofences.Add(new TripGeofence
        {
            Id = Guid.NewGuid(), TripId = draft.Id, TenantId = company,
            GeofenceId = Guid.NewGuid(), Role = TripGeofenceRole.Checkpoint, SequenceOrder = 1
        });
        db.SaveChanges();
        var service = new TripLifecycleService(db);

        var errors = await service.SchedulingPreconditionsAsync(draft.Id);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task CancelWithoutReason_IsRejected()
    {
        using var db = NewDb("cancel_" + Guid.NewGuid());
        var trip = new Trip
        {
            Id = Guid.NewGuid(), Name = "Cancel me", Status = TripStatus.Scheduled,
            CompanyId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(), DriverId = Guid.NewGuid(),
            StartLocation = "A", StartLatitude = 1, StartLongitude = 1,
        };
        db.Trips.Add(trip);
        db.SaveChanges();
        var service = new TripLifecycleService(db);

        var errors = await service.TransitionAsync(trip, TripStatus.Cancelled, null, "manual", "tester");
        Assert.Contains(errors, e => e.Contains("reason is required"));
        Assert.Equal(TripStatus.Scheduled, trip.Status); // unchanged
    }

    [Fact]
    public async Task CompletedTrip_IsTerminal()
    {
        using var db = NewDb("terminal_" + Guid.NewGuid());
        var trip = new Trip
        {
            Id = Guid.NewGuid(), Name = "Done", Status = TripStatus.Completed,
            CompanyId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(), DriverId = Guid.NewGuid(),
            StartLocation = "A", StartLatitude = 1, StartLongitude = 1,
        };
        db.Trips.Add(trip);
        db.SaveChanges();
        var service = new TripLifecycleService(db);

        var errors = await service.TransitionAsync(trip, TripStatus.Cancelled, "why", "manual", "tester");
        Assert.Contains(errors, e => e.Contains("no further transitions"));
    }

    [Fact]
    public async Task Complete_WithTelemetry_AggregatesMetrics()
    {
        using var db = NewDb("metrics_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var trip = new Trip
        {
            Id = Guid.NewGuid(), Name = "Measure", Status = TripStatus.InProgress,
            CompanyId = company, TenantId = company,
            VehicleId = vehicle, DriverId = Guid.NewGuid(),
            StartLocation = "A", StartLatitude = 0, StartLongitude = 0,
            ActualStartTime = DateTime.UtcNow.AddMinutes(-10),
        };
        db.Trips.Add(trip);
        // ~11.1 km north along a meridian = 0.1° lat; speeds 60 & 80 km/h.
        var t0 = trip.ActualStartTime!.Value;
        db.TelemetryEvents.AddRange(
            new TelemetryEvent { Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle, EventTimeUtc = t0, Latitude = 0, Longitude = 0, SpeedKmh = 60 },
            new TelemetryEvent { Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle, EventTimeUtc = t0.AddMinutes(1), Latitude = 0.05, Longitude = 0, SpeedKmh = 80 },
            new TelemetryEvent { Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle, EventTimeUtc = t0.AddMinutes(2), Latitude = 0.1, Longitude = 0, SpeedKmh = 70 });
        db.SaveChanges();
        var service = new TripLifecycleService(db);

        var errors = await service.TransitionAsync(trip, TripStatus.Completed, null, "manual", "tester");
        Assert.Empty(errors);
        Assert.Equal(TripStatus.Completed, trip.Status);
        Assert.NotNull(trip.ActualDistance);
        // Two ~5.56 km hops ≈ 11.1 km total.
        Assert.InRange(trip.ActualDistance!.Value, 10.5m, 11.8m);
        Assert.Equal(80m, trip.MaxSpeed);
        Assert.NotNull(trip.ActualDuration);
        await db.SaveChangesAsync();
        // A history row was written for the transition.
        Assert.True(db.TripStatusHistories.Any(h => h.TripId == trip.Id && h.ToStatus == TripStatus.Completed));
    }
}