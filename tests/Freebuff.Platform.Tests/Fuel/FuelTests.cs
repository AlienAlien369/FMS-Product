using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Fuel;

/// <summary>
/// Fuel Management module:
///   - manual fill-up efficiency is computed against the previous transaction
///     (or the vehicle's odometer baseline for the first one)
///   - sensor-derived drop + no distance + engine never off → theft alert
///   - legitimate refuel (engine off) and distance-covered drops do NOT alert
///   - low level alerts below the company threshold
///   - efficiency degradation alerts vs the vehicle's own baseline
///   - entitlement gating suppresses alerts for non-entitled companies
/// </summary>
public class FuelServiceTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("fuel_" + name + "_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationDbContext Db, Guid CompanyId, Guid VehicleId)> SeedVehicleAsync(
        string name, decimal? tankCapacity = null, long? odometer = null)
    {
        var db = NewDb(name);
        var company = Guid.NewGuid();
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(), RegistrationNumber = $"V-{name}", CompanyId = company,
            FuelType = FuelType.Diesel, FuelTankCapacity = tankCapacity,
            OdometerReading = odometer, EngineHours = 0
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (db, company, vehicle.Id);
    }

    // ── Manual fill-ups: efficiency computation ────────────────────────────

    [Fact]
    public async Task ManualFillUp_Efficiency_AgainstPreviousTransaction()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("m1", odometer: 1000);
        var service = new FuelService(db);

        var first = new CreateFuelRecordDto { VehicleId = vehicleId, Quantity = 50, OdometerReading = 1000, RecordDate = DateTime.UtcNow.AddDays(-10) };
        var r1 = await service.CreateManualAsync(company, first, null);
        Assert.Null(r1.EfficiencyKmPerLiter); // first transaction — no distance yet

        var second = new CreateFuelRecordDto { VehicleId = vehicleId, Quantity = 50, OdometerReading = 1600, RecordDate = DateTime.UtcNow };
        var r2 = await service.CreateManualAsync(company, second, null);

        Assert.Equal(600m, r2.DistanceTraveledKm);
        Assert.Equal(12m, r2.EfficiencyKmPerLiter);   // 600 km / 50 L
        Assert.Equal(8.33m, r2.ConsumptionLitersPer100km); // 100/12
    }

    [Fact]
    public async Task ManualFillUp_FirstTransaction_UsesVehicleOdometerBaseline()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("m2", odometer: 1000);
        var service = new FuelService(db);

        var dto = new CreateFuelRecordDto { VehicleId = vehicleId, Quantity = 50, OdometerReading = 1450 };
        var record = await service.CreateManualAsync(company, dto, null);

        Assert.Equal(450m, record.DistanceTraveledKm);
        Assert.Equal(9m, record.EfficiencyKmPerLiter);
    }

    [Fact]
    public async Task ManualFillUp_AdvancesVehicleOdometer()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("m3", odometer: 1000);
        var service = new FuelService(db);

        var dto = new CreateFuelRecordDto { VehicleId = vehicleId, Quantity = 40, OdometerReading = 2100 };
        await service.CreateManualAsync(company, dto, null);

        var vehicle = await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == vehicleId);
        Assert.Equal(2100, vehicle.OdometerReading);
    }

    // ── Sensor-derived: theft detection ────────────────────────────────────

    [Fact]
    public async Task SensorDrop_NoDistance_EngineOn_FiresTheftAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("theft1", tankCapacity: 100);
        db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            EventTimeUtc = DateTime.UtcNow.AddMinutes(-10),
            FuelLevelLiters = 80, OdometerKm = 1000, Ignition = true
        });
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        // 25 L drop (≥ max(8, 15% of 100)), 1 km traveled, engine still on → theft.
        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            55, 55, 1001, true, true, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == FuelConsumptionAnalyzer.TheftAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.High, alert!.Severity);

        // The snapshot itself is flagged as an anomaly.
        var snapshot = await db.FuelConsumptionSnapshots.AsNoTracking()
            .OrderByDescending(s => s.EventTimeUtc).FirstAsync();
        Assert.True(snapshot.IsAnomaly);
    }

    [Fact]
    public async Task SensorDrop_WithDistance_NoTheftAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("theft2", tankCapacity: 100);
        db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            EventTimeUtc = DateTime.UtcNow.AddMinutes(-10),
            FuelLevelLiters = 80, OdometerKm = 1000, Ignition = true
        });
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        // Same 25 L drop but 60 km of distance — normal driving consumption.
        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            55, 55, 1060, true, true, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == FuelConsumptionAnalyzer.TheftAlertCode).ToListAsync());
        var snapshot = await db.FuelConsumptionSnapshots.AsNoTracking()
            .OrderByDescending(s => s.EventTimeUtc).FirstAsync();
        Assert.False(snapshot.IsAnomaly);
    }

    [Fact]
    public async Task SensorDrop_EngineOff_IsRefuel_NoTheftAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("theft3", tankCapacity: 100);
        db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            EventTimeUtc = DateTime.UtcNow.AddMinutes(-10),
            FuelLevelLiters = 80, OdometerKm = 1000, Ignition = true
        });
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        // Drop while the engine is OFF = legitimate station refueling event.
        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            55, 55, 1001, false, false, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == FuelConsumptionAnalyzer.TheftAlertCode).ToListAsync());
    }

    [Fact]
    public async Task Refuel_LevelRises_EngineOff_MaterializesSensorTransaction()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("refuel", tankCapacity: 100);
        db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            EventTimeUtc = DateTime.UtcNow.AddMinutes(-10),
            FuelLevelLiters = 40, OdometerKm = 1000, Ignition = true
        });
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            85, 85, 1001, false, false, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var transaction = await db.FuelRecords.FirstOrDefaultAsync(f => f.Source == FuelRecord.SourceSensor);
        Assert.NotNull(transaction);
        Assert.Equal(45m, transaction!.Quantity); // 85 - 40
    }

    // ── Low level ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LowLevel_BelowDefaultThreshold_FiresAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("low1", tankCapacity: 100);
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            8, 8, 1200, true, true, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == FuelConsumptionAnalyzer.LowLevelAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Low, alert!.Severity);
    }

    [Fact]
    public async Task LowLevel_AboveThreshold_NoAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("low2", tankCapacity: 100);
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            30, 30, 1200, true, true, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == FuelConsumptionAnalyzer.LowLevelAlertCode).ToListAsync());
    }

    // ── Efficiency degradation vs the vehicle's own baseline ───────────────

    [Fact]
    public async Task EfficiencyWorseThanBaseline_FiresDegradationAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("eff1", tankCapacity: 100);
        var now = DateTime.UtcNow;
        // Three normal pairs at ~10 L/100km: 10 L over 100 km each. The most
        // recent (30 min back) is level 60 @ 1200 km — the current reading
        // compares against it.
        for (var i = 0; i < 3; i++)
        {
            db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
            {
                Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
                EventTimeUtc = now.AddMinutes(-30 + 10 * i),
                FuelLevelLiters = 80 - 10 * i, OdometerKm = 1000 + 100 * i,
                Ignition = true, LitersConsumed = 10, DistanceKm = 100,
                ConsumptionLitersPer100km = 10
            });
        }
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        // Current pair: previous reading was 60 L at 1200 km → drop 15 L over
        // 100 km = 15 L/100km → 50% worse than the 10 L/100km baseline.
        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            45, 45, 1300, true, true, null, null, now);
        await db.SaveChangesAsync();

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == FuelConsumptionAnalyzer.EfficiencyAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Medium, alert!.Severity);
        Assert.Contains("50% worse", alert.Message);
    }

    [Fact]
    public async Task EfficiencyWithinBaseline_NoDegradationAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("eff2", tankCapacity: 100);
        var now = DateTime.UtcNow;
        for (var i = 1; i <= 3; i++)
        {
            db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
            {
                Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
                EventTimeUtc = now.AddMinutes(-10 * i),
                FuelLevelLiters = 80 - 10 * (i - 1), OdometerKm = 1000 + 100 * (i - 1),
                Ignition = true, LitersConsumed = 10, DistanceKm = 100,
                ConsumptionLitersPer100km = 10
            });
        }
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new AlwaysEntitledAlertEnforcement());

        // 11 L/100km — within the 25% degradation band.
        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            59, 59, 1300, true, true, null, null, now);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == FuelConsumptionAnalyzer.EfficiencyAlertCode).ToListAsync());
    }

    // ── Entitlement ────────────────────────────────────────────────────────

    [Fact]
    public async Task NotEntitled_CompanyNeverGetsFuelAlerts()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("deny", tankCapacity: 100);
        db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            EventTimeUtc = DateTime.UtcNow.AddMinutes(-10),
            FuelLevelLiters = 80, OdometerKm = 1000, Ignition = true
        });
        await db.SaveChangesAsync();
        var analyzer = new FuelConsumptionAnalyzer(db, new DenyAllAlertEnforcement());

        await analyzer.ProcessSnapshotAsync(company, vehicleId, Guid.NewGuid(),
            55, 55, 1001, true, true, null, null, DateTime.UtcNow);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.ToListAsync());
    }
}