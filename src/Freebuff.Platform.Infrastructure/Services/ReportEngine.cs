using System.Globalization;
using System.Text;
using System.Text.Json;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// The shared Report Engine — one registry, one execution path, one export
/// path. Every report type is a definition (data source query + filters +
/// output shape), NOT bespoke controller code; adding a report is a registry
/// entry, exactly like the Alert Type / Notification Event Type registries.
///
/// Security is centralized here, not per report:
///   - Tenant scoping: every definition receives the resolved company-scope
///     ids (CompanyScopePolicy) and MUST filter CompanyId by them. SuperAdmin
///     in "All Companies" mode gets scope=null → all companies aggregate
///     correctly; any constrained scope narrows every report.
///   - Entitlement: a report category only appears in the catalog when the
///     caller's effective permissions include its RequiresView set (e.g. the
///     fuel report needs fuel.view — a company whose package excludes the fuel
///     page never even sees the option, not just an empty report).
///   - Export: requires the report's RequiresExport set (reports.export AND
///     the underlying page's export, e.g. fuel.export) — checked per request.
///
/// Performance: every definition aggregates the MATERIALIZED tables the fleet
/// modules already maintain (FuelRecord/FuelConsumptionSnapshot,
/// MaintenanceRecord/MaintenanceSchedule, DriverScorePeriod, Trip metrics,
/// Alert rows) — never a scan of raw telemetry, so month-range reports stay
/// fast by construction.
/// </summary>
public sealed record ReportDefinition(
    string Code,
    string Name,
    string Description,
    string Icon,
    IReadOnlyList<string> RequiresView,
    IReadOnlyList<string> RequiresExport,
    Func<ApplicationDbContext, ReportRequestDto, IReadOnlyList<Guid>?, Task<ReportResultDto>> Execute);

public interface IReportEngine
{
    /// <summary>
    /// Report types the caller can see, gated on their effective permissions
    /// (isSuperAdmin = JWT role claim — bypasses role-grant checks entirely).
    /// </summary>
    Task<List<ReportCatalogItemDto>> GetCatalogAsync(HashSet<string> effectivePermissions, bool isSuperAdmin = false);

    /// <summary>Run any registered report with tenant-scope enforcement built in.</summary>
    Task<ReportResultDto> RunAsync(string reportType, ReportRequestDto request, IReadOnlyList<Guid>? scope);

    ReportDefinition? Find(string reportType);

    /// <summary>UTF-8 CSV (with BOM for Excel) of a materialized report.</summary>
    byte[] ToCsv(ReportResultDto result);

    /// <summary>Self-contained PDF (no external fonts) of a materialized report.</summary>
    byte[] ToPdf(ReportResultDto result);
}

public class ReportEngine : IReportEngine
{
    private readonly ApplicationDbContext _db;
    private readonly MaintenanceService _maintenance;
    private readonly ReportDefinition[] _definitions;

    public ReportEngine(ApplicationDbContext db, MaintenanceService maintenance)
    {
        _db = db;
        _maintenance = maintenance;
        _definitions = BuildDefinitions(_maintenance);
    }

    /// <summary>The canonical report registry — one entry per report type.</summary>
    private static ReportDefinition[] BuildDefinitions(MaintenanceService maintenance) => new[]
    {
        new ReportDefinition("fleet_utilization", "Fleet Utilization",
            "Active vs idle time, trips completed, distance traveled and utilization rate per vehicle.",
            "gauge",
            new[] { "vehicle.view", "trip.view" },
            new[] { "vehicle.export" },
            (db, req, scope) => BuildFleetUtilizationAsync(db, req, scope)),

        new ReportDefinition("fuel", "Fuel Report",
            "Consumption, cost, efficiency trends and theft/anomaly incidents across the fleet.",
            "fuel",
            new[] { "fuel.view" },
            new[] { "fuel.export" },
            (db, req, scope) => BuildFuelAsync(db, req, scope)),

        new ReportDefinition("maintenance", "Maintenance Report",
            "Due/overdue summary, maintenance cost over time and preventive-vs-breakdown ratio.",
            "wrench",
            new[] { "maintenance.view" },
            new[] { "maintenance.export" },
            (db, req, scope) => BuildMaintenanceAsync(db, req, scope, maintenance)),

        new ReportDefinition("driver_performance", "Driver Performance",
            "Scorecard trends, safety event frequency and license compliance status across the fleet.",
            "award",
            new[] { "driver.view", "driverscore.view" },
            new[] { "driver.export" },
            (db, req, scope) => BuildDriverPerformanceAsync(db, req, scope)),

        new ReportDefinition("trip_route", "Trip & Route Report",
            "On-time delivery rate, checkpoint adherence, missed checkpoints and restricted-zone violations.",
            "navigation",
            new[] { "trip.view" },
            new[] { "trip.export" },
            (db, req, scope) => BuildTripRouteAsync(db, req, scope)),

        new ReportDefinition("alerts", "Alerts Report",
            "Alert volume by type and severity over time — operational review + subscription-value signal.",
            "bell",
            new[] { "alert.view" },
            new[] { "alert.export" },
            (db, req, scope) => BuildAlertsAsync(db, req, scope)),
    };

    public ReportDefinition? Find(string reportType)
        => _definitions.FirstOrDefault(d => string.Equals(d.Code, reportType, StringComparison.OrdinalIgnoreCase));

