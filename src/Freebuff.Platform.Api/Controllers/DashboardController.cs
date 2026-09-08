using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    public DashboardController(ApplicationDbContext db, ITenantContext tenant) { _db = db; _tenant = tenant; }

    [HttpGet("stats")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetStats()
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set (dashboards).
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var now = DateTime.UtcNow;

        var totalVehicles = await _db.Vehicles.CountAsync(v => !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)));
        var activeVehicles = await _db.Vehicles.CountAsync(v => !v.IsDeleted && v.Status == VehicleStatus.Active && (scope == null || scope.Contains(v.CompanyId)));
        var maintenanceVehicles = await _db.Vehicles.CountAsync(v => !v.IsDeleted && v.Status == VehicleStatus.InMaintenance && (scope == null || scope.Contains(v.CompanyId)));
        var totalDrivers = await _db.Drivers.CountAsync(d => !d.IsDeleted && (scope == null || scope.Contains(d.CompanyId)));
        var activeDrivers = await _db.Drivers.CountAsync(d => !d.IsDeleted && d.Status == DriverStatus.Active && (scope == null || scope.Contains(d.CompanyId)));
        var onTripDrivers = await _db.Drivers.CountAsync(d => !d.IsDeleted && d.Status == DriverStatus.OnTrip && (scope == null || scope.Contains(d.CompanyId)));
        var totalTrips = await _db.Trips.CountAsync(t => !t.IsDeleted && (scope == null || scope.Contains(t.CompanyId)));
        var activeTrips = await _db.Trips.CountAsync(t => !t.IsDeleted && t.Status == TripStatus.InProgress && (scope == null || scope.Contains(t.CompanyId)));
        var totalUsers = await _db.Users.CountAsync(u => !u.IsDeleted && (scope == null || scope.Contains(u.CompanyId)));
        var totalGeofences = await _db.Geofences.CountAsync(g => !g.IsDeleted && (scope == null || scope.Contains(g.CompanyId)));

        return Ok(ApiResponse<object>.Ok(new
        {
            vehicles = new { total = totalVehicles, active = activeVehicles, maintenance = maintenanceVehicles },
            drivers = new { total = totalDrivers, active = activeDrivers, onTrip = onTripDrivers },
            trips = new { total = totalTrips, active = activeTrips },
            users = totalUsers,
            geofences = totalGeofences
        }));
    }

    [HttpGet("vehicles/by-status")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetVehiclesByStatus()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var data = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)))
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("vehicles/by-fuel-type")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetVehiclesByFuelType()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var data = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)))
            .GroupBy(v => v.FuelType)
            .Select(g => new { FuelType = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("drivers/by-status")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetDriversByStatus()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var data = await _db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && (scope == null || scope.Contains(d.CompanyId)))
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("drivers/top-safety")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetTopSafetyDrivers()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var data = await _db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && d.SafetyScore.HasValue && (scope == null || scope.Contains(d.CompanyId)))
            .OrderByDescending(d => d.SafetyScore)
            .Take(8)
            .Select(d => new { d.Id, Name = d.FirstName + " " + d.LastName, d.SafetyScore, d.BehaviourScore, Status = d.Status.ToString() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    /// <summary>
    /// Driver Safety Scores widget — wired to REAL data from the driver-behavior
    /// event pipeline (scorecard groundwork). Scores are derived from raw
    /// DriverBehaviorEvent rows via DriverSafetyScoring (100 − per-type
    /// deductions), with a per-event-code breakdown; drivers with no events are
    /// excluded until the scorecard feature formalizes baseline scoring.
    /// </summary>
    /// <summary>
    /// Fleet operational health widget: how many vehicles are currently over the
    /// speed policy and how many currently show a tyre pressure anomaly (any tyre
    /// warning or critical on their latest reading). Vehicles without a policy
    /// or without TPMS-bearing devices simply don't count.
    /// </summary>
    [HttpGet("fleet-health")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetFleetHealth()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var vehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)))
            .Select(v => new { v.Id, v.CompanyId, v.RegistrationNumber, v.SpeedPolicyMaxKmh, v.TyrePressureMinBar, v.TyrePressureMaxBar })
            .ToListAsync();
        if (vehicles.Count == 0)
            return Ok(ApiResponse<object>.Ok(new { overSpeedCount = 0, tyreAnomalyCount = 0, withSpeedPolicy = 0, withTyrePolicy = 0 }));

        // Company-level speed + tyre defaults, keyed by company (only companies in scope).
        var companyIds = vehicles.Select(v => v.CompanyId).Distinct().ToList();
        var configs = await _db.Configurations.AsNoTracking()
            .Where(c => companyIds.Contains(c.CompanyId ?? Guid.Empty) && c.Scope == Domain.Enums.ConfigurationScope.Company && !c.IsDeleted
                && (c.Key == Freebuff.Platform.Infrastructure.Services.FleetPolicyService.SpeedPolicyKey
                    || c.Key == Freebuff.Platform.Infrastructure.Services.FleetPolicyService.TyreMinPolicyKey
                    || c.Key == Freebuff.Platform.Infrastructure.Services.FleetPolicyService.TyreMaxPolicyKey))
            .Select(c => new { c.CompanyId, c.Key, c.Value })
            .ToListAsync();

        double? CompanyValue(Guid companyId, string key)
            => configs.Where(c => c.CompanyId == companyId && c.Key == key)
                .Select(c => double.TryParse(c.Value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
                .FirstOrDefault();

        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var latestSpeeds = await _db.TelemetryStates.AsNoTracking()
            .Where(s => vehicleIds.Contains(s.VehicleId))
            .Select(s => new { s.VehicleId, s.SpeedKmh })
            .ToListAsync();
        // Latest tyre-bearing event time per vehicle, then that event's readings.
        var latestTyreTimes = await _db.TelemetryEvents.AsNoTracking()
            .Where(e => e.VehicleId.HasValue && vehicleIds.Contains(e.VehicleId.Value) && e.TyrePressureReadings.Any())
            .GroupBy(e => e.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, MaxTime = g.Max(e => e.EventTimeUtc) })
            .ToListAsync();
        var latestTyres = new List<Domain.Entities.TyrePressureReading>();
        foreach (var t in latestTyreTimes)
        {
            latestTyres.AddRange(await _db.TyrePressureReadings.AsNoTracking()
                .Where(r => r.VehicleId == t.VehicleId && r.EventTimeUtc == t.MaxTime)
                .ToListAsync());
        }

        var overSpeed = 0;
        var tyreAnomaly = 0;
        var withSpeedPolicy = 0;
        var withTyrePolicy = 0;
        foreach (var v in vehicles)
        {
            var speedMax = v.SpeedPolicyMaxKmh ?? CompanyValue(v.CompanyId, Freebuff.Platform.Infrastructure.Services.FleetPolicyService.SpeedPolicyKey);
            if (speedMax.HasValue)
            {
                withSpeedPolicy++;
                var speed = latestSpeeds.FirstOrDefault(s => s.VehicleId == v.Id)?.SpeedKmh;
                if (speed.HasValue && speed.Value > speedMax.Value) overSpeed++;
            }

            var tyreMin = v.TyrePressureMinBar ?? CompanyValue(v.CompanyId, Freebuff.Platform.Infrastructure.Services.FleetPolicyService.TyreMinPolicyKey);
            var tyreMax = v.TyrePressureMaxBar ?? CompanyValue(v.CompanyId, Freebuff.Platform.Infrastructure.Services.FleetPolicyService.TyreMaxPolicyKey);
            if (tyreMin.HasValue && tyreMax.HasValue)
            {
                withTyrePolicy++;
                var any = latestTyres.Where(r => r.VehicleId == v.Id).Any(r =>
                    Freebuff.Platform.Infrastructure.Services.SensorPolicyAlertProducer
                        .ClassifyStatic(r.PressureBar, tyreMin.Value, tyreMax.Value, out var sev) != null && sev != null);
                if (any) tyreAnomaly++;
            }
        }

        return Ok(ApiResponse<object>.Ok(new { overSpeedCount = overSpeed, tyreAnomalyCount = tyreAnomaly, withSpeedPolicy, withTyrePolicy }));
    }

    [HttpGet("drivers/safety-scores")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetDriverSafetyScores()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var scopedDriverIds = _db.Drivers.AsNoTracking()
            .Where(d => !d.IsDeleted && (scope == null || scope.Contains(d.CompanyId)))
            .Select(d => d.Id);

        // Raw events in scope — small per-company volume; aggregation in memory
        // (the future Driver Scorecard can push this into a SQL aggregate).
        var raw = await _db.DriverBehaviorEvents.AsNoTracking()
            .Where(e => e.DriverId != null && scopedDriverIds.Contains(e.DriverId.Value))
            .Select(e => new { e.DriverId, e.EventType })
            .ToListAsync();
        if (raw.Count == 0)
            return Ok(ApiResponse<object>.Ok(new List<DriverSafetyScoreDto>()));

        var driverIds = raw.Select(r => r.DriverId!.Value).Distinct().ToList();
        var names = await _db.Drivers.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, Name = d.FirstName + " " + d.LastName })
            .ToListAsync();
        var nameByDriver = names.ToDictionary(n => n.Id, n => n.Name);

        var result = raw
            .GroupBy(r => r.DriverId!.Value)
            .Select(g => new DriverSafetyScoreDto
            {
                DriverId = g.Key,
                Name = nameByDriver.TryGetValue(g.Key, out var n) ? n : "Unknown driver",
                Score = DriverSafetyScoring.Score(g.Select(r => r.EventType)),
                EventCount = g.Count(),
                Breakdown = g.GroupBy(r => r.EventType.ToString())
                    .ToDictionary(x => x.Key, x => x.Count())
            })
            .OrderByDescending(s => s.Score)
            .Take(8)
            .ToList();

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Driver Safety Scores widget wired to REAL materialized scorecard data:
    /// fleet average, distribution buckets, drivers below the critical threshold,
    /// and how many drivers are in the insufficient-data state (never shown as a
    /// misleading number).
    /// </summary>
    [HttpGet("drivers/scorecard-overview")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<DriverScoreOverviewDto>>> GetScorecardOverview()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var periods = await _db.DriverScorePeriods.AsNoTracking()
            .Where(p => p.Window == "30d" && (scope == null || scope.Contains(p.CompanyId)))
            .GroupBy(p => p.DriverId)
            .Select(g => g.OrderByDescending(p => p.AnchorDate).ThenByDescending(p => p.ComputedAt).First())
            .ToListAsync();

        var composites = periods.Where(p => p.CompositeScore.HasValue).Select(p => p.CompositeScore!.Value).ToList();
        decimal BucketIndex(decimal c) => c <= 20 ? 0 : c <= 40 ? 1 : c <= 60 ? 2 : c <= 80 ? 3 : 4;
        var bucketNames = new[] { "0-20", "21-40", "41-60", "61-80", "81-100" };
        var distribution = bucketNames
            .Select((name, i) => new ScoreBucketDto { Bucket = name, Count = composites.Count(c => BucketIndex(c) == i) })
            .ToList();

        return Ok(ApiResponse<DriverScoreOverviewDto>.Ok(new DriverScoreOverviewDto
        {
            Average = composites.Count == 0 ? 0 : Math.Round(composites.Average(), 1),
            BelowThresholdCount = composites.Count(c => c <= 40),
            Threshold = 40,
            InsufficientDataCount = periods.Count(p => !p.CompositeScore.HasValue),
            Distribution = distribution
        }));
    }

    /// <summary>
    /// Fuel consumption/cost widget (next to "Vehicles by Fuel Type"): fleet
    /// totals for the last 30 days — spend, liters, efficiency, cost/km — plus
    /// the top consumers, so the dashboard surfaces fuel operations without a
    /// trip to the Fuel page.
    /// </summary>
    [HttpGet("fuel-overview")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetFuelOverview()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var since = DateTime.UtcNow.AddDays(-30);

        var rows = await _db.FuelRecords.AsNoTracking()
            .Where(f => !f.IsDeleted && f.RecordDate >= since && (scope == null || scope.Contains(f.CompanyId)))
            .Select(f => new { f.Quantity, f.TotalCost, f.DistanceTraveledKm, f.VehicleId, f.IsAnomaly })
            .ToListAsync();

        var totalLiters = rows.Sum(r => r.Quantity);
        var totalSpend = rows.Sum(r => r.TotalCost ?? 0m);
        var totalDistance = rows.Sum(r => r.DistanceTraveledKm ?? 0m);

        var vehicleIds = rows.Select(r => r.VehicleId).Distinct().ToList();
        var vehicles = vehicleIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Vehicles.AsNoTracking()
                .Where(v => vehicleIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, v => v.RegistrationNumber);

        var topConsumers = rows
            .GroupBy(r => r.VehicleId)
            .Select(g => new
            {
                vehicleId = g.Key,
                registration = vehicles.TryGetValue(g.Key, out var reg) ? reg : "Unknown",
                liters = Math.Round(g.Sum(r => r.Quantity), 1),
                spend = g.Sum(r => r.TotalCost ?? 0m)
            })
            .OrderByDescending(x => x.liters)
            .Take(5)
            .ToList();

        return Ok(ApiResponse<object>.Ok(new
        {
            totalLiters = Math.Round(totalLiters, 1),
            totalSpend = Math.Round(totalSpend, 2),
            transactionCount = rows.Count,
            anomalyCount = rows.Count(r => r.IsAnomaly),
            avgEfficiencyKmPerLiter = totalDistance > 0 && totalLiters > 0 ? Math.Round(totalDistance / totalLiters, 2) : (decimal?)null,
            costPerKm = totalDistance > 0 ? Math.Round(totalSpend / totalDistance, 4) : (decimal?)null,
            topConsumers
        }));
    }

    [HttpGet("vehicles/recent")]
    [RequirePermission("dashboard.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetRecentVehicles()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var data = await _db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)))
            .OrderByDescending(v => v.LastLocationUpdate ?? v.CreatedAt)
            .Take(6)
            .Select(v => new
            {
                v.Id, v.RegistrationNumber, v.Name, v.Make, v.Model,
                CompanyName = v.Company != null ? v.Company.Name : null,
                Status = v.Status.ToString(),
                Speed = v.LastSpeed,
                Ignition = v.IgnitionStatus,
                DriverName = v.Driver != null ? v.Driver.FirstName + " " + v.Driver.LastName : null
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }
}
