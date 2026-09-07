using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Ingestion.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Sensors;

/// <summary>
/// Threshold-based sensor alerting (Speed Governor + TPMS):
///   - alerts fire on the POLICY limit, never the hardware governor limit
///   - per-vehicle override wins over the company fleet default
///   - tyre severity tiers (warning ≥10% out, critical ≥20% out or rapid loss)
///   - rapid loss flags critical even when the static check would only warn
///   - severity-aware throttle + entitlement gating
/// </summary>
public class SensorPolicyAlertProducerTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationDbContext Db, Guid CompanyId, Guid VehicleId)> SeedVehicleAsync(
        string name, double? speedPolicy = null, double? tyreMin = null, double? tyreMax = null)
    {
        var db = NewDb("seed_" + name + "_" + Guid.NewGuid().ToString("N"));
        var company = Guid.NewGuid();
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(), RegistrationNumber = $"V-{name}", CompanyId = company,
            SpeedPolicyMaxKmh = speedPolicy, TyrePressureMinBar = tyreMin, TyrePressureMaxBar = tyreMax
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (db, company, vehicle.Id);
    }

    private static async Task SetCompanySpeedPolicyAsync(ApplicationDbContext db, Guid company, double maxKmh)
    {
        db.Configurations.Add(new Configuration
        {
            Id = Guid.NewGuid(), Key = FleetPolicyService.SpeedPolicyKey,
            Value = maxKmh.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Scope = ConfigurationScope.Company, ScopeEntityId = company, CompanyId = company, Module = "fleet"
        });
        await db.SaveChangesAsync();
    }

    private static SensorPolicyAlertProducer Producer(ApplicationDbContext db,
        IAlertTypeEnforcement? enforcement = null, Func<DateTime>? clock = null)
        => new(db, enforcement ?? new AlwaysEntitledAlertEnforcement(), new FleetPolicyService(db), null, clock);

    private static NormalizedTyrePressure Tyre(string position, double bar)
        => new() { Position = position, PressureBar = bar };

    // ── Speed: policy threshold, not governor limit ────────────────────────

    [Fact]
    public async Task SpeedAbovePolicy_Alerts_EvenThoughGovernorLimitNotCrossed()
    {
        var (db, company, vehicleId) = await SeedVehicleWithGovernorAsync("speed1", speedPolicy: 60, governor: 80);
        var producer = Producer(db);

        // 70 km/h: above the 60 policy, below the 80 governor → must alert on policy.
        await producer.ProcessSnapshotAsync(company, vehicleId, 70, Array.Empty<NormalizedTyrePressure>(), null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == SensorPolicyAlertProducer.SpeedAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.High, alert!.Severity);
        Assert.Contains("60", alert.Message); // the policy value, not the governor

        // 55 km/h: below both → no alert.
        var producer2 = Producer(db);
        await producer2.ProcessSnapshotAsync(company, vehicleId, 55, Array.Empty<NormalizedTyrePressure>(), null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        Assert.Single(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.SpeedAlertCode).ToListAsync());
    }

    [Fact]
    public async Task SpeedAlert_NotFired_WhenOnlyGovernorLowerThanSpeed()
    {
        // Company policy is unset (no speed policy) — a governor-limited vehicle
        // must NOT alert: without a policy there is nothing to enforce.
        var (db, company, vehicleId) = await SeedVehicleWithGovernorAsync("speed2", speedPolicy: null, governor: 60);
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 70, Array.Empty<NormalizedTyrePressure>(), null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.SpeedAlertCode).ToListAsync());
    }

    [Fact]
    public async Task VehicleOverride_Wins_OverCompanyDefault()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("prec", speedPolicy: 45);
        await SetCompanySpeedPolicyAsync(db, company, 80);
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 50, Array.Empty<NormalizedTyrePressure>(), null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        // 50 > 45 (override) but < 80 (default) → the override governs.
        Assert.Single(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.SpeedAlertCode).ToListAsync());
    }

    [Fact]
    public async Task CompanyDefault_Used_WhenNoVehicleOverride()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("def", speedPolicy: null);
        await SetCompanySpeedPolicyAsync(db, company, 60);
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 65, Array.Empty<NormalizedTyrePressure>(), null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Single(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.SpeedAlertCode).ToListAsync());
    }

    // ── Tyre pressure: tiers + rapid loss ──────────────────────────────────

    [Fact]
    public async Task TyreWithinRange_NoAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("tyre_ok", tyreMin: 5.0, tyreMax: 7.0);
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 40,
            new[] { Tyre("front_left", 6.0), Tyre("rear_right", 6.5) }, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode).ToListAsync());
    }

    [Fact]
    public async Task TyreWarningTier_AlertsMedium()
    {
        // 4.4 vs min 5.0 → 12% below → warning tier (Medium).
        var (db, company, vehicleId) = await SeedVehicleAsync("tyre_warn", tyreMin: 5.0, tyreMax: 7.0);
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 40,
            new[] { Tyre("front_left", 4.4) }, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Medium, alert!.Severity);
        Assert.Contains("12% below", alert.Message);
    }

    [Fact]
    public async Task TyreCriticalTier_AlertsHigh()
    {
        // 3.9 vs min 5.0 → 22% below → critical tier (High).
        var (db, company, vehicleId) = await SeedVehicleAsync("tyre_crit", tyreMin: 5.0, tyreMax: 7.0);
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 40,
            new[] { Tyre("front_left", 3.9) }, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.High, alert!.Severity);
    }

    [Fact]
    public async Task RapidLoss_FlagsCritical_FasterThanStaticCheck()
    {
        // Static check on 4.4 vs min 5.0 says WARNING (12% below). But the
        // previous reading 1 minute ago was 6.0 → 27% drop → rapid loss →
        // critical. This is the blowout-in-progress case a static check misses.
        var (db, company, vehicleId) = await SeedVehicleAsync("tyre_rapid", tyreMin: 5.0, tyreMax: 7.0);
        var now = DateTime.UtcNow;
        db.TyrePressureReadings.Add(new TyrePressureReading
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            Position = TyrePosition.FrontLeft, PressureBar = 6.0,
            EventTimeUtc = now.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 40,
            new[] { Tyre("front_left", 4.4) }, null, null, now);
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.High, alert!.Severity);
        Assert.Contains("rapid pressure loss", alert.Message);
    }

    [Fact]
    public async Task NoTyrePolicy_NoTyreAlerts_EvenWhenReadingsAnomalous()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("tyre_nopolicy");
        var producer = Producer(db);

        await producer.ProcessSnapshotAsync(company, vehicleId, 40,
            new[] { Tyre("front_left", 1.0) }, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode).ToListAsync());
    }

    // ── Throttle + entitlement ─────────────────────────────────────────────

    [Fact]
    public async Task Throttle_SuppressesRepeat_ButEscalationFires()
    {
        var clock = () => DateTime.UtcNow;
        var (db, company, vehicleId) = await SeedVehicleAsync("throttle", tyreMin: 5.0, tyreMax: 7.0);
        var producer = Producer(db, clock: clock);

        // Warning snapshot (12% below), then another warning within the window → suppressed.
        // (Save between snapshots, mirroring DeviceIngestionService's commit-per-ingest.)
        await producer.ProcessSnapshotAsync(company, vehicleId, 40, new[] { Tyre("front_left", 4.4) }, null, null, clock());
        await db.SaveChangesAsync();
        await producer.ProcessSnapshotAsync(company, vehicleId, 40, new[] { Tyre("front_left", 4.4) }, null, null, clock());
        await db.SaveChangesAsync();
        Assert.Single(await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode).ToListAsync());

        // Escalation to critical (22% below) within the same window → fires
        // (a warning never suppresses a critical).
        await producer.ProcessSnapshotAsync(company, vehicleId, 40, new[] { Tyre("front_left", 3.9) }, null, null, clock());
        await db.SaveChangesAsync();
        var alerts = await db.Alerts.Where(a => a.AlertType == SensorPolicyAlertProducer.TyreAlertCode).ToListAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(AlertSeverity.High, alerts[^1].Severity);
    }

    [Fact]
    public async Task NotEntitled_CompanyNeverGetsTheAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("deny", speedPolicy: 60);
        var producer = Producer(db, new DenyAllAlertEnforcement());

        await producer.ProcessSnapshotAsync(company, vehicleId, 90, Array.Empty<NormalizedTyrePressure>(), null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.ToListAsync());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task<(ApplicationDbContext Db, Guid CompanyId, Guid VehicleId)> SeedVehicleWithGovernorAsync(
        string name, double? speedPolicy = null, double? governor = null, double? tyreMin = null, double? tyreMax = null)
    {
        var (db, company, vehicleId) = await SeedVehicleAsync(name, speedPolicy: speedPolicy, tyreMin: tyreMin, tyreMax: tyreMax);
        // Governor limit lives on the telemetry stream, not the vehicle — seed a
        // recent event so the producer context is realistic.
        db.TelemetryEvents.Add(new TelemetryEvent
        {
            Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicleId,
            EventTimeUtc = DateTime.UtcNow, SpeedKmh = 0, SpeedGovernorLimitKmh = governor
        });
        await db.SaveChangesAsync();
        return (db, company, vehicleId);
    }
}

