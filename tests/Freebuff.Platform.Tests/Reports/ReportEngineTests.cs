using System.Text;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Reports;

/// <summary>
/// Reports module:
///   - every report type aggregates known seeded data into expected figures
///   - tenant scope (company scope selector) is enforced at the engine level —
///     one fix in the engine secures every report type
///   - catalog is gated on the caller's effective permissions (a company that
///     cannot view fuel data never sees the fuel report)
///   - CSV/PDF export produce valid artifacts
///   - scheduled reports compute cadence and actually fire + deliver
/// </summary>
public class ReportEngineTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("reports_" + name + "_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ReportEngine NewEngine(ApplicationDbContext db)
        => new(db, new MaintenanceService(db, new AlwaysEntitledAlertEnforcement(), clock: () => DateTime.UtcNow));

    private static async Task<(Guid CompanyA, Guid CompanyB, Guid VehicleA1, Guid VehicleA2, Guid VehicleB1)> SeedTripsAsync(ApplicationDbContext db)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var vA1 = Guid.NewGuid();
        var vA2 = Guid.NewGuid();
        var vB1 = Guid.NewGuid();

        db.Vehicles.AddRange(
            new Vehicle { Id = vA1, RegistrationNumber = "A-100", CompanyId = a },
            new Vehicle { Id = vA2, RegistrationNumber = "A-200", CompanyId = a },
            new Vehicle { Id = vB1, RegistrationNumber = "B-100", CompanyId = b });
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        db.Trips.AddRange(
            // Company A, vehicle A1: 3 completed trips, 240 km, 6h active, 2 on time.
            new Trip { Id = Guid.NewGuid(), Name = "t1", CompanyId = a, VehicleId = vA1, DriverId = Guid.NewGuid(),
                Status = TripStatus.Completed, ActualStartTime = now.AddDays(-3), ActualEndTime = now.AddDays(-3).AddHours(2),
                ActualDistance = 80m, ActualDuration = TimeSpan.FromHours(2), IsDelayed = false },
            new Trip { Id = Guid.NewGuid(), Name = "t2", CompanyId = a, VehicleId = vA1, DriverId = Guid.NewGuid(),
                Status = TripStatus.Completed, ActualStartTime = now.AddDays(-2), ActualEndTime = now.AddDays(-2).AddHours(2),
                ActualDistance = 80m, ActualDuration = TimeSpan.FromHours(2), IsDelayed = true },
            new Trip { Id = Guid.NewGuid(), Name = "t3", CompanyId = a, VehicleId = vA1, DriverId = Guid.NewGuid(),
                Status = TripStatus.Completed, ActualStartTime = now.AddDays(-1), ActualEndTime = now.AddDays(-1).AddHours(2),
                ActualDistance = 80m, ActualDuration = TimeSpan.FromHours(2), IsDelayed = false },
            // Company B, vehicle B1: 1 trip — must never leak into A's scope.
            new Trip { Id = Guid.NewGuid(), Name = "tb", CompanyId = b, VehicleId = vB1, DriverId = Guid.NewGuid(),
                Status = TripStatus.Completed, ActualStartTime = now.AddDays(-2), ActualEndTime = now.AddDays(-2).AddHours(1),
                ActualDistance = 40m, ActualDuration = TimeSpan.FromHours(1), IsDelayed = false });
        await db.SaveChangesAsync();
        return (a, b, vA1, vA2, vB1);
    }

    // ── 1. Fleet utilization ───────────────────────────────────────────────

    [Fact]
    public async Task FleetUtilization_AggregatesDistanceTripsAndUtilization()
    {
        var db = NewDb("util");
        var (a, _, vA1, vA2, _) = await SeedTripsAsync(db);
        var engine = NewEngine(db);

        var result = await engine.RunAsync("fleet_utilization", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, new[] { a });

        var rowA1 = Assert.Single(result.Rows.Where(r => (string)r["vehicle"]! == "A-100"));
        Assert.Equal(3, rowA1["trips"]);
        Assert.Equal(240m, rowA1["distanceKm"]);
        Assert.Equal(6m, rowA1["activeHours"]);
        // Scope [company A] exposes A's two vehicles (B-100 excluded); only A-100 has trips.
        Assert.Equal(2, result.Rows.Count);

        var summary = result.Summary.ToDictionary(s => s.Label, s => s.Value);
        Assert.Equal("3", summary["Trips Completed"]);
        Assert.Equal("240.0", summary["Distance (km)"]);
        Assert.Equal("66.7%", summary["On-Time Rate"]);
        Assert.True(result.Series.Count == 1 && result.Series[0].Points.Count == 3);
    }

    [Fact]
    public async Task FleetUtilization_ScopeExcludesOtherCompanies()
    {
        var db = NewDb("utilscope");
        var (a, _, _, _, _) = await SeedTripsAsync(db);
        var engine = NewEngine(db);

        var result = await engine.RunAsync("fleet_utilization",
            new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, new[] { a });

        // Company B's trip must not appear — total distance is A's 240 km only.
        Assert.Equal(240m, result.Rows.Sum(r => (decimal)r["distanceKm"]!));
        Assert.DoesNotContain(result.Rows, r => (string)r["vehicle"]! == "B-100");
    }

    // ── 2. Fuel ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fuel_AggregatesLitersCostAndAnomalies()
    {
        var db = NewDb("fuelrpt");
        var company = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        db.Vehicles.AddRange(
            new Vehicle { Id = v1, RegistrationNumber = "F-1", CompanyId = company },
            new Vehicle { Id = v2, RegistrationNumber = "F-2", CompanyId = company });
        await db.SaveChangesAsync();
        db.FuelRecords.AddRange(
            new FuelRecord { Id = Guid.NewGuid(), VehicleId = v1, CompanyId = company, Quantity = 50, TotalCost = 100m, DistanceTraveledKm = 600m, RecordDate = DateTime.UtcNow.AddDays(-2) },
            new FuelRecord { Id = Guid.NewGuid(), VehicleId = v1, CompanyId = company, Quantity = 50, TotalCost = 105m, DistanceTraveledKm = 625m, RecordDate = DateTime.UtcNow.AddDays(-1), IsAnomaly = true, AnomalyReason = "suspected theft" },
            new FuelRecord { Id = Guid.NewGuid(), VehicleId = v2, CompanyId = company, Quantity = 20, TotalCost = 40m, DistanceTraveledKm = 240m, RecordDate = DateTime.UtcNow });
        db.FuelConsumptionSnapshots.Add(new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(), TenantId = company, VehicleId = v1,
            EventTimeUtc = DateTime.UtcNow.AddHours(-1), FuelLevelPercent = 20, IsAnomaly = true
        });
        await db.SaveChangesAsync();
        var engine = NewEngine(db);

        var result = await engine.RunAsync("fuel", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-7), To = DateTime.UtcNow }, new[] { company });

        var f1 = Assert.Single(result.Rows.Where(r => (string)r["vehicle"]! == "F-1"));
        Assert.Equal(2, f1["fillUps"]);
        Assert.Equal(100m, f1["liters"]);
        Assert.Equal(205m, f1["cost"]);
        Assert.Equal(12.25m, f1["kmPerLiter"]); // 1225 km / 100 L
        Assert.Equal(1, f1["anomalies"]);

        var summary = result.Summary.ToDictionary(s => s.Label, s => s.Value);
        Assert.Equal("120.0 L", summary["Fuel Consumed"]);
        Assert.Equal("$245.00", summary["Total Cost"]);
        Assert.Equal("2", summary["Anomalies"]); // record anomaly + snapshot anomaly
    }

    // ── 3. Maintenance ─────────────────────────────────────────────────────

    [Fact]
    public async Task Maintenance_AggregatesCostPreventiveBreakdownAndRatio()
    {
        var db = NewDb("maint");
        var company = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        db.Vehicles.Add(new Vehicle { Id = v1, RegistrationNumber = "M-1", CompanyId = company });
        await db.SaveChangesAsync();
        db.MaintenanceRecords.AddRange(
            new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = v1, CompanyId = company, RecordType = MaintenanceRecordType.Preventive, Title = "Oil change", Cost = 120m, CompletedDate = DateTime.UtcNow.AddDays(-5) },
            new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = v1, CompanyId = company, RecordType = MaintenanceRecordType.Preventive, Title = "Tyre rotation", Cost = 60m, CompletedDate = DateTime.UtcNow.AddDays(-3) },
            new MaintenanceRecord { Id = Guid.NewGuid(), VehicleId = v1, CompanyId = company, RecordType = MaintenanceRecordType.Breakdown, Title = "Alternator failure", Cost = 340m, DowntimeHours = 8m, CompletedDate = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var engine = NewEngine(db);

        var result = await engine.RunAsync("maintenance", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, new[] { company });

        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row["preventive"]);
        Assert.Equal(1, row["breakdowns"]);
        Assert.Equal(33.3m, row["breakdownRatioPct"]);
        Assert.Equal(520m, row["cost"]);
        Assert.Equal(8m, row["downtimeHours"]);

        var summary = result.Summary.ToDictionary(s => s.Label, s => s.Value);
        Assert.Equal("$520.00", summary["Total Cost"]);
        Assert.Equal("2", summary["Preventive Services"]);
        Assert.Equal("1", summary["Breakdowns"]);
        Assert.Equal("33.3%", summary["Breakdown Ratio"]);
    }

    // ── 4. Driver performance ──────────────────────────────────────────────

    [Fact]
    public async Task DriverPerformance_AggregatesScoresAndLicenseStatus()
    {
        var db = NewDb("drvrpt");
        var company = Guid.NewGuid();
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        db.Drivers.AddRange(
            new Driver { Id = d1, CompanyId = company, EmployeeId = "E1", FirstName = "Ann", LastName = "A", LicenseExpiry = DateTime.UtcNow.AddDays(-5) },
            new Driver { Id = d2, CompanyId = company, EmployeeId = "E2", FirstName = "Bob", LastName = "B", LicenseExpiry = DateTime.UtcNow.AddDays(200) });
        await db.SaveChangesAsync();
        var anchor = DateTime.UtcNow.Date.AddDays(-2);
        db.DriverScorePeriods.AddRange(
            new DriverScorePeriod { Id = Guid.NewGuid(), CompanyId = company, DriverId = d1, Window = "30d", AnchorDate = anchor, PeriodStart = anchor.AddDays(-30), PeriodEnd = anchor, SafetyScore = 70, ComplianceScore = 80, PunctualityScore = 90, BehaviorScore = 75, CompositeScore = 79, TripCount = 5, EventCount = 3 },
            new DriverScorePeriod { Id = Guid.NewGuid(), CompanyId = company, DriverId = d2, Window = "30d", AnchorDate = anchor, PeriodStart = anchor.AddDays(-30), PeriodEnd = anchor, SafetyScore = 90, ComplianceScore = 95, PunctualityScore = 85, BehaviorScore = 88, CompositeScore = 90, TripCount = 8, EventCount = 1 });
        db.DriverBehaviorEvents.AddRange(
            new DriverBehaviorEvent { Id = Guid.NewGuid(), TenantId = company, DriverId = d1, EventType = DriverBehaviorEventType.HarshBraking, EventTimeUtc = anchor.AddHours(1) },
            new DriverBehaviorEvent { Id = Guid.NewGuid(), TenantId = company, DriverId = d1, EventType = DriverBehaviorEventType.Distraction, EventTimeUtc = anchor.AddHours(2) },
            new DriverBehaviorEvent { Id = Guid.NewGuid(), TenantId = company, DriverId = d2, EventType = DriverBehaviorEventType.PhoneUsage, EventTimeUtc = anchor.AddHours(3) });
        await db.SaveChangesAsync();
        var engine = NewEngine(db);

        var result = await engine.RunAsync("driver_performance", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-7), To = DateTime.UtcNow }, new[] { company });

        Assert.Equal(2, result.Rows.Count);
        var ann = Assert.Single(result.Rows.Where(r => (string)r["driver"]! == "Ann A"));
        Assert.Equal(79m, ann["composite"]);
        Assert.Equal(2, ann["events"]);
        Assert.Equal("expired", ann["license"]);
        var bob = Assert.Single(result.Rows.Where(r => (string)r["driver"]! == "Bob B"));
        Assert.Equal("valid", bob["license"]);

        var summary = result.Summary.ToDictionary(s => s.Label, s => s.Value);
        Assert.Equal("3", summary["Safety Events"]);
        Assert.Equal("1", summary["Expired Licenses"]);
        Assert.Equal("84.5", summary["Avg Composite"]);
    }

    // ── 5. Trip & route ────────────────────────────────────────────────────

    [Fact]
    public async Task TripRoute_AggregatesOnTimeCheckpointAdherenceAndViolations()
    {
        var db = NewDb("trip");
        var (a, _, vA1, _, _) = await SeedTripsAsync(db);
        var trip1 = await db.Trips.AsNoTracking().FirstAsync(t => t.VehicleId == vA1 && t.Name == "t1");
        var trip2 = await db.Trips.AsNoTracking().FirstAsync(t => t.VehicleId == vA1 && t.Name == "t2");
        var geofence = new Geofence { Id = Guid.NewGuid(), Name = "CP", CompanyId = a, Type = GeofenceType.Circle };
        db.Geofences.Add(geofence);
        await db.SaveChangesAsync();
        db.TripGeofences.AddRange(
            new TripGeofence { Id = Guid.NewGuid(), TripId = trip1.Id, GeofenceId = geofence.Id, Role = TripGeofenceRole.Checkpoint, Visited = true },
            new TripGeofence { Id = Guid.NewGuid(), TripId = trip1.Id, GeofenceId = geofence.Id, Role = TripGeofenceRole.Checkpoint, Visited = true },
            new TripGeofence { Id = Guid.NewGuid(), TripId = trip2.Id, GeofenceId = geofence.Id, Role = TripGeofenceRole.Checkpoint, Visited = false },
            new TripGeofence { Id = Guid.NewGuid(), TripId = trip2.Id, GeofenceId = geofence.Id, Role = TripGeofenceRole.Checkpoint, Visited = true });
        db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(), CompanyId = a, AlertType = "route.restricted_zone_violation",
            Severity = AlertSeverity.High, Title = "Restricted zone", CreatedAt = DateTime.UtcNow.AddDays(-1),
            Status = EntityStatus.Active
        });
        await db.SaveChangesAsync();
        var engine = NewEngine(db);

        var result = await engine.RunAsync("trip_route", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, new[] { a });

        var row = Assert.Single(result.Rows);
        Assert.Equal(3, row["trips"]);
        Assert.Equal(3, row["completed"]);
        Assert.Equal(66.7m, row["onTimeRatePct"]);
        Assert.Equal(75m, row["checkpointAdherencePct"]); // 3 of 4 visited
        Assert.Equal(1, row["missedCheckpoints"]);

        var summary = result.Summary.ToDictionary(s => s.Label, s => s.Value);
        Assert.Equal("66.7%", summary["On-Time Rate"]);
        Assert.Equal("75%", summary["Checkpoint Adherence"]);
        Assert.Equal("1", summary["Restricted-Zone Violations"]);
    }

    // ── 6. Alerts ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Alerts_AggregatesVolumeByTypeAndSeverity()
    {
        var db = NewDb("alerts");
        var company = Guid.NewGuid();
        db.Companies.Add(new Company { Id = company, Name = "ACME Fleet", Slug = "acme" });
        await db.SaveChangesAsync();
        db.Alerts.AddRange(
            new Alert { Id = Guid.NewGuid(), CompanyId = company, AlertType = "fuel.theft_suspected", Severity = AlertSeverity.Critical, Title = "Theft", CreatedAt = DateTime.UtcNow.AddDays(-2), Status = EntityStatus.Active },
            new Alert { Id = Guid.NewGuid(), CompanyId = company, AlertType = "fuel.theft_suspected", Severity = AlertSeverity.Critical, Title = "Theft 2", CreatedAt = DateTime.UtcNow.AddDays(-1), Status = EntityStatus.Inactive },
            new Alert { Id = Guid.NewGuid(), CompanyId = company, AlertType = "geofence.entry", Severity = AlertSeverity.Low, Title = "Entry", Status = EntityStatus.Active });
        await db.SaveChangesAsync();
        // ApplyAuditInfo stamps CreatedAt=now on insert — backdate via a second
        // (Modified) pass so the report's date-range grouping sees distinct days.
        var seeded = await db.Alerts.ToListAsync();
        seeded[0].CreatedAt = DateTime.UtcNow.AddDays(-2);
        seeded[1].CreatedAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var engine = NewEngine(db);

        var result = await engine.RunAsync("alerts", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-7), To = DateTime.UtcNow }, new[] { company });

        var theftRow = Assert.Single(result.Rows.Where(r => (string)r["alertType"]! == "fuel.theft_suspected"));
        Assert.Equal(2, theftRow["count"]);
        Assert.Equal(1, theftRow["open"]);
        Assert.Equal(2, theftRow["criticalHigh"]);

        var summary = result.Summary.ToDictionary(s => s.Label, s => s.Value);
        Assert.Equal("3", summary["Total Alerts"]);
        Assert.Equal("fuel.theft_suspected", summary["Top Type"]);
        Assert.True(result.Series[0].Points.Count >= 2, $"expected >=2 daily points, got {result.Series[0].Points.Count}: {string.Join(",", result.Series[0].Points.Select(p => p.X))}");
    }

    // ── 7. Export ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CsvExport_HasBomHeaderAndRows()
    {
        var db = NewDb("csv");
        var (a, _, _, _, _) = await SeedTripsAsync(db);
        var engine = NewEngine(db);
        var result = await engine.RunAsync("fleet_utilization", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, new[] { a });

        var bytes = engine.ToCsv(result);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("\uFEFF", text);
        Assert.Contains("Vehicle", text);
        Assert.Contains("A-100", text);
        Assert.DoesNotContain("B-100", text);
    }

    [Fact]
    public async Task PdfExport_IsValidPdfWithTitleAndRows()
    {
        var db = NewDb("pdf");
        var (a, _, _, _, _) = await SeedTripsAsync(db);
        var engine = NewEngine(db);
        var result = await engine.RunAsync("fuel", new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow }, new[] { a });

        // Fuel report with no records still exports a valid PDF.
        var bytes = engine.ToPdf(result);
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(64, bytes.Length));
        Assert.StartsWith("%PDF-1.4", head);
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes));
        // Cross-check the object table: trailer with a Root reference.
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("/Root", text);
    }

    // ── 8. Catalog entitlement ─────────────────────────────────────────────

    [Fact]
    public async Task Catalog_FiltersByEffectivePermissions()
    {
        var db = NewDb("catalog");
        var engine = NewEngine(db);

        // User with only fuel + report perms sees ONLY the fuel report.
        var catalog = await engine.GetCatalogAsync(new HashSet<string> { "report.view", "fuel.view", "report.export", "fuel.export", "report.create" });
        Assert.Single(catalog);
        Assert.Equal("fuel", catalog[0].Code);
        Assert.True(catalog[0].CanExport);
        Assert.True(catalog[0].CanSchedule);

        // User with report perms but NOT fuel export → fuel report visible but not exportable.
        var noExport = await engine.GetCatalogAsync(new HashSet<string> { "report.view", "fuel.view" });
        Assert.Single(noExport);
        Assert.False(noExport[0].CanExport);
    }

    [Fact]
    public async Task Catalog_SuperAdminSeesEveryCategory()
    {
        var db = NewDb("catalogsa");
        var engine = NewEngine(db);
        var catalog = await engine.GetCatalogAsync(new HashSet<string>(), isSuperAdmin: true);
        Assert.Equal(6, catalog.Count);
        Assert.All(catalog, c => Assert.True(c.CanExport));
    }

    // ── 9. Scheduled reports ───────────────────────────────────────────────

    [Fact]
    public async Task ScheduledReport_DailyCadence_ComputesNextRunAndFires()
    {
        var db = NewDb("sched");
        var company = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var notifications = new CapturingNotificationService();
        var engine = NewEngine(db);
        var service = new ScheduledReportService(db, engine);

        var created = await service.CreateAsync(company, userId, new CreateScheduledReportDto
        {
            ReportType = "fuel",
            Title = "Daily fuel digest",
            Parameters = new ReportRequestDto { From = DateTime.UtcNow.AddDays(-7), To = DateTime.UtcNow },
            Cadence = "daily"
        }, new[] { company });

        Assert.True(created.NextRunAt > DateTime.UtcNow);
        Assert.Equal(created.NextRunAt!.Value.Date, DateTime.UtcNow.Date.AddDays(1));

        // Simulate the runner firing at the due moment.
        var (executed, delivered) = await service.RunDueAsync(created.NextRunAt!.Value, notifications);
        Assert.Equal(1, executed);
        Assert.Equal(1, delivered);
        Assert.Contains(notifications.Dispatched, d => d.EventType == "report.scheduled_delivered");

        var after = await service.GetMineAsync(company, userId);
        var s = Assert.Single(after);
        Assert.Equal("ok", s.LastRunStatus);
        Assert.Equal(created.NextRunAt.Value.AddDays(1), s.NextRunAt); // rolled forward
    }

    [Fact]
    public async Task ScheduledReport_WeeklyAndMonthlyCadence_AreCorrect()
    {
        var db = NewDb("schedwm");
        var company = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var engine = NewEngine(db);
        var service = new ScheduledReportService(db, engine);

        // Tuesday 2026-09-08 → next weekly (Monday) is 2026-09-14.
        var weekly = await service.CreateAsync(company, userId, new CreateScheduledReportDto
        {
            ReportType = "fuel", Title = "w", Cadence = "weekly", DayOfWeek = 1,
            Parameters = new ReportRequestDto()
        }, new[] { company });
        Assert.Equal(new DateTime(2026, 9, 14), weekly.NextRunAt!.Value.Date);

        // Monthly on the 1st → 2026-10-01.
        var monthly = await service.CreateAsync(company, userId, new CreateScheduledReportDto
        {
            ReportType = "fuel", Title = "m", Cadence = "monthly", DayOfMonth = 1,
            Parameters = new ReportRequestDto()
        }, new[] { company });
        Assert.Equal(new DateTime(2026, 10, 1), monthly.NextRunAt!.Value.Date);
    }

    [Fact]
    public async Task ScheduledReport_ThreeFailures_AutoPauses()
    {
        var db = NewDb("schedfail");
        var company = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var notifications = new CapturingNotificationService();
        var failingEngine = new FailingReportEngine();
        var service = new ScheduledReportService(db, failingEngine);

        var created = await service.CreateAsync(company, userId, new CreateScheduledReportDto
        {
            ReportType = "fuel",
            Title = "broken",
            Parameters = new ReportRequestDto(),
            Cadence = "daily"
        }, new[] { company });

        for (var i = 0; i < 3; i++)
        {
            var schedule = await db.ScheduledReports.FirstAsync();
            schedule.NextRunAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            var (executed, _) = await service.RunDueAsync(DateTime.UtcNow, notifications);
            Assert.Equal(1, executed);
        }
        var after = await service.GetMineAsync(company, userId);
        Assert.False(after[0].IsActive);
        Assert.Equal("error", after[0].LastRunStatus);
        Assert.DoesNotContain(notifications.Dispatched, d => d.EventType == "report.scheduled_delivered");
    }

    /// <summary>Engine whose RunAsync always throws — drives the schedule failure path.</summary>
    private sealed class FailingReportEngine : IReportEngine
    {
        public Task<List<ReportCatalogItemDto>> GetCatalogAsync(HashSet<string> effectivePermissions, bool isSuperAdmin = false)
            => Task.FromResult(new List<ReportCatalogItemDto>());
        public Task<ReportResultDto> RunAsync(string reportType, ReportRequestDto request, IReadOnlyList<Guid>? scope)
            => throw new InvalidOperationException("engine down");
        public ReportDefinition? Find(string reportType) => new("fuel", "Fuel", "", "fuel", new[] { "fuel.view" }, new[] { "fuel.export" }, null!);
        public byte[] ToCsv(ReportResultDto result) => Array.Empty<byte>();
        public byte[] ToPdf(ReportResultDto result) => Array.Empty<byte>();
    }

    // ── 10. Scope freeze on schedules ──────────────────────────────────────

    [Fact]
    public async Task ScheduledReport_FreezesCreatorScope()
    {
        var db = NewDb("schedscope");
        var (a, b, _, _, _) = await SeedTripsAsync(db);
        var engine = NewEngine(db);
        var service = new ScheduledReportService(db, engine);
        var notifications = new CapturingNotificationService();

        // User scoped to company A only.
        var created = await service.CreateAsync(a, Guid.NewGuid(), new CreateScheduledReportDto
        {
            ReportType = "fleet_utilization", Title = "util",
            Parameters = new ReportRequestDto { From = DateTime.UtcNow.AddDays(-30), To = DateTime.UtcNow },
            Cadence = "daily"
        }, new[] { a });

        var (executed, _) = await service.RunDueAsync(created.NextRunAt!.Value, notifications);
        Assert.Equal(1, executed);
        // The digest should reflect A's 240 km, never B's 40 km.
        var notification = Assert.Single(notifications.Dispatched);
        Assert.Contains("Distance (km): 240.0", notification.Title + notification.Message);
        Assert.DoesNotContain("B-100", notification.Message);
    }
}