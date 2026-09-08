using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Maintenance;

/// <summary>
/// Maintenance Management module:
///   - mileage-based schedules transition ok → due soon → overdue as the
///     odometer advances, firing the matching alerts
///   - completing a preventive service against a schedule rolls the next-due
///     point forward from the service odometer
///   - time-based schedules transition on the calendar
///   - logging a breakdown raises maintenance.breakdown_logged
///   - entitlement gating suppresses due/breakdown alerts
/// </summary>
public class MaintenanceServiceTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("maint_" + name + "_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationDbContext Db, Guid CompanyId, Guid VehicleId)> SeedVehicleAsync(
        string name, long? odometer = null, long? engineHours = null)
    {
        var db = NewDb(name);
        var company = Guid.NewGuid();
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(), RegistrationNumber = $"V-{name}", CompanyId = company,
            OdometerReading = odometer, EngineHours = engineHours
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        return (db, company, vehicle.Id);
    }

    private static MaintenanceService Service(ApplicationDbContext db,
        IAlertTypeEnforcement? enforcement = null, Func<DateTime>? clock = null)
        => new(db, enforcement ?? new AlwaysEntitledAlertEnforcement(), null, clock);

    // ── Mileage-based schedule lifecycle ────────────────────────────────────

    [Fact]
    public async Task MileageSchedule_TransitionsDueSoon_ThenOverdue_FiresAlerts()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("mile1", odometer: 1000);
        var service = Service(db);

        var schedule = await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Engine oil change", ServiceType = "oil_change",
            TriggerType = MaintenanceTriggerType.MileageInterval,
            IntervalValue = 10000, DueLeadValue = 500
        }, null);
        Assert.Equal(11000m, schedule.NextDueOdometer); // seeded from current odometer

        // 10,500 km → 500 remaining = exactly the lead window → due soon.
        await service.EvaluateVehicleAsync(company, vehicleId, odometerKm: 10500);
        await db.SaveChangesAsync();
        var dueSoon = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.DueSoonAlertCode);
        Assert.NotNull(dueSoon);
        Assert.Equal(AlertSeverity.Low, dueSoon!.Severity);

        // 11,100 km → 100 past due → overdue.
        await service.EvaluateVehicleAsync(company, vehicleId, odometerKm: 11100);
        await db.SaveChangesAsync();
        var overdue = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.OverdueAlertCode);
        Assert.NotNull(overdue);
        Assert.Equal(AlertSeverity.High, overdue!.Severity);
        Assert.Contains("overdue by 100 km", overdue.Message);
    }

    [Fact]
    public async Task MileageSchedule_WithinWindow_NoAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("mile2", odometer: 1000);
        var service = Service(db);

        await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Tyre rotation", ServiceType = "tire_rotation",
            TriggerType = MaintenanceTriggerType.MileageInterval,
            IntervalValue = 10000, DueLeadValue = 500
        }, null);

        // 10,200 km → 800 remaining, beyond the 500 lead → OK, no alert.
        await service.EvaluateVehicleAsync(company, vehicleId, odometerKm: 10200);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == MaintenanceService.DueSoonAlertCode
            || a.AlertType == MaintenanceService.OverdueAlertCode).ToListAsync());
    }

    [Fact]
    public async Task LogPreventiveRecord_RollsNextDueForward_FromServiceOdometer()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("roll1", odometer: 1000);
        var service = Service(db);

        var schedule = await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Engine oil change", ServiceType = "oil_change",
            TriggerType = MaintenanceTriggerType.MileageInterval, IntervalValue = 10000
        }, null);

        // Service completed at 12,000 km → next due = 12,000 + 10,000.
        await service.LogRecordAsync(company, new LogMaintenanceRecordDto
        {
            VehicleId = vehicleId, Title = "Engine oil change",
            RecordType = MaintenanceRecordType.Preventive,
            MaintenanceScheduleId = schedule.Id,
            OdometerAtService = 12000
        }, null);

        var updated = await db.MaintenanceSchedules.AsNoTracking().FirstAsync(s => s.Id == schedule.Id);
        Assert.Equal(12000m, updated.LastServiceOdometer);
        Assert.Equal(22000m, updated.NextDueOdometer);

        // Vehicle odometer mirrored forward too.
        var vehicle = await db.Vehicles.AsNoTracking().FirstAsync(v => v.Id == vehicleId);
        Assert.Equal(12000, vehicle.OdometerReading);
    }

    [Fact]
    public async Task LogPreventiveRecord_CompletingSchedule_MovesItBackToOk()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("roll2", odometer: 1000);
        var service = Service(db);

        var schedule = await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Brake inspection", ServiceType = "brake_inspection",
            TriggerType = MaintenanceTriggerType.MileageInterval, IntervalValue = 20000
        }, null);

        // Overdue at 21,500 km...
        await service.EvaluateVehicleAsync(company, vehicleId, odometerKm: 21500);
        await db.SaveChangesAsync();
        Assert.NotNull(await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.OverdueAlertCode));

        // ...until the service is completed at that odometer → next due = 41,500.
        await service.LogRecordAsync(company, new LogMaintenanceRecordDto
        {
            VehicleId = vehicleId, Title = "Brake inspection",
            RecordType = MaintenanceRecordType.Preventive,
            MaintenanceScheduleId = schedule.Id,
            OdometerAtService = 21500
        }, null);

        var detail = await service.GetVehicleDetailAsync(null, vehicleId);
        Assert.Equal(MaintenanceDueStatus.Ok, detail.Schedules.Single().DueStatus);
    }

    // ── Time-based schedule ────────────────────────────────────────────────

    [Fact]
    public async Task TimeSchedule_TransitionsOnCalendar()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("time1", odometer: 5000);
        var now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = Service(db, clock: () => now);

        await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Annual inspection", ServiceType = "general_inspection",
            TriggerType = MaintenanceTriggerType.TimeInterval,
            IntervalValue = 180, DueLeadValue = 7
        }, null);

        // 175 days later (5 days before due) → due soon.
        await service.EvaluateVehicleAsync(company, vehicleId, at: now.AddDays(175));
        await db.SaveChangesAsync();
        Assert.NotNull(await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.DueSoonAlertCode));

        // 181 days later (1 day past due) → overdue.
        await service.EvaluateVehicleAsync(company, vehicleId, at: now.AddDays(181));
        await db.SaveChangesAsync();
        Assert.NotNull(await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.OverdueAlertCode));
    }

    // ── Engine-hours schedule ──────────────────────────────────────────────

    [Fact]
    public async Task EngineHoursSchedule_EvaluatesAgainstEngineHours()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("hours1", odometer: 0, engineHours: 100);
        var service = Service(db);

        await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Hydraulic service", ServiceType = "custom",
            TriggerType = MaintenanceTriggerType.EngineHoursInterval,
            IntervalValue = 500, DueLeadValue = 50
        }, null);

        // 610 hours → 10 over the 600 due point → overdue.
        await service.EvaluateVehicleAsync(company, vehicleId, engineHours: 610);
        await db.SaveChangesAsync();
        Assert.NotNull(await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.OverdueAlertCode));
    }

    // ── Breakdown records ──────────────────────────────────────────────────

    [Fact]
    public async Task LogBreakdown_FiresBreakdownAlert_AndFlagsRecord()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("bd1", odometer: 5000);
        var service = Service(db);

        var record = await service.LogRecordAsync(company, new LogMaintenanceRecordDto
        {
            VehicleId = vehicleId, Title = "Water pump failure",
            RecordType = MaintenanceRecordType.Breakdown,
            BreakdownSeverity = "major", DowntimeHours = 6,
            RootCause = "Failed water pump — belt snapped",
            OdometerAtService = 5120
        }, null);

        Assert.Equal(MaintenanceRecordType.Breakdown, record.RecordType);
        Assert.Equal("major", record.BreakdownSeverity);
        Assert.Equal(6m, record.DowntimeHours);

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.AlertType == MaintenanceService.BreakdownAlertCode);
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.High, alert!.Severity);
        Assert.Contains("Water pump", alert.Title);
    }

    [Fact]
    public async Task PreventiveRecord_NoBreakdownAlert()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("bd2", odometer: 5000);
        var service = Service(db);

        await service.LogRecordAsync(company, new LogMaintenanceRecordDto
        {
            VehicleId = vehicleId, Title = "Oil change",
            RecordType = MaintenanceRecordType.Preventive,
            OdometerAtService = 5120
        }, null);

        Assert.Empty(await db.Alerts.Where(a => a.AlertType == MaintenanceService.BreakdownAlertCode).ToListAsync());
    }

    // ── Entitlement ────────────────────────────────────────────────────────

    [Fact]
    public async Task NotEntitled_NoDueOrBreakdownAlerts()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("deny", odometer: 1000);
        var service = Service(db, new DenyAllAlertEnforcement());

        await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Oil change", ServiceType = "oil_change",
            TriggerType = MaintenanceTriggerType.MileageInterval, IntervalValue = 10000
        }, null);

        await service.EvaluateVehicleAsync(company, vehicleId, odometerKm: 15000);
        await db.SaveChangesAsync();

        await service.LogRecordAsync(company, new LogMaintenanceRecordDto
        {
            VehicleId = vehicleId, Title = "Clutch failure",
            RecordType = MaintenanceRecordType.Breakdown,
            BreakdownSeverity = "critical"
        }, null);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Alerts.ToListAsync());
    }

    // ── Fleet overview sorting ─────────────────────────────────────────────

    [Fact]
    public async Task FleetOverview_SortsOverdueFirst()
    {
        var (db, company, vehicleId) = await SeedVehicleAsync("ov1", odometer: 1000);
        var company2 = Guid.NewGuid();
        var vehicle2 = new Vehicle { Id = Guid.NewGuid(), RegistrationNumber = "V-ov2", CompanyId = company2 };
        db.Vehicles.Add(vehicle2);
        await db.SaveChangesAsync();
        var service = Service(db);

        // Vehicle 1: overdue. Vehicle 2: OK.
        await service.CreateScheduleAsync(company, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicleId, Title = "Oil change", ServiceType = "oil_change",
            TriggerType = MaintenanceTriggerType.MileageInterval, IntervalValue = 10000
        }, null);
        await service.CreateScheduleAsync(company2, new CreateMaintenanceScheduleDto
        {
            VehicleId = vehicle2.Id, Title = "Tyre rotation", ServiceType = "tire_rotation",
            TriggerType = MaintenanceTriggerType.MileageInterval, IntervalValue = 10000
        }, null);
        await db.SaveChangesAsync();

        await service.EvaluateVehicleAsync(company, vehicleId, odometerKm: 12000);
        await db.SaveChangesAsync();

        // Live telemetry says vehicle 1 has driven past the 11,000 km due point
        // (the overview reads TelemetryState, not the stale Vehicle row).
        db.TelemetryStates.Add(new TelemetryState
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = vehicleId,
            DeviceId = Guid.NewGuid(), EventTimeUtc = DateTime.UtcNow,
            OdometerKm = 12000, EngineHours = 100
        });
        await db.SaveChangesAsync();

        var overview = await service.GetFleetOverviewAsync(null);
        Assert.Equal(2, overview.TotalSchedules);
        Assert.Equal(1, overview.OverdueCount);
        Assert.Equal("Oil change", overview.Schedules[0].Title); // overdue first
        Assert.Equal(MaintenanceDueStatus.Overdue, overview.Schedules[0].DueStatus);
    }
}