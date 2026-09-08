using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportEngine _engine;
    private readonly ScheduledReportService _schedules;
    private readonly IPermissionService _permissions;
    private readonly ITenantContext _tenant;

    public ReportsController(IReportEngine engine, ScheduledReportService schedules,
        IPermissionService permissions, ITenantContext tenant)
    {
        _engine = engine;
        _schedules = schedules;
        _permissions = permissions;
        _tenant = tenant;
    }

    /// <summary>
    /// Report categories the caller may see. Category visibility is gated on the
    /// underlying page's view permission (fuel.view etc.), so a company whose
    /// package excludes a dataset never even sees its report — not an empty one.
    /// </summary>
    [HttpGet("catalog")]
    [RequirePermission("report.view")]
    public async Task<ActionResult<ApiResponse<List<ReportCatalogItemDto>>>> GetCatalog()
    {
        var perms = await GetEffectivePermissionsAsync();
        var catalog = await _engine.GetCatalogAsync(perms, User.IsSuperAdmin());
        return Ok(ApiResponse<List<ReportCatalogItemDto>>.Ok(catalog));
    }

    /// <summary>Run any report the caller can view, within their company scope.</summary>
    [HttpPost("run")]
    [RequirePermission("report.view")]
    public async Task<ActionResult<ApiResponse<ReportResultDto>>> Run([FromBody] ReportRequestDto request)
    {
        var def = _engine.Find(request.ReportType);
        if (def == null)
            return NotFound(ApiResponse<ReportResultDto>.Fail("NOT_FOUND", $"Unknown report type '{request.ReportType}'"));
        if (!await HasAllAsync(def.RequiresView))
            return Forbid();

        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var result = await _engine.RunAsync(def.Code, request, scope);
        return Ok(ApiResponse<ReportResultDto>.Ok(result));
    }

    /// <summary>
    /// Export a report as CSV or PDF. Export respects BOTH the reports.export
    /// permission and the underlying page's export permission (fuel.export,
    /// driver.export, …) — a user who can view fuel data but not export it
    /// cannot export the fuel report.
    /// </summary>
    [HttpGet("export")]
    [RequirePermission("report.export")]
    public async Task<IActionResult> Export(
        [FromQuery] string type, [FromQuery] string format = "csv",
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? vehicleIds = null, [FromQuery] string? driverIds = null)
    {
        var def = _engine.Find(type);
        if (def == null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Unknown report type '{type}'"));
        if (!await HasAllAsync(def.RequiresExport))
            return Forbid();

        var request = new ReportRequestDto
        {
            ReportType = def.Code,
            From = from,
            To = to,
            VehicleIds = ParseIds(vehicleIds),
            DriverIds = ParseIds(driverIds)
        };
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var result = await _engine.RunAsync(def.Code, request, scope);

        var isPdf = format.Equals("pdf", StringComparison.OrdinalIgnoreCase);
        var bytes = isPdf ? _engine.ToPdf(result) : _engine.ToCsv(result);
        var ext = isPdf ? "pdf" : "csv";
        var fileName = $"{def.Code}-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.{ext}";
        return File(bytes, isPdf ? "application/pdf" : "text/csv", fileName);
    }

    // ── Scheduled reports ───────────────────────────────────────────────────

    [HttpPost("schedules")]
    [RequirePermission("report.create")]
    public async Task<ActionResult<ApiResponse<ScheduledReportDto>>> CreateSchedule([FromBody] CreateScheduledReportDto dto)
    {
        var tenantId = _tenant.TenantId;
        var userId = User.GetUserId();
        if (tenantId == null) return Forbid();
        try
        {
            var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
            var schedule = await _schedules.CreateAsync(tenantId.Value, userId, dto, scope);
            return CreatedAtAction(nameof(GetMySchedules), ApiResponse<ScheduledReportDto>.Ok(schedule));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ScheduledReportDto>.Fail("INVALID_SCHEDULE", ex.Message));
        }
    }

    [HttpGet("schedules")]
    [RequirePermission("report.create")]
    public async Task<ActionResult<ApiResponse<List<ScheduledReportDto>>>> GetMySchedules()
    {
        var tenantId = _tenant.TenantId;
        var userId = User.GetUserId();
        if (tenantId == null) return Forbid();
        var items = await _schedules.GetMineAsync(tenantId.Value, userId);
        return Ok(ApiResponse<List<ScheduledReportDto>>.Ok(items));
    }

    [HttpPut("schedules/{id:guid}")]
    [RequirePermission("report.create")]
    public async Task<ActionResult<ApiResponse<ScheduledReportDto>>> UpdateSchedule(Guid id, [FromBody] UpdateScheduledReportDto dto)
    {
        var tenantId = _tenant.TenantId;
        var userId = User.GetUserId();
        if (tenantId == null) return Forbid();
        try
        {
            var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
            var updated = await _schedules.UpdateAsync(tenantId.Value, userId, id, dto, scope);
            if (updated == null) return NotFound(ApiResponse<ScheduledReportDto>.Fail("NOT_FOUND", "Scheduled report not found"));
            return Ok(ApiResponse<ScheduledReportDto>.Ok(updated));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ScheduledReportDto>.Fail("INVALID_SCHEDULE", ex.Message));
        }
    }

    [HttpDelete("schedules/{id:guid}")]
    [RequirePermission("report.create")]
    public async Task<ActionResult<ApiResponse>> DeleteSchedule(Guid id)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return Forbid();
        var deleted = await _schedules.DeleteAsync(tenantId.Value, id);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Scheduled report not found"));
        return Ok(ApiResponse.Ok(message: "Scheduled report deleted"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<HashSet<string>> GetEffectivePermissionsAsync()
    {
        var userId = User.GetUserId();
        var tenantId = User.GetTenantId();
        return await _permissions.GetEffectivePermissionsAsync(userId, tenantId);
    }

    /// <summary>SuperAdmin bypasses role-grant gates; everyone else must hold every code.</summary>
    private async Task<bool> HasAllAsync(IEnumerable<string> codes)
    {
        if (User.IsSuperAdmin()) return true;
        var perms = await GetEffectivePermissionsAsync();
        return codes.All(perms.Contains);
    }

    private static List<Guid> ParseIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<Guid>();
        var ids = new List<Guid>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id)) ids.Add(id);
        }
        return ids;
    }
}