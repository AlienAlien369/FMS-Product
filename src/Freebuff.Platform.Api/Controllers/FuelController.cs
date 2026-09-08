using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
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
public class FuelController : ControllerBase
{
    private readonly FuelService _fuel;
    private readonly ITenantContext _tenant;
    private readonly ApplicationDbContext _db;

    public FuelController(FuelService fuel, ITenantContext tenant, ApplicationDbContext db)
    {
        _fuel = fuel;
        _tenant = tenant;
        _db = db;
    }

    [HttpGet]
    [RequirePermission("fuel.view")]
    public async Task<ActionResult<ApiResponse<PagedResult<FuelRecordDto>>>> GetAll(
        [FromQuery] PagedRequest filter, [FromQuery] Guid? vehicleId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        if (vehicleId.HasValue && !await VehicleInScopeAsync(vehicleId.Value, scope))
            return NotFound(ApiResponse<PagedResult<FuelRecordDto>>.Fail("NOT_FOUND", "Vehicle not found"));

        var result = await _fuel.GetTransactionsAsync(scope, vehicleId, from, to,
            Math.Max(1, filter.Page), Math.Clamp(filter.PageSize, 1, 200), filter.Search);
        return Ok(ApiResponse<PagedResult<FuelRecordDto>>.Ok(result));
    }

    [HttpGet("stats")]
    [RequirePermission("fuel.view")]
    public async Task<ActionResult<ApiResponse<FuelFleetStatsDto>>> GetStats(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var stats = await _fuel.GetFleetStatsAsync(scope, from, to);
        return Ok(ApiResponse<FuelFleetStatsDto>.Ok(stats));
    }

    [HttpGet("trend")]
    [RequirePermission("fuel.view")]
    public async Task<ActionResult<ApiResponse<List<FuelTrendPointDto>>>> GetTrend(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var trend = await _fuel.GetTrendAsync(scope, from, to);
        return Ok(ApiResponse<List<FuelTrendPointDto>>.Ok(trend));
    }

    [HttpGet("vehicles/{vehicleId:guid}/metrics")]
    [RequirePermission("fuel.view")]
    public async Task<ActionResult<ApiResponse<FuelVehicleMetricsDto>>> GetVehicleMetrics(Guid vehicleId)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var metrics = await _fuel.GetVehicleMetricsAsync(scope, vehicleId);
        if (metrics == null) return NotFound(ApiResponse<FuelVehicleMetricsDto>.Fail("NOT_FOUND", "Vehicle not found"));
        return Ok(ApiResponse<FuelVehicleMetricsDto>.Ok(metrics));
    }

    [HttpPost]
    [RequirePermission("fuel.create")]
    public async Task<ActionResult<ApiResponse<FuelRecordDto>>> Create([FromBody] CreateFuelRecordDto dto)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return Forbid();

        // Company users are scoped to their own company; SuperAdmin targets the
        // vehicle's company. Resolve the vehicle's owning company in scope and
        // write against it (the JWT tenant for SuperAdmin is the platform company).
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var vehicleCompany = await _db.Vehicles.AsNoTracking()
            .Where(v => v.Id == dto.VehicleId && !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)))
            .Select(v => (Guid?)v.CompanyId)
            .FirstOrDefaultAsync();
        if (vehicleCompany == null)
            return NotFound(ApiResponse<FuelRecordDto>.Fail("NOT_FOUND", "Vehicle not found"));

        var record = await _fuel.CreateManualAsync(vehicleCompany.Value, dto, _tenant.UserId);
        return CreatedAtAction(nameof(GetAll), ApiResponse<FuelRecordDto>.Ok(record));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("fuel.delete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return Forbid();
        var deleted = await _fuel.DeleteAsync(tenantId.Value, id);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Fuel record not found"));
        return Ok(ApiResponse.Ok(message: "Fuel record deleted"));
    }

    private async Task<bool> VehicleInScopeAsync(Guid vehicleId, IReadOnlyList<Guid>? scope)
    {
        return await _db.Vehicles.AsNoTracking()
            .AnyAsync(v => v.Id == vehicleId && !v.IsDeleted && (scope == null || scope.Contains(v.CompanyId)));
    }
}