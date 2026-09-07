using System.Security.Claims;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DriversController : ControllerBase
{
    private readonly DriverService _driverService;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DriversController(DriverService driverService, ApplicationDbContext db, ITenantContext tenant)
    {
        _driverService = driverService;
        _db = db;
        _tenant = tenant;
    }

    [HttpGet]
    [RequirePermission("driver.view")]
    public async Task<ActionResult<ApiResponse<PagedResult<DriverDto>>>> GetAll([FromQuery] PagedRequest filter, [FromQuery] int? status = null)
    {
        var result = await _driverService.GetListAsync(filter, status);
        return Ok(ApiResponse<PagedResult<DriverDto>>.Ok(result));
    }

    [HttpGet("stats")]
    [RequirePermission("driver.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetStats()
    {
        // Query-side: effective scope = X-Company-Scope ∩ permitted set.
        var query = _db.Drivers.AsNoTracking().Where(d => !d.IsDeleted);
        query = query.InEffectiveCompanyScope(_tenant.Scope, d => d.CompanyId);

        var total = await query.CountAsync();
        var active = await query.CountAsync(d => d.Status == DriverStatus.Active);
        var inactive = await query.CountAsync(d => d.Status == DriverStatus.Inactive);
        var onTrip = await query.CountAsync(d => d.Status == DriverStatus.OnTrip);
        var offDuty = await query.CountAsync(d => d.Status == DriverStatus.OffDuty);
        var suspended = await query.CountAsync(d => d.Status == DriverStatus.Suspended);
        var avgSafety = await query.Where(d => d.SafetyScore.HasValue).AverageAsync(d => (double?)d.SafetyScore) ?? 0;
        var avgBehaviour = await query.Where(d => d.BehaviourScore.HasValue).AverageAsync(d => (double?)d.BehaviourScore) ?? 0;

        return Ok(ApiResponse<object>.Ok(new
        {
            total, active, inactive, onTrip, offDuty, suspended,
            avgSafety = Math.Round(avgSafety, 1),
            avgBehaviour = Math.Round(avgBehaviour, 1)
        }));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("driver.view")]
    public async Task<ActionResult<ApiResponse<DriverDto>>> GetById(Guid id)
    {
        var result = await _driverService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<DriverDto>.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse<DriverDto>.Ok(result));
    }

    [HttpPost]
    [RequirePermission("driver.create")]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Create([FromBody] CreateDriverDto dto)
    {

        var userId = User.GetUserIdString();
        var result = await _driverService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DriverDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("driver.update")]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Update(Guid id, [FromBody] UpdateDriverDto dto)
    {

        var userId = User.GetUserIdString();
        var result = await _driverService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<DriverDto>.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse<DriverDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("driver.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {

        var userId = User.GetUserIdString();
        var deleted = await _driverService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse.Ok(message: "Driver deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("driver.update")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {

        var userId = User.GetUserIdString();
        var restored = await _driverService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Driver not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Driver restored"));
    }

    [HttpGet("{id:guid}/audit")]
    [RequirePermission("driver.view")]
    public async Task<ActionResult<ApiResponse<List<AuditEntryDto>>>> GetAuditHistory(Guid id)
    {
        var result = await _driverService.GetAuditHistoryAsync(id);
        return Ok(ApiResponse<List<AuditEntryDto>>.Ok(result));
    }

    /// <summary>
    /// Recent driver-behavior / DMS events for the driver (Safety Events tab).
    /// Scorecard groundwork: the rows are indexed by (DriverId, EventTimeUtc) at
    /// write time, so filtering by driver + date range is a plain range query.
    /// </summary>
    [HttpGet("{id:guid}/safety-events")]
    [RequirePermission("driver.view")]
    public async Task<ActionResult<ApiResponse<List<DriverBehaviorEventDto>>>> GetSafetyEvents(Guid id,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] int limit = 50)
    {
        var tenantId = Guid.Parse(User.FindFirstValue("tenant_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isSuperAdmin = User.IsInRole("SuperAdmin") || User.Claims.Any(c => c.Type == "is_super_admin" && c.Value == "true");
        var owned = await _db.Drivers.AsNoTracking()
            .AnyAsync(d => d.Id == id && !d.IsDeleted && (isSuperAdmin || d.CompanyId == tenantId));
        if (!owned) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Driver not found"));

        var query = _db.DriverBehaviorEvents.AsNoTracking()
            .Where(e => e.DriverId == id && (isSuperAdmin || e.TenantId == tenantId));
        if (from.HasValue) query = query.Where(e => e.EventTimeUtc >= from.Value);
        if (to.HasValue) query = query.Where(e => e.EventTimeUtc <= to.Value);

        var events = await query.OrderByDescending(e => e.EventTimeUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(e => new DriverBehaviorEventDto
            {
                Id = e.Id,
                CompanyId = e.TenantId,
                VehicleId = e.VehicleId,
                VehicleName = e.VehicleId != null ? e.Vehicle!.RegistrationNumber : null,
                DriverId = e.DriverId,
                EventType = DriverBehaviorCatalog.Spec(e.EventType).CanonicalCode,
                EventTypeName = DriverBehaviorCatalog.Spec(e.EventType).AlertName,
                Confidence = e.Confidence,
                Severity = (int)DriverBehaviorCatalog.Spec(e.EventType).Severity,
                EventTimeUtc = e.EventTimeUtc,
                Latitude = e.Latitude,
                Longitude = e.Longitude,
                SpeedKmh = e.SpeedKmh,
                MediaUrl = e.MediaUrl
            })
            .ToListAsync();

        return Ok(ApiResponse<List<DriverBehaviorEventDto>>.Ok(events));
    }
}
