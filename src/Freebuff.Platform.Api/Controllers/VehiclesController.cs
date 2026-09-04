using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
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
public class VehiclesController : ControllerBase
{
    private readonly VehicleService _vehicleService;
    private readonly ApplicationDbContext _db;

    public VehiclesController(VehicleService vehicleService, ApplicationDbContext db)
    {
        _vehicleService = vehicleService;
        _db = db;
    }

    [HttpGet]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<PagedResult<VehicleDto>>>> GetAll([FromQuery] PagedRequest filter, [FromQuery] int? status = null)
    {
        var result = await _vehicleService.GetListAsync(filter, status);
        return Ok(ApiResponse<PagedResult<VehicleDto>>.Ok(result));
    }

    [HttpGet("stats")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<object>>> GetStats()
    {
        var query = _db.Vehicles.AsNoTracking().Where(v => !v.IsDeleted);
        if (!User.IsSuperAdmin())
        {
            var tenantId = User.GetTenantId();
            query = query.Where(v => v.CompanyId == tenantId);
        }

        var total = await query.CountAsync();
        var active = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Active);
        var inactive = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Inactive);
        var maintenance = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.InMaintenance);
        var retired = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Retired);
        var stolen = await query.CountAsync(v => v.Status == Domain.Enums.VehicleStatus.Stolen);
        var withDriver = await query.CountAsync(v => v.DriverId != null);
        var withDevice = await query.CountAsync(v => v.DeviceImei != null);

        return Ok(ApiResponse<object>.Ok(new
        {
            total, active, inactive, maintenance, retired, stolen,
            withDriver, withDevice,
            unassigned = total - withDriver
        }));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> GetById(Guid id)
    {
        var result = await _vehicleService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpPost]
    [RequirePermission("vehicle.create")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Create([FromBody] CreateVehicleDto dto)
    {

        var userId = User.GetUserIdString();
        var result = await _vehicleService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("vehicle.update")]
    public async Task<ActionResult<ApiResponse<VehicleDto>>> Update(Guid id, [FromBody] UpdateVehicleDto dto)
    {

        var userId = User.GetUserIdString();
        var result = await _vehicleService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<VehicleDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<VehicleDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("vehicle.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {

        var userId = User.GetUserIdString();
        var deleted = await _vehicleService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse.Ok(message: "Vehicle deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    [RequirePermission("vehicle.update")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {

        var userId = User.GetUserIdString();
        var restored = await _vehicleService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Vehicle not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Vehicle restored"));
    }

    [HttpGet("{id:guid}/audit")]
    [RequirePermission("vehicle.view")]
    public async Task<ActionResult<ApiResponse<List<AuditEntryDto>>>> GetAuditHistory(Guid id)
    {
        var result = await _vehicleService.GetAuditHistoryAsync(id);
        return Ok(ApiResponse<List<AuditEntryDto>>.Ok(result));
    }
}