    public async Task<List<ReportCatalogItemDto>> GetCatalogAsync(HashSet<string> effectivePermissions, bool isSuperAdmin = false)
    {
        var catalog = new List<ReportCatalogItemDto>();
        foreach (var def in _definitions)
        {
            if (!isSuperAdmin && !def.RequiresView.All(effectivePermissions.Contains)) continue;
            catalog.Add(new ReportCatalogItemDto
            {
                Code = def.Code,
                Name = def.Name,
                Description = def.Description,
                Icon = def.Icon,
                RequiresView = def.RequiresView.ToList(),
                RequiresExport = def.RequiresExport.ToList(),
                CanExport = isSuperAdmin || def.RequiresExport.All(effectivePermissions.Contains),
                // Scheduling a report = creating a report configuration — gated on
                // the report page's create action (the registry's 6-action model).
                CanSchedule = isSuperAdmin || effectivePermissions.Contains("report.create")
            });
        }
        return catalog;
    }

    public async Task<ReportResultDto> RunAsync(string reportType, ReportRequestDto request, IReadOnlyList<Guid>? scope)
    {
        var def = Find(reportType) ?? throw new KeyNotFoundException($"Unknown report type '{reportType}'");
        request.From ??= DateTime.UtcNow.Date.AddDays(-29);
        request.To ??= DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
        return await def.Execute(_db, request, scope);
    }

    // ── 1. Fleet Utilization ────────────────────────────────────────────────

