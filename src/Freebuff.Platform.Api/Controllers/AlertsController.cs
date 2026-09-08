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

/// <summary>
/// The Alerts page surface (the Api.Monitoring project has a legacy alerts
/// endpoint over its own MonitoringDbContext; this controller serves the live
/// Alerts page from the main ApplicationDbContext so the Alerts report, alert
/// volume stats and the page all read the same scoped stream).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public AlertsController(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet]
    [RequirePermission("alert.view")]
    public async Task<ActionResult<ApiResponse<PagedResult<AlertListItemDto>>>> GetAll(
        [FromQuery] PagedRequest filter,
        [FromQuery] string? alertType = null,
        [FromQuery] int? severity = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? vehicleId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Alerts.AsNoTracking()
            .Where(a => !a.IsDeleted && (scope == null || scope.Contains(a.CompanyId)));

        if (!string.IsNullOrWhiteSpace(alertType)) query = query.Where(a => a.AlertType == alertType);
        if (severity.HasValue) query = query.Where(a => (int)a.Severity == severity.Value);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EntityStatus>(status, true, out var statusEnum))
            query = query.Where(a => a.Status == statusEnum);
        if (vehicleId.HasValue) query = query.Where(a => a.VehicleId == vehicleId.Value);
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(a => a.Title.Contains(filter.Search) || a.AlertType.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((Math.Max(1, filter.Page) - 1) * Math.Clamp(filter.PageSize, 1, 200))
            .Take(Math.Clamp(filter.PageSize, 1, 200))
            .Select(a => new AlertListItemDto
            {
                Id = a.Id,
                AlertType = a.AlertType,
                Title = a.Title,
                Message = a.Message,
                Severity = (int)a.Severity,
                Status = a.Status.ToString(),
                VehicleId = a.VehicleId,
                VehicleRegistration = a.Vehicle != null ? a.Vehicle.RegistrationNumber : null,
                DriverId = a.DriverId,
                DriverName = a.Driver != null ? a.Driver.FirstName + " " + a.Driver.LastName : null,
                CreatedAt = a.CreatedAt,
                AcknowledgedAt = a.AcknowledgedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<AlertListItemDto>>.Ok(new PagedResult<AlertListItemDto>
        {
            Items = items, TotalCount = totalCount, Page = Math.Max(1, filter.Page), PageSize = Math.Clamp(filter.PageSize, 1, 200)
        }));
    }

    [HttpGet("stats")]
    [RequirePermission("alert.view")]
    public async Task<ActionResult<ApiResponse<AlertStatsDto>>> GetStats(
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var query = _db.Alerts.AsNoTracking()
            .Where(a => !a.IsDeleted && (scope == null || scope.Contains(a.CompanyId)));
        if (from.HasValue) query = query.Where(a => a.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.CreatedAt <= to.Value);

        var rows = await query
            .Select(a => new { a.AlertType, a.Severity, a.Status })
            .ToListAsync();

        var stats = new AlertStatsDto
        {
            TotalCount = rows.Count,
            OpenCount = rows.Count(a => a.Status == EntityStatus.Active),
            AcknowledgedCount = rows.Count(a => a.Status != EntityStatus.Active),
            ByType = rows.GroupBy(a => a.AlertType).ToDictionary(g => g.Key, g => g.Count()),
            BySeverity = rows.GroupBy(a => a.Severity.ToString()).ToDictionary(g => g.Key, g => g.Count())
        };
        return Ok(ApiResponse<AlertStatsDto>.Ok(stats));
    }

    [HttpPost("{id:guid}/acknowledge")]
    [RequirePermission("alert.update")]
    public async Task<ActionResult<ApiResponse>> Acknowledge(Guid id)
    {
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var alert = await _db.Alerts
            .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted
                && a.Status == EntityStatus.Active
                && (scope == null || scope.Contains(a.CompanyId)));
        if (alert == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Alert not found"));
        alert.Status = EntityStatus.Inactive;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = _tenant.UserId?.ToString();
        alert.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Alert acknowledged"));
    }
}