/// <summary>Retention: raw readings fold into hourly rollups, then drop; rollups expire after 12 months.</summary>
public class SensorRetentionTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task RunRetention_RollsUpOldRaw_AndPurgesBeyondWindows()
    {
        var db = NewDb("ret_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var oldHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddDays(-40);

        // Raw speed + tyre rows from 40 days ago (beyond the 30-day raw window).
        var oldEvent = new TelemetryEvent
        {
            Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle,
            EventTimeUtc = oldHour, SpeedKmh = 50
        };
        db.TelemetryEvents.Add(oldEvent);
        db.TyrePressureReadings.Add(new TyrePressureReading
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicle,
            TelemetryEventId = oldEvent.Id, Position = TyrePosition.FrontLeft,
            PressureBar = 6.0, EventTimeUtc = oldHour
        });
        // A recent raw row (inside the window) must survive.
        db.TelemetryEvents.Add(new TelemetryEvent
        {
            Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle,
            EventTimeUtc = now, SpeedKmh = 42
        });
        // An expired rollup (older than 12 months) must go.
        db.TelemetryRollupsHourly.Add(new TelemetryRollupHourly
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicle, SensorType = "speed",
            HourBucketUtc = now.AddMonths(-13), MinValue = 1, MaxValue = 2, AvgValue = 1.5, ReadingCount = 1
        });
        await db.SaveChangesAsync();

        await SensorRetentionService.RunRetentionAsync(db, now.AddDays(-30), now.AddMonths(-12));

        // Old raw event purged, recent kept.
        Assert.DoesNotContain(db.TelemetryEvents, e => e.Id == oldEvent.Id);
        Assert.Single(db.TelemetryEvents);
        // Old tyre readings purged with their event.
        Assert.Empty(db.TyrePressureReadings);
        // Rollups created for the old speed bucket AND the old tyre bucket;
        // the expired (13-month-old) rollup is purged.
        Assert.Equal(2, db.TelemetryRollupsHourly.Count());
        var rollup = db.TelemetryRollupsHourly.Single(r => r.SensorType == "speed");
        Assert.Equal(50, rollup.MinValue);
        Assert.Equal(50, rollup.MaxValue);
        Assert.Null(rollup.TyrePosition);
        var tyreRollup = db.TelemetryRollupsHourly.Single(r => r.SensorType == "tyre");
        Assert.Equal(6.0, tyreRollup.AvgValue);
        Assert.Equal(TyrePosition.FrontLeft, tyreRollup.TyrePosition);
    }

    [Fact]
    public async Task ReRun_DoesNotDoubleCountRollups()
    {
        var db = NewDb("ret2_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var oldHour = DateTime.UtcNow.AddDays(-40);
        db.TelemetryEvents.Add(new TelemetryEvent
        {
            Id = Guid.NewGuid(), TenantId = company, DeviceId = Guid.NewGuid(), VehicleId = vehicle,
            EventTimeUtc = oldHour, SpeedKmh = 60
        });
        await db.SaveChangesAsync();

        var cutoff = DateTime.UtcNow.AddDays(-30);
        await SensorRetentionService.RunRetentionAsync(db, cutoff, DateTime.UtcNow.AddMonths(-12));
        await SensorRetentionService.RunRetentionAsync(db, cutoff, DateTime.UtcNow.AddMonths(-12));

        Assert.Single(db.TelemetryRollupsHourly);
        Assert.Equal(1, db.TelemetryRollupsHourly.Single().ReadingCount);
    }
}