    private static async Task<ReportResultDto> BuildFleetUtilizationAsync(
        ApplicationDbContext db, ReportRequestDto req, IReadOnlyList<Guid>? scope)
    {
        var from = req.From!.Value;
        var to = req.To!.Value;
        var availableHours = Math.Max(1, (decimal)(to - from).TotalHours);

        var tripRows = await db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted
                && (scope == null || scope.Contains(t.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(t.VehicleId))
                && t.Status == TripStatus.Completed
                && t.ActualStartTime != null
                && t.ActualStartTime >= from && t.ActualStartTime <= to)
            .Select(t => new
            {
                t.VehicleId,
                t.Vehicle.RegistrationNumber,
                Distance = t.ActualDistance ?? 0m,
                ActiveHours = (t.ActualDuration != null ? (decimal)t.ActualDuration.Value.TotalHours : 0m),
                OnTime = !t.IsDelayed
            })
            .ToListAsync();

        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(v.Id)))
            .Select(v => new { v.Id, v.RegistrationNumber })
            .ToListAsync();

        var byVehicle = tripRows.GroupBy(t => t.VehicleId).ToList();
        var rows = new List<Dictionary<string, object?>>();
        foreach (var vehicle in vehicles.OrderBy(v => v.RegistrationNumber))
        {
            var trips = byVehicle.FirstOrDefault(g => g.Key == vehicle.Id);
            var count = trips?.Count() ?? 0;
            var distance = trips?.Sum(t => t.Distance) ?? 0m;
            var active = trips?.Sum(t => t.ActiveHours) ?? 0m;
            var utilization = Math.Min(100m, active / availableHours * 100m);
            rows.Add(new Dictionary<string, object?>
            {
                ["vehicle"] = vehicle.RegistrationNumber,
                ["trips"] = count,
                ["distanceKm"] = Math.Round(distance, 1),
                ["activeHours"] = Math.Round(active, 1),
                ["utilizationPct"] = Math.Round(utilization, 1)
            });
        }

        var totalTrips = tripRows.Count;
        var totalDistance = tripRows.Sum(t => t.Distance);
        var totalActive = tripRows.Sum(t => t.ActiveHours);
        var onTime = tripRows.Count(t => t.OnTime);

        var dayPoints = await BuildUtilizationSeriesAsync(db, from, to, req.VehicleIds, scope);

        return new ReportResultDto
        {
            ReportType = "fleet_utilization",
            Title = "Fleet Utilization",
            From = from, To = to,
            Columns = new List<ReportColumnDto>
            {
                new() { Key = "vehicle", Label = "Vehicle", Type = "string" },
                new() { Key = "trips", Label = "Trips Completed", Type = "number" },
                new() { Key = "distanceKm", Label = "Distance (km)", Type = "number" },
                new() { Key = "activeHours", Label = "Active Hours", Type = "number" },
                new() { Key = "utilizationPct", Label = "Utilization %", Type = "number" },
            },
            Rows = rows,
            Series = dayPoints,
            Summary = new List<ReportSummaryItemDto>
            {
                new() { Label = "Vehicles", Value = vehicles.Count.ToString() },
                new() { Label = "Trips Completed", Value = totalTrips.ToString() },
                new() { Label = "Distance (km)", Value = Math.Round(totalDistance, 1).ToString("N1") },
                new() { Label = "Active Hours", Value = Math.Round(totalActive, 1).ToString("N1") },
                new() { Label = "On-Time Rate", Value = totalTrips == 0 ? "—" : $"{Math.Round((double)onTime / totalTrips * 100, 1)}%" },
                new() { Label = "Avg Utilization", Value = vehicles.Count == 0 || totalActive == 0 ? "—" : $"{Math.Round((double)(totalActive / (availableHours * vehicles.Count) * 100m), 1)}%" },
            }
        };
    }

    private static async Task<List<ReportSeriesDto>> BuildUtilizationSeriesAsync(
        ApplicationDbContext db, DateTime from, DateTime to, List<Guid> vehicleIds, IReadOnlyList<Guid>? scope)
    {
        var rows = await db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted
                && (scope == null || scope.Contains(t.CompanyId))
                && (vehicleIds.Count == 0 || vehicleIds.Contains(t.VehicleId))
                && t.Status == TripStatus.Completed && t.ActualStartTime != null
                && t.ActualStartTime >= from && t.ActualStartTime <= to)
            .Select(t => new { Day = t.ActualStartTime!.Value.Date, Distance = t.ActualDistance ?? 0m })
            .ToListAsync();
        var byDay = rows.GroupBy(t => t.Day).OrderBy(g => g.Key).ToList();
        var distance = new ReportSeriesDto { Label = "Distance (km)" };
        foreach (var g in byDay)
            distance.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM-dd"), Y = (double)g.Sum(x => x.Distance) });
        return new List<ReportSeriesDto> { distance };
    }

    // ── 2. Fuel ─────────────────────────────────────────────────────────────

    private static async Task<ReportResultDto> BuildFuelAsync(
        ApplicationDbContext db, ReportRequestDto req, IReadOnlyList<Guid>? scope)
    {
        var from = req.From!.Value;
        var to = req.To!.Value;

        var records = await db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted
                && (scope == null || scope.Contains(f.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(f.VehicleId))
                && f.RecordDate >= from && f.RecordDate <= to)
            .Select(f => new
            {
                f.VehicleId,
                f.Vehicle.RegistrationNumber,
                f.Quantity,
                f.TotalCost,
                f.DistanceTraveledKm,
                f.IsAnomaly
            })
            .ToListAsync();

        var anomalies = await db.FuelConsumptionSnapshots.AsNoTracking()
            .CountAsync(s => (scope == null || scope.Contains(s.TenantId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(s.VehicleId))
                && s.EventTimeUtc >= from && s.EventTimeUtc <= to && s.IsAnomaly);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var g in records.GroupBy(r => r.VehicleId).OrderBy(g => g.First().RegistrationNumber))
        {
            var liters = g.Sum(r => r.Quantity);
            var cost = g.Sum(r => r.TotalCost ?? 0m);
            var dist = g.Sum(r => r.DistanceTraveledKm ?? 0m);
            var kmPerL = liters > 0 ? dist / liters : (decimal?)null;
            var costPerKm = dist > 0 ? cost / dist : (decimal?)null;
            rows.Add(new Dictionary<string, object?>
            {
                ["vehicle"] = g.First().RegistrationNumber,
                ["fillUps"] = g.Count(),
                ["liters"] = Math.Round(liters, 1),
                ["cost"] = Math.Round(cost, 2),
                ["kmPerLiter"] = kmPerL != null ? Math.Round(kmPerL.Value, 2) : null,
                ["costPerKm"] = costPerKm != null ? Math.Round(costPerKm.Value, 4) : null,
                ["anomalies"] = g.Count(r => r.IsAnomaly)
            });
        }

        var totalLiters = records.Sum(r => r.Quantity);
        var totalCost = records.Sum(r => r.TotalCost ?? 0m);
        var totalDist = records.Sum(r => r.DistanceTraveledKm ?? 0m);

        // Daily liters/spend series from the same window (re-query day grouping).
        var dayRows = await db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted
                && (scope == null || scope.Contains(f.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(f.VehicleId))
                && f.RecordDate >= from && f.RecordDate <= to)
            .Select(f => new { f.RecordDate.Date, f.Quantity, f.TotalCost })
            .ToListAsync();
        var byDay = dayRows.GroupBy(r => r.Date).OrderBy(g => g.Key).ToList();
        var litersSeries = new ReportSeriesDto { Label = "Liters" };
        var spendSeries = new ReportSeriesDto { Label = "Spend" };
        foreach (var g in byDay)
        {
            litersSeries.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM-dd"), Y = (double)g.Sum(x => x.Quantity) });
            spendSeries.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM-dd"), Y = (double)(g.Sum(x => x.TotalCost ?? 0m)) });
        }

        return new ReportResultDto
        {
            ReportType = "fuel",
            Title = "Fuel Report",
            From = from, To = to,
            Columns = new List<ReportColumnDto>
            {
                new() { Key = "vehicle", Label = "Vehicle", Type = "string" },
                new() { Key = "fillUps", Label = "Fill-ups", Type = "number" },
                new() { Key = "liters", Label = "Liters", Type = "number" },
                new() { Key = "cost", Label = "Cost", Type = "currency" },
                new() { Key = "kmPerLiter", Label = "km/L", Type = "number" },
                new() { Key = "costPerKm", Label = "Cost/km", Type = "currency" },
                new() { Key = "anomalies", Label = "Anomalies", Type = "number" },
            },
            Rows = rows,
            Series = new List<ReportSeriesDto> { litersSeries, spendSeries },
            Summary = new List<ReportSummaryItemDto>
            {
                new() { Label = "Fuel Consumed", Value = $"{Math.Round(totalLiters, 1):N1} L" },
                new() { Label = "Total Cost", Value = $"${Math.Round(totalCost, 2):N2}" },
                new() { Label = "Avg Efficiency", Value = totalLiters == 0 ? "—" : $"{Math.Round(totalDist / totalLiters, 2):N2} km/L" },
                new() { Label = "Avg Price", Value = totalLiters == 0 ? "—" : $"${Math.Round(totalCost / totalLiters, 3):N3}/L" },
                new() { Label = "Anomalies", Value = (anomalies + records.Count(r => r.IsAnomaly)).ToString() },
            }
        };
    }

    // ── 3. Maintenance ──────────────────────────────────────────────────────

    private static async Task<ReportResultDto> BuildMaintenanceAsync(
        ApplicationDbContext db, ReportRequestDto req, IReadOnlyList<Guid>? scope, MaintenanceService maintenance)
    {
        var from = req.From!.Value;
        var to = req.To!.Value;

        var overview = await maintenance.GetFleetOverviewAsync(scope);
        var due = overview.Schedules
            .Where(s => s.DueStatus == MaintenanceDueStatus.DueSoon || s.DueStatus == MaintenanceDueStatus.Overdue)
            .ToList();
        var overdue = due.Count(s => s.DueStatus == MaintenanceDueStatus.Overdue);

        var records = await db.MaintenanceRecords.AsNoTracking()
            .Where(m => !m.IsDeleted
                && (scope == null || scope.Contains(m.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(m.VehicleId))
                && m.CompletedDate != null && m.CompletedDate >= from && m.CompletedDate <= to)
            .Select(m => new
            {
                m.VehicleId,
                m.Vehicle.RegistrationNumber,
                m.RecordType,
                m.Cost,
                m.DowntimeHours,
                m.Title
            })
            .ToListAsync();

        var rows = new List<Dictionary<string, object?>>();
        foreach (var g in records.GroupBy(r => r.VehicleId).OrderBy(g => g.First().RegistrationNumber))
        {
            var preventive = g.Count(r => r.RecordType == MaintenanceRecordType.Preventive);
            var breakdowns = g.Count(r => r.RecordType == MaintenanceRecordType.Breakdown);
            var ratio = preventive + breakdowns == 0 ? (decimal?)null
                : Math.Round((decimal)breakdowns / (preventive + breakdowns) * 100, 1);
            var dueItems = due.Count(s => s.VehicleId == g.Key);
            rows.Add(new Dictionary<string, object?>
            {
                ["vehicle"] = g.First().RegistrationNumber,
                ["preventive"] = preventive,
                ["breakdowns"] = breakdowns,
                ["breakdownRatioPct"] = ratio,
                ["cost"] = Math.Round(g.Sum(r => r.Cost ?? 0m), 2),
                ["downtimeHours"] = Math.Round(g.Sum(r => r.DowntimeHours ?? 0m), 1),
                ["dueItems"] = dueItems
            });
        }

        var monthRows = await db.MaintenanceRecords.AsNoTracking()
            .Where(m => !m.IsDeleted
                && (scope == null || scope.Contains(m.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(m.VehicleId))
                && m.CompletedDate != null && m.CompletedDate >= from && m.CompletedDate <= to)
            .Select(m => new { m.CompletedDate!.Value, m.Cost })
            .ToListAsync();
        var costSeries = new ReportSeriesDto { Label = "Cost" };
        foreach (var g in monthRows.GroupBy(r => new DateTime(r.Value.Year, r.Value.Month, 1)).OrderBy(g => g.Key))
        {
            costSeries.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM"), Y = (double)g.Sum(x => x.Cost ?? 0m) });
        }

        var totalCost = records.Sum(r => r.Cost ?? 0m);
        var preventiveTotal = records.Count(r => r.RecordType == MaintenanceRecordType.Preventive);
        var breakdownTotal = records.Count(r => r.RecordType == MaintenanceRecordType.Breakdown);

        return new ReportResultDto
        {
            ReportType = "maintenance",
            Title = "Maintenance Report",
            From = from, To = to,
            Columns = new List<ReportColumnDto>
            {
                new() { Key = "vehicle", Label = "Vehicle", Type = "string" },
                new() { Key = "preventive", Label = "Preventive", Type = "number" },
                new() { Key = "breakdowns", Label = "Breakdowns", Type = "number" },
                new() { Key = "breakdownRatioPct", Label = "Breakdown %", Type = "number" },
                new() { Key = "cost", Label = "Cost", Type = "currency" },
                new() { Key = "downtimeHours", Label = "Downtime (h)", Type = "number" },
                new() { Key = "dueItems", Label = "Due/Overdue Items", Type = "number" },
            },
            Rows = rows,
            Series = new List<ReportSeriesDto> { costSeries },
            Summary = new List<ReportSummaryItemDto>
            {
                new() { Label = "Total Cost", Value = $"${Math.Round(totalCost, 2):N2}" },
                new() { Label = "Preventive Services", Value = preventiveTotal.ToString() },
                new() { Label = "Breakdowns", Value = breakdownTotal.ToString() },
                new() { Label = "Breakdown Ratio", Value = preventiveTotal + breakdownTotal == 0 ? "—" : $"{Math.Round((double)breakdownTotal / (preventiveTotal + breakdownTotal) * 100, 1)}%" },
                new() { Label = "Overdue Items", Value = overdue.ToString() },
                new() { Label = "Due Soon Items", Value = (due.Count - overdue).ToString() },
            }
        };
    }

    // ── 4. Driver Performance ───────────────────────────────────────────────

    private static async Task<ReportResultDto> BuildDriverPerformanceAsync(
        ApplicationDbContext db, ReportRequestDto req, IReadOnlyList<Guid>? scope)
    {
        var from = req.From!.Value;
        var to = req.To!.Value;

        // Latest 30d anchor per driver within the window — trend uses all anchors.
        var periods = await db.DriverScorePeriods.AsNoTracking()
            .Where(p => (scope == null || scope.Contains(p.CompanyId))
                && (req.DriverIds.Count == 0 || req.DriverIds.Contains(p.DriverId))
                && p.Window == "30d" && p.AnchorDate >= from && p.AnchorDate <= to)
            .Select(p => new
            {
                p.DriverId,
                p.AnchorDate,
                p.SafetyScore,
                p.ComplianceScore,
                p.PunctualityScore,
                p.BehaviorScore,
                p.CompositeScore,
                p.TripCount,
                p.EventCount,
                p.DistanceKm,
                DriverName = p.Driver != null ? p.Driver.FirstName + " " + p.Driver.LastName : null,
                p.Driver.LicenseExpiry
            })
            .ToListAsync();

        var drivers = await db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && (scope == null || scope.Contains(d.CompanyId))
                && (req.DriverIds.Count == 0 || req.DriverIds.Contains(d.Id)))
            .Select(d => new { d.Id, Name = d.FirstName + " " + d.LastName, d.LicenseExpiry })
            .ToListAsync();

        var now = DateTime.UtcNow;
        var events = await db.DriverBehaviorEvents.AsNoTracking()
            .Where(e => (scope == null || scope.Contains(e.TenantId))
                && (req.DriverIds.Count == 0 || req.DriverIds.Contains(e.DriverId ?? Guid.Empty))
                && e.EventTimeUtc >= from && e.EventTimeUtc <= to)
            .GroupBy(e => e.DriverId)
            .Select(g => new { DriverId = g.Key, Count = g.Count() })
            .ToListAsync();
        var eventCountByDriver = events
            .Where(e => e.DriverId != null)
            .ToDictionary(e => e.DriverId!.Value, e => e.Count);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var d in drivers.OrderBy(d => d.Name))
        {
            var latest = periods.Where(p => p.DriverId == d.Id).OrderByDescending(p => p.AnchorDate).FirstOrDefault();
            string licenseStatus;
            if (d.LicenseExpiry == null) licenseStatus = "n/a";
            else if (d.LicenseExpiry < now) licenseStatus = "expired";
            else if (d.LicenseExpiry < now.AddDays(30)) licenseStatus = "expiring soon";
            else licenseStatus = "valid";

            rows.Add(new Dictionary<string, object?>
            {
                ["driver"] = d.Name,
                ["composite"] = latest?.CompositeScore,
                ["safety"] = latest?.SafetyScore,
                ["compliance"] = latest?.ComplianceScore,
                ["behavior"] = latest?.BehaviorScore,
                ["trips"] = latest?.TripCount ?? 0,
                ["events"] = eventCountByDriver.GetValueOrDefault(d.Id),
                ["license"] = licenseStatus
            });
        }

        var trend = new ReportSeriesDto { Label = "Fleet Composite" };
        foreach (var g in periods.GroupBy(p => p.AnchorDate).OrderBy(g => g.Key))
        {
            var avg = g.Average(p => p.CompositeScore);
            if (avg != null)
                trend.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM-dd"), Y = (double)avg.Value });
        }

        var expired = drivers.Count(d => d.LicenseExpiry != null && d.LicenseExpiry < now);
        var scored = periods.GroupBy(p => p.DriverId).Count();
        var latestAll = periods.GroupBy(p => p.DriverId)
            .Select(g => g.OrderByDescending(p => p.AnchorDate).First())
            .ToList();

        return new ReportResultDto
        {
            ReportType = "driver_performance",
            Title = "Driver Performance",
            From = from, To = to,
            Columns = new List<ReportColumnDto>
            {
                new() { Key = "driver", Label = "Driver", Type = "string" },
                new() { Key = "composite", Label = "Composite", Type = "number" },
                new() { Key = "safety", Label = "Safety", Type = "number" },
                new() { Key = "compliance", Label = "Compliance", Type = "number" },
                new() { Key = "behavior", Label = "Behavior", Type = "number" },
                new() { Key = "trips", Label = "Trips", Type = "number" },
                new() { Key = "events", Label = "Safety Events", Type = "number" },
                new() { Key = "license", Label = "License", Type = "string" },
            },
            Rows = rows,
            Series = new List<ReportSeriesDto> { trend },
            Summary = new List<ReportSummaryItemDto>
            {
                new() { Label = "Drivers Scored", Value = scored.ToString() },
                new() { Label = "Avg Composite", Value = latestAll.Count == 0 || latestAll.All(x => x.CompositeScore == null) ? "—" : $"{Math.Round(latestAll.Where(x => x.CompositeScore != null).Average(x => x.CompositeScore!.Value), 1):N1}" },
                new() { Label = "Avg Safety", Value = latestAll.Count == 0 || latestAll.All(x => x.SafetyScore == null) ? "—" : $"{Math.Round(latestAll.Where(x => x.SafetyScore != null).Average(x => x.SafetyScore!.Value), 1):N1}" },
                new() { Label = "Safety Events", Value = events.Sum(e => e.Count).ToString() },
                new() { Label = "Expired Licenses", Value = expired.ToString() },
                new() { Label = "License Expiring ≤30d", Value = drivers.Count(d => d.LicenseExpiry != null && d.LicenseExpiry >= now && d.LicenseExpiry < now.AddDays(30)).ToString() },
            }
        };
    }

    // ── 5. Trip & Route ─────────────────────────────────────────────────────

    private static async Task<ReportResultDto> BuildTripRouteAsync(
        ApplicationDbContext db, ReportRequestDto req, IReadOnlyList<Guid>? scope)
    {
        var from = req.From!.Value;
        var to = req.To!.Value;

        var tripRows = await db.Trips.AsNoTracking()
            .Where(t => !t.IsDeleted
                && (scope == null || scope.Contains(t.CompanyId))
                && (req.VehicleIds.Count == 0 || req.VehicleIds.Contains(t.VehicleId))
                && t.ActualStartTime != null && t.ActualStartTime >= from && t.ActualStartTime <= to)
            .Select(t => new
            {
                t.Id,
                t.VehicleId,
                t.Vehicle.RegistrationNumber,
                t.Status,
                t.IsDelayed,
                t.PlannedDuration,
                t.ActualDuration,
                t.ActualStartTime
            })
            .ToListAsync();
        // Nullable TimeSpan.Value conditionals are unreliable under the in-memory
        // provider — project to minutes after materialization.
        var trips = tripRows
            .Select(t => new
            {
                t.Id,
                t.VehicleId,
                t.RegistrationNumber,
                t.Status,
                t.IsDelayed,
                PlannedMinutes = t.PlannedDuration.HasValue ? (int?)t.PlannedDuration.Value.TotalMinutes : null,
                ActualMinutes = t.ActualDuration.HasValue ? (int?)t.ActualDuration.Value.TotalMinutes : null,
                t.ActualStartTime,
                OnTime = !t.IsDelayed
            })
            .ToList();

        var tripIds = trips.Select(t => t.Id).ToList();
        var checkpointData = tripIds.Count == 0
            ? new List<(Guid TripId, int Total, int Visited)>()
            : (await db.TripGeofences.AsNoTracking()
                .Where(g => tripIds.Contains(g.TripId) && g.Role == TripGeofenceRole.Checkpoint)
                .GroupBy(g => g.TripId)
                .Select(g => new { TripId = g.Key, Total = g.Count(), Visited = g.Count(x => x.Visited == true) })
                .ToListAsync())
                .Select(x => (x.TripId, x.Total, x.Visited))
                .ToList();
        var checkpointByTrip = checkpointData.ToDictionary(c => c.TripId);

        var restricted = await db.Alerts.AsNoTracking()
            .CountAsync(a => !a.IsDeleted && (scope == null || scope.Contains(a.CompanyId))
                && a.AlertType == "route.restricted_zone_violation"
                && a.CreatedAt >= from && a.CreatedAt <= to);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var g in trips.GroupBy(t => t.VehicleId).OrderBy(g => g.First().RegistrationNumber))
        {
            var completed = g.Count(t => t.Status == TripStatus.Completed);
            var onTime = g.Count(t => t.OnTime);
            var onTimeRate = completed == 0 ? (decimal?)null : Math.Round((decimal)onTime / completed * 100, 1);
            var checkpoints = checkpointByTrip.Where(kv => g.Any(t => t.Id == kv.Key)).ToList();
            var totalCp = checkpoints.Sum(kv => kv.Value.Total);
            var visitedCp = checkpoints.Sum(kv => kv.Value.Visited);
            var adherence = totalCp == 0 ? (decimal?)null : Math.Round((decimal)visitedCp / totalCp * 100, 1);
            var avgDelayMin = g.Where(t => t.IsDelayed && t.ActualMinutes != null && t.PlannedMinutes != null)
                .Select(t => t.ActualMinutes!.Value - t.PlannedMinutes!.Value)
                .DefaultIfEmpty(0).Average();
            rows.Add(new Dictionary<string, object?>
            {
                ["vehicle"] = g.First().RegistrationNumber,
                ["trips"] = g.Count(),
                ["completed"] = completed,
                ["onTimeRatePct"] = onTimeRate,
                ["avgDelayMin"] = Math.Round(avgDelayMin, 0),
                ["checkpointAdherencePct"] = adherence,
                ["missedCheckpoints"] = totalCp - visitedCp
            });
        }

        var daily = trips
            .GroupBy(t => t.ActualStartTime!.Value.Date)
            .OrderBy(g => g.Key)
            .ToList();
        var tripsSeries = new ReportSeriesDto { Label = "Trips" };
        var onTimeSeries = new ReportSeriesDto { Label = "On-Time %" };
        foreach (var g in daily)
        {
            tripsSeries.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM-dd"), Y = g.Count() });
            var completed = g.Count(t => t.Status == TripStatus.Completed);
            onTimeSeries.Points.Add(new ReportSeriesPointDto
            {
                X = g.Key.ToString("yyyy-MM-dd"),
                Y = completed == 0 ? 0 : Math.Round((double)g.Count(t => t.OnTime) / completed * 100, 1)
            });
        }

        var totalTrips = trips.Count;
        var totalCompleted = trips.Count(t => t.Status == TripStatus.Completed);
        var totalOnTime = trips.Count(t => t.OnTime);
        var totalCp2 = checkpointByTrip.Values.Sum(c => c.Total);

        return new ReportResultDto
        {
            ReportType = "trip_route",
            Title = "Trip & Route Report",
            From = from, To = to,
            Columns = new List<ReportColumnDto>
            {
                new() { Key = "vehicle", Label = "Vehicle", Type = "string" },
                new() { Key = "trips", Label = "Trips", Type = "number" },
                new() { Key = "completed", Label = "Completed", Type = "number" },
                new() { Key = "onTimeRatePct", Label = "On-Time %", Type = "number" },
                new() { Key = "avgDelayMin", Label = "Avg Delay (min)", Type = "number" },
                new() { Key = "checkpointAdherencePct", Label = "Checkpoint Adherence %", Type = "number" },
                new() { Key = "missedCheckpoints", Label = "Missed Checkpoints", Type = "number" },
            },
            Rows = rows,
            Series = new List<ReportSeriesDto> { tripsSeries, onTimeSeries },
            Summary = new List<ReportSummaryItemDto>
            {
                new() { Label = "Trips", Value = totalTrips.ToString() },
                new() { Label = "On-Time Rate", Value = totalCompleted == 0 ? "—" : $"{Math.Round((double)totalOnTime / totalCompleted * 100, 1)}%" },
                new() { Label = "Checkpoint Adherence", Value = totalCp2 == 0 ? "—" : $"{Math.Round((double)checkpointByTrip.Values.Sum(c => c.Visited) / totalCp2 * 100, 1)}%" },
                new() { Label = "Restricted-Zone Violations", Value = restricted.ToString() },
                new() { Label = "Missed Checkpoints", Value = (totalCp2 - checkpointByTrip.Values.Sum(c => c.Visited)).ToString() },
            }
        };
    }

    // ── 6. Alerts ───────────────────────────────────────────────────────────

    private static async Task<ReportResultDto> BuildAlertsAsync(
        ApplicationDbContext db, ReportRequestDto req, IReadOnlyList<Guid>? scope)
    {
        var from = req.From!.Value;
        var to = req.To!.Value;

        var alerts = await db.Alerts.AsNoTracking()
            .Where(a => (scope == null || scope.Contains(a.CompanyId))
                && a.CreatedAt >= from && a.CreatedAt <= to)
            .Select(a => new
            {
                a.CompanyId,
                CompanyName = a.Company.Name,
                a.AlertType,
                a.Severity,
                a.Status,
                a.CreatedAt
            })
            .ToListAsync();

        var rows = new List<Dictionary<string, object?>>();
        var multiCompany = scope == null;
        foreach (var g in alerts.GroupBy(a => new { a.CompanyId, a.AlertType }).OrderBy(g => g.Key.AlertType))
        {
            var severity = new Dictionary<string, int>();
            foreach (var a in g) severity[$"{a.Severity}"] = severity.GetValueOrDefault($"{a.Severity}") + 1;
            rows.Add(new Dictionary<string, object?>
            {
                ["company"] = multiCompany ? g.First().CompanyName : null,
                ["alertType"] = g.Key.AlertType,
                ["count"] = g.Count(),
                ["open"] = g.Count(a => a.Status == EntityStatus.Active),
                ["criticalHigh"] = g.Count(a => a.Severity == AlertSeverity.Critical || a.Severity == AlertSeverity.High),
                ["medium"] = g.Count(a => a.Severity == AlertSeverity.Medium),
                ["lowInfo"] = g.Count(a => a.Severity == AlertSeverity.Low || a.Severity == AlertSeverity.Info)
            });
        }

        var daily = alerts.GroupBy(a => a.CreatedAt.Date).OrderBy(g => g.Key).ToList();
        var volumeSeries = new ReportSeriesDto { Label = "Alerts" };
        foreach (var g in daily)
            volumeSeries.Points.Add(new ReportSeriesPointDto { X = g.Key.ToString("yyyy-MM-dd"), Y = g.Count() });

        var byType = alerts.GroupBy(a => a.AlertType).ToDictionary(g => g.Key, g => g.Count());
        var topType = byType.OrderByDescending(kv => kv.Value).FirstOrDefault();

        return new ReportResultDto
        {
            ReportType = "alerts",
            Title = "Alerts Report",
            From = from, To = to,
            Columns = new List<ReportColumnDto>
            {
                new() { Key = "company", Label = "Company", Type = "string" },
                new() { Key = "alertType", Label = "Alert Type", Type = "string" },
                new() { Key = "count", Label = "Count", Type = "number" },
                new() { Key = "open", Label = "Open", Type = "number" },
                new() { Key = "criticalHigh", Label = "Critical/High", Type = "number" },
                new() { Key = "medium", Label = "Medium", Type = "number" },
                new() { Key = "lowInfo", Label = "Low/Info", Type = "number" },
            },
            Rows = rows,
            Series = new List<ReportSeriesDto> { volumeSeries },
            Summary = new List<ReportSummaryItemDto>
            {
                new() { Label = "Total Alerts", Value = alerts.Count.ToString() },
                new() { Label = "Open", Value = alerts.Count(a => a.Status == EntityStatus.Active).ToString() },
                new() { Label = "Critical/High", Value = alerts.Count(a => a.Severity == AlertSeverity.Critical || a.Severity == AlertSeverity.High).ToString() },
                new() { Label = "Top Type", Value = topType.Key ?? "—" },
            }
        };
    }

    // ── Export: CSV ─────────────────────────────────────────────────────────

    public byte[] ToCsv(ReportResultDto result)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // UTF-8 BOM so Excel opens UTF-8 correctly.
        var visibleColumns = result.Columns.Where(c => c.Key != "company" || result.Rows.Any(r => r.ContainsKey("company") && r["company"] != null)).ToList();
        sb.Append(string.Join(",", visibleColumns.Select(c => CsvEscape(c.Label))));
        sb.Append('\n');
        foreach (var row in result.Rows)
        {
            sb.Append(string.Join(",", visibleColumns.Select(c => CsvEscape(FormatValue(row.GetValueOrDefault(c.Key), c.Type)))));
            sb.Append('\n');
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string FormatValue(object? value, string type)
    {
        if (value == null) return string.Empty;
        return type switch
        {
            "currency" => value is decimal d ? $"${d.ToString("N2", CultureInfo.InvariantCulture)}" : value.ToString() ?? string.Empty,
            "number" => value is decimal dm ? dm.ToString(CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            "date" => value is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm") : value.ToString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    // ── Export: PDF ─────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal dependency-free PDF writer: A4 pages, built-in Helvetica fonts,
    /// title + summary + a simple table. No external packages — a fleet report
    /// export must work offline in any deployment.
    /// </summary>
    public byte[] ToPdf(ReportResultDto result)
    {
        const float pageW = 595f, pageH = 842f, margin = 40f;
        var pageObjects = new List<string>();
        var pageRefs = new List<int>();
        var fontRef = 0;
        var boldRef = 0;
        var objectCount = 0;

        int NextRef() => ++objectCount;

        // Font objects (shared).
        fontRef = NextRef();
        boldRef = NextRef();
        pageObjects.Add($"{fontRef} 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");
        pageObjects.Add($"{boldRef} 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> endobj");

        var visibleColumns = result.Columns.ToList();
        if (visibleColumns.Count == 0) visibleColumns.Add(new ReportColumnDto { Key = "info", Label = "Info", Type = "string" });

        float y = pageH - 60;

        // Paginate: we render title+summary on page 1, table starting below.
        var pages = new List<List<string>>();
        var current = new List<string>();

        void FlushPage()
        {
            if (current.Count == 0) return;
            pages.Add(current);
            current = new List<string>();
            y = pageH - margin;
        }

        current.Add($"BT /F{boldRef} 16 Tf {margin.ToString("F1", CultureInfo.InvariantCulture)} {y.ToString("F1", CultureInfo.InvariantCulture)} Td ({PdfEscape(result.Title)}) Tj ET");
        y -= 28;

        var rangeText = result.From != null
            ? $"{result.From:yyyy-MM-dd} to {result.To:yyyy-MM-dd}"
            : string.Empty;
        current.Add($"BT /F{fontRef} 9 Tf {margin.ToString("F1", CultureInfo.InvariantCulture)} {y.ToString("F1", CultureInfo.InvariantCulture)} Td ({PdfEscape($"{result.Title} — generated {result.GeneratedAt:yyyy-MM-dd HH:mm} UTC" + (rangeText.Length > 0 ? $"  ·  {rangeText}" : ""))}) Tj ET");
        y -= 22;

        foreach (var s in result.Summary)
        {
            current.Add($"BT /F{fontRef} 9 Tf {margin.ToString("F1", CultureInfo.InvariantCulture)} {y.ToString("F1", CultureInfo.InvariantCulture)} Td ({PdfEscape($"{s.Label}: {s.Value}")}) Tj ET");
            y -= 14;
        }
        y -= 12;

        // Table header.
        void DrawHeader()
        {
            current.Add($"BT /F{boldRef} 9 Tf {margin.ToString("F1", CultureInfo.InvariantCulture)} {y.ToString("F1", CultureInfo.InvariantCulture)} Td ({PdfEscape(string.Join("   ", visibleColumns.Select(c => c.Label)))}) Tj ET");
            y -= 14;
        }
        DrawHeader();

        foreach (var row in result.Rows)
        {
            if (y < 50)
            {
                FlushPage();
                DrawHeader();
            }
            var cells = visibleColumns.Select(c => FormatValue(row.GetValueOrDefault(c.Key), c.Type)).ToList();
            var line = string.Join("   ", cells);
            if (line.Length > 130) line = line[..127] + "...";
            current.Add($"BT /F{fontRef} 8.5 Tf {margin.ToString("F1", CultureInfo.InvariantCulture)} {y.ToString("F1", CultureInfo.InvariantCulture)} Td ({PdfEscape(line)}) Tj ET");
            y -= 12;
        }
        FlushPage();

        // Assemble the PDF.
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsetByRef = new Dictionary<int, int>();
        var catalogRef = NextRef();
        var pagesRef = NextRef();
        var kidRefs = new List<int>();
        foreach (var page in pages)
        {
            var contentRef = NextRef();
            var pageRef = NextRef();
            kidRefs.Add(pageRef);

            var stream = string.Join("\n", page);
            var streamBytes = Encoding.ASCII.GetBytes(stream);
            var streamLen = streamBytes.Length;
            var obj = $"{pageRef} 0 obj\n<< /Type /Page /Parent {pagesRef} 0 R /MediaBox [0 0 {pageW.ToString("F1", CultureInfo.InvariantCulture)} {pageH.ToString("F1", CultureInfo.InvariantCulture)}] /Resources << /Font << /F{fontRef} {fontRef} 0 R /F{boldRef} {boldRef} 0 R >> >> /Contents {contentRef} 0 R >>\nendobj";
            pageObjects.Add(obj);

            var contentObj = $"{contentRef} 0 obj\n<< /Length {streamLen} >>\nstream\n{stream}\nendstream\nendobj";
            pageObjects.Add(contentObj);
        }

        // Catalog + pages go first in the object list for clean xref ordering.
        var ordered = new List<string>();
        ordered.Add($"{catalogRef} 0 obj << /Type /Catalog /Pages {pagesRef} 0 R >> endobj");
        ordered.Add($"{pagesRef} 0 obj << /Type /Pages /Kids [{(string.Join(" ", kidRefs.Select(r => $"{r} 0 R")))}] /Count {kidRefs.Count} >> endobj");
        // Fonts must come before pages reference them; pages reference font refs.
        // Build the object sequence: catalog, pages, fonts, then page/content objects.
        var fontObjects = pageObjects.Where(o => o.StartsWith($"{fontRef} 0 obj") || o.StartsWith($"{boldRef} 0 obj")).ToList();
        var pageObjectsRest = pageObjects.Where(o => !fontObjects.Contains(o)).ToList();
        var allObjects = new List<string>();
        allObjects.AddRange(ordered);
        allObjects.AddRange(fontObjects);
        allObjects.AddRange(pageObjectsRest);

        foreach (var obj in allObjects)
        {
            var refNum = int.Parse(obj[..obj.IndexOf(' ')]);
            offsetByRef[refNum] = sb.Length;
            sb.Append(obj);
            sb.Append('\n');
        }

        var xrefStart = sb.Length;
        var totalObjects = offsetByRef.Keys.Count;
        sb.Append($"xref\n0 {totalObjects + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i <= totalObjects; i++)
        {
            sb.Append((offsetByRef.TryGetValue(i, out var off) ? off : 0).ToString("D10", CultureInfo.InvariantCulture));
            sb.Append(" 00000 n \n");
        }
        sb.Append($"trailer\n<< /Size {totalObjects + 1} /Root {catalogRef} 0 R >>\nstartxref\n{xrefStart}\n%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string PdfEscape(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch == '(' || ch == ')' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch >= 32 && ch <= 126) sb.Append(ch);
            else sb.Append('?');
        }
        return sb.ToString();
    }
}