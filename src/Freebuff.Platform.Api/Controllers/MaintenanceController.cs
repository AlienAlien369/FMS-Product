using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class MaintenanceController : ControllerBase
{
    private readonly MaintenanceService _maintenance;
    private readonly ITenantContext _tenant;
    private readonly ApplicationDbContext _db;

    public MaintenanceController(MaintenanceService maintenance, ITenantContext tenant, ApplicationDbContext db)
    {
        _maintenance = maintenance;
        _tenant = tenant;
        _db = db;
    }

    [HttpGet]
    [RequirePermission("maintenance.view")]
    public async Task<ActionResult<ApiResponse<List<MaintenanceScheduleDto>>>> GetSchedules([FromQuery] Guid? vehicleId)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var schedules = await _maintenance.GetSchedulesAsync(scope, vehicleId);
        return Ok(ApiResponse<List<MaintenanceScheduleDto>>.Ok(schedules));
    }

    /// <summary>Fleet-wide due/overdue board — the page a fleet manager checks every morning.</summary>
    [HttpGet("overview")]
    [RequirePermission("maintenance.view")]
    public async Task<ActionResult<ApiResponse<MaintenanceFleetOverviewDto>>> GetOverview()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var overview = await _maintenance.GetFleetOverviewAsync(scope);
        return Ok(ApiResponse<MaintenanceFleetOverviewDto>.Ok(overview));
    }

    [HttpGet("records")]
    [RequirePermission("maintenance.view")]
    public async Task<ActionResult<ApiResponse<List<MaintenanceRecordDto>>>> GetRecords([FromQuery] Guid? vehicleId, [FromQuery] int take = 100)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var records = await _maintenance.GetRecordsAsync(scope, vehicleId, Math.Clamp(take, 1, 500));
        return Ok(ApiResponse<List<MaintenanceRecordDto>>.Ok(records));
    }

    [HttpGet("vehicles/{vehicleId:guid}")]
    [RequirePermission("maintenance.view")]
    public async Task<ActionResult<ApiResponse<MaintenanceVehicleDetailDto>>> GetVehicleDetail(Guid vehicleId)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var detail = await _maintenance.GetVehicleDetailAsync(scope, vehicleId);
        if (detail == null) return NotFound(ApiResponse<MaintenanceVehicleDetailDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<MaintenanceVehicleDetailDto>.Ok(detail));
    }

    [HttpPost("schedules")]
    [RequirePermission("maintenance.create")]
    public async Task<ActionResult<ApiResponse<MaintenanceScheduleDto>>> CreateSchedule([FromBody] CreateMaintenanceScheduleDto dto)
    {
        if (dto.IntervalValue <= 0)
            return BadRequest(ApiResponse<MaintenanceScheduleDto>.Fail("INVALID_INTERVAL", "Interval must be greater than zero"));

        var companyId = await ResolveVehicleCompanyAsync(dto.VehicleId);
        if (companyId == null)
            return NotFound(ApiResponse<MaintenanceScheduleDto>.Fail("NOT_FOUND", "Vehicle not found"));

        var schedule = await _maintenance.CreateScheduleAsync(companyId.Value, dto, _tenant.UserId);
        return CreatedAtAction(nameof(GetSchedules), ApiResponse<MaintenanceScheduleDto>.Ok(schedule));
    }

    [HttpPut("schedules/{id:guid}")]
    [RequirePermission("maintenance.update")]
    public async Task<ActionResult<ApiResponse<MaintenanceScheduleDto>>> UpdateSchedule(Guid id, [FromBody] UpdateMaintenanceScheduleDto dto)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return Forbid();
        var schedule = await _maintenance.UpdateScheduleAsync(tenantId.Value, id, dto, _tenant.UserId);
        if (schedule == null) return NotFound(ApiResponse<MaintenanceScheduleDto>.Fail("NOT_FOUND", "Schedule not found"));
        return Ok(ApiResponse<MaintenanceScheduleDto>.Ok(schedule));
    }

    [HttpDelete("schedules/{id:guid}")]
    [RequirePermission("maintenance.delete")]
    public async Task<ActionResult<ApiResponse>> DeleteSchedule(Guid id)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return Forbid();
        var deleted = await _maintenance.DeleteScheduleAsync(tenantId.Value, id);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Schedule not found"));
        return Ok(ApiResponse.Ok(message: "Schedule deleted"));
    }

    [HttpPost("records")]
    [RequirePermission("maintenance.create")]
    public async Task<ActionResult<ApiResponse<MaintenanceRecordDto>>> LogRecord([FromBody] LogMaintenanceRecordDto dto)
    {
        // A preventive record linked to a schedule must reference a schedule of
        // the SAME vehicle (cross-vehicle completion would corrupt next-due math).
        if (dto.RecordType == MaintenanceRecordType.Preventive && dto.MaintenanceScheduleId.HasValue)
        {
            var match = await _db.MaintenanceSchedules.AsNoTracking()
                .AnyAsync(s => s.Id == dto.MaintenanceScheduleId.Value && !s.IsDeleted && s.VehicleId == dto.VehicleId);
            if (!match)
                return BadRequest(ApiResponse<MaintenanceRecordDto>.Fail("SCHEDULE_MISMATCH", "Schedule does not belong to this vehicle"));
        }

        var companyId = await ResolveVehicleCompanyAsync(dto.VehicleId);
        if (companyId == null)
            return NotFound(ApiResponse<MaintenanceRecordDto>.Fail("NOT_FOUND", "Vehicle not found"));

        var record = await _maintenance.LogRecordAsync(companyId.Value, dto, _tenant.UserId);
        return CreatedAtAction(nameof(GetRecords), ApiResponse<MaintenanceRecordDto>.Ok(record));
    }

    [HttpDelete("records/{id:guid}")]
    [RequirePermission("maintenance.delete")]
    public async Task<ActionResult<ApiResponse>> DeleteRecord(Guid id)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return Forbid();
        var deleted = await _maintenance.DeleteRecordAsync(tenantId.Value, id);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Record not found"));
        return Ok(ApiResponse.Ok(message: "Record deleted"));
    }

    private async Task<Guid?> ResolveVehicleCompanyAsync(Guid vehicleId)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        return await _db.Vehicles.AsNoTracking()
            .Where(v => v.Id == vehicleId && !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)))
            .Select(v => (Guid?)v.CompanyId)
            .FirstOrDefaultAsync();
    }

    /// <summary>Force re-evaluation of all due/overdue schedules in scope (fires alerts).</summary>
    [HttpPost("evaluate")]
    [RequirePermission("maintenance.update")]
    public async Task<ActionResult<ApiResponse>> EvaluateFleet()
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var schedules = await _maintenance.GetSchedulesAsync(scope, null);
        var vehicleIds = schedules.Select(s => s.VehicleId).Distinct().ToList();
        foreach (var vehicleId in vehicleIds)
        {
            await _maintenance.EvaluateVehicleAsync(schedules.First(s => s.VehicleId == vehicleId).CompanyId, vehicleId);
        }
        return Ok(ApiResponse.Ok(message: $"Evaluated {vehicleIds.Count} vehicles"));
    }
}