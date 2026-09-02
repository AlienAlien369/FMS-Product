using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Extensions;
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
    public DashboardController(ApplicationDbContext db) => _db = db;

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<object>>> GetStats()
    {
        var tenantId = User.GetTenantId();
        var now = DateTime.UtcNow;

        var totalVehicles = await _db.Vehicles.CountAsync(v => v.CompanyId == tenantId && !v.IsDeleted);
        var activeVehicles = await _db.Vehicles.CountAsync(v => v.CompanyId == tenantId && !v.IsDeleted && v.Status == VehicleStatus.Active);
        var maintenanceVehicles = await _db.Vehicles.CountAsync(v => v.CompanyId == tenantId && !v.IsDeleted && v.Status == VehicleStatus.InMaintenance);
        var totalDrivers = await _db.Drivers.CountAsync(d => d.CompanyId == tenantId && !d.IsDeleted);
        var activeDrivers = await _db.Drivers.CountAsync(d => d.CompanyId == tenantId && !d.IsDeleted && d.Status == DriverStatus.Active);
        var onTripDrivers = await _db.Drivers.CountAsync(d => d.CompanyId == tenantId && !d.IsDeleted && d.Status == DriverStatus.OnTrip);
        var totalTrips = await _db.Trips.CountAsync(t => t.CompanyId == tenantId && !t.IsDeleted);
        var activeTrips = await _db.Trips.CountAsync(t => t.CompanyId == tenantId && !t.IsDeleted && (t.Status == TripStatus.InProgress || t.Status == TripStatus.Started));
        var totalUsers = await _db.Users.CountAsync(u => u.CompanyId == tenantId && !u.IsDeleted);
        var totalGeofences = await _db.Geofences.CountAsync(g => g.CompanyId == tenantId && !g.IsDeleted);

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
    public async Task<ActionResult<ApiResponse<object>>> GetVehiclesByStatus()
    {
        var tenantId = User.GetTenantId();
        var data = await _db.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == tenantId && !v.IsDeleted)
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("vehicles/by-fuel-type")]
    public async Task<ActionResult<ApiResponse<object>>> GetVehiclesByFuelType()
    {
        var tenantId = User.GetTenantId();
        var data = await _db.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == tenantId && !v.IsDeleted)
            .GroupBy(v => v.FuelType)
            .Select(g => new { FuelType = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("drivers/by-status")]
    public async Task<ActionResult<ApiResponse<object>>> GetDriversByStatus()
    {
        var tenantId = User.GetTenantId();
        var data = await _db.Drivers.AsNoTracking()
            .Where(d => d.CompanyId == tenantId && !d.IsDeleted)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("drivers/top-safety")]
    public async Task<ActionResult<ApiResponse<object>>> GetTopSafetyDrivers()
    {
        var tenantId = User.GetTenantId();
        var data = await _db.Drivers.AsNoTracking()
            .Where(d => d.CompanyId == tenantId && !d.IsDeleted && d.SafetyScore.HasValue)
            .OrderByDescending(d => d.SafetyScore)
            .Take(8)
            .Select(d => new { d.Id, Name = d.FirstName + " " + d.LastName, d.SafetyScore, d.BehaviourScore, Status = d.Status.ToString() })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("vehicles/recent")]
    public async Task<ActionResult<ApiResponse<object>>> GetRecentVehicles()
    {
        var tenantId = User.GetTenantId();
        var data = await _db.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == tenantId && !v.IsDeleted)
            .OrderByDescending(v => v.LastLocationUpdate ?? v.CreatedAt)
            .Take(6)
            .Select(v => new
            {
                v.Id, v.RegistrationNumber, v.Name, v.Make, v.Model,
                Status = v.Status.ToString(),
                Speed = v.LastSpeed,
                Ignition = v.IgnitionStatus,
                DriverName = v.Driver != null ? v.Driver.FirstName + " " + v.Driver.LastName : null
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(data));
    }
}
