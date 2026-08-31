using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Api.Monitoring.Data;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Monitoring.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly MonitoringDbContext _db;
    public AlertsController(MonitoringDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<Alert>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var query = _db.Alerts.AsNoTracking().Where(a => !a.IsDeleted).AsQueryable();
        if (Guid.TryParse(tenantId, out var tid)) query = query.Where(a => a.CompanyId == tid);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(a => a.Title.Contains(filter.Search) || a.AlertType.Contains(filter.Search));

        var totalCount = await query.CountAsync();
        var items = await query.OrderByDescending(a => a.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync();

        return Ok(ApiResponse<PagedResult<Alert>>.Ok(new PagedResult<Alert>
        {
            Items = items, TotalCount = totalCount, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<Alert>>> GetById(Guid id)
    {
        var alert = await _db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (alert == null) return NotFound(ApiResponse<Alert>.Fail("NOT_FOUND", "Alert not found"));
        return Ok(ApiResponse<Alert>.Ok(alert));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Alert>>> Create([FromBody] Alert alert)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value ?? throw new UnauthorizedAccessException("No tenant");
        alert.Id = Guid.NewGuid();
        alert.CompanyId = Guid.Parse(tenantId);
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = alert.Id }, ApiResponse<Alert>.Ok(alert));
    }
}
