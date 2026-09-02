using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
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

    public DriversController(DriverService driverService, ApplicationDbContext db)
    {
        _driverService = driverService;
        _db = db;
    }

    private async Task<bool> HasPermissionAsync(string permissionCode)
    {
        if (User.IsSuperAdmin()) return true;
        var userId = User.GetUserId();
        return await _db.UserRoles
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .SelectMany(ur => ur.Role.RolePermissions)
            .AnyAsync(rp => !rp.IsDeleted && rp.Permission.Code == permissionCode);
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<DriverDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var result = await _driverService.GetListAsync(filter);
        return Ok(ApiResponse<PagedResult<DriverDto>>.Ok(result));
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<object>>> GetStats()
    {
        var query = _db.Drivers.AsNoTracking().Where(d => !d.IsDeleted);
        if (!User.IsSuperAdmin())
        {
            var tenantId = User.GetTenantId();
            query = query.Where(d => d.CompanyId == tenantId);
        }

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
    public async Task<ActionResult<ApiResponse<DriverDto>>> GetById(Guid id)
    {
        var result = await _driverService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<DriverDto>.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse<DriverDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Create([FromBody] CreateDriverDto dto)
    {
        if (!await HasPermissionAsync("driver.create"))
            return StatusCode(403, ApiResponse<DriverDto>.Fail("FORBIDDEN", "You do not have permission to create drivers"));

        var userId = User.GetUserIdString();
        var result = await _driverService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<DriverDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<DriverDto>>> Update(Guid id, [FromBody] UpdateDriverDto dto)
    {
        if (!await HasPermissionAsync("driver.edit"))
            return StatusCode(403, ApiResponse<DriverDto>.Fail("FORBIDDEN", "You do not have permission to edit drivers"));

        var userId = User.GetUserIdString();
        var result = await _driverService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<DriverDto>.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse<DriverDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {
        if (!await HasPermissionAsync("driver.delete"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "You do not have permission to delete drivers"));

        var userId = User.GetUserIdString();
        var deleted = await _driverService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Driver not found"));
        return Ok(ApiResponse.Ok(message: "Driver deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {
        if (!await HasPermissionAsync("driver.edit"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "You do not have permission to restore drivers"));

        var userId = User.GetUserIdString();
        var restored = await _driverService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Driver not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Driver restored"));
    }

    [HttpGet("{id:guid}/audit")]
    public async Task<ActionResult<ApiResponse<List<AuditEntryDto>>>> GetAuditHistory(Guid id)
    {
        var result = await _driverService.GetAuditHistoryAsync(id);
        return Ok(ApiResponse<List<AuditEntryDto>>.Ok(result));
    }
}
