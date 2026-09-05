using Freebuff.Platform.Api.Authorization;
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
