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
// Mounted at /api/v1/drivers alongside DriversController — all scorecard routes
// are distinct literals / guid-constrained, so there is no collision.
[Route("api/v1/drivers")]
[Authorize]
public class DriverScorecardController : ControllerBase
{
    private readonly DriverScorecardService _scorecards;
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DriverScorecardController(DriverScorecardService scorecards, ApplicationDbContext db, ITenantContext tenant)
    {
        _scorecards = scorecards;
        _db = db;
        _tenant = tenant;
    }

    /// <summary>Full scorecard for one driver (scores + trend + explainability feed) for a window.</summary>
    [HttpGet("{driverId:guid}/scorecard")]
    [RequirePermission("driverscore.view")]
    public async Task<ActionResult<ApiResponse<DriverScorecardDto>>> GetScorecard(Guid driverId, [FromQuery] string window = "30d")
    {
        window = NormalizeWindow(window);
        var companyId = await _db.Drivers.AsNoTracking()
            .Where(d => d.Id == driverId && !d.IsDeleted).Select(d => d.CompanyId).FirstOrDefaultAsync();
        if (companyId == Guid.Empty) return NotFound();
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        if (scope != null && !scope.Contains(companyId)) return NotFound();

        var dto = await _scorecards.GetScorecardAsync(driverId, window);
        return Ok(ApiResponse<DriverScorecardDto>.Ok(dto));
    }

    /// <summary>Fleet ranking for a window (drivers with insufficient data sorted last).</summary>
    [HttpGet("scorecards")]
    [RequirePermission("driverscore.view")]
    public async Task<ActionResult<ApiResponse<List<DriverScoreRankingDto>>>> GetRanking(
        [FromQuery] string window = "30d", [FromQuery] decimal? minScore = null, [FromQuery] int limit = 100)
    {
        window = NormalizeWindow(window);
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        var ranking = await _scorecards.GetRankingAsync(window, scope, minScore, Math.Clamp(limit, 1, 500));
        return Ok(ApiResponse<List<DriverScoreRankingDto>>.Ok(ranking));
    }

    /// <summary>Effective weights: caller's own company override, else the platform default.</summary>
    [HttpGet("scorecard-config")]
    [RequirePermission("driverscore.view")]
    public async Task<ActionResult<ApiResponse<ScoreWeightConfigDto>>> GetConfig()
    {
        // SuperAdmin (no own company) sees the platform default; a company admin
        // sees their own company's override-or-default.
        var companyId = _tenant.IsSuperAdmin ? null : _tenant.TenantId;
        var dto = await _scorecards.GetWeightsAsync(companyId);
        return Ok(ApiResponse<ScoreWeightConfigDto>.Ok(dto));
    }

    /// <summary>
    /// Set composite weights: SuperAdmin sets the platform default (no companyId)
    /// or any company's override; a company admin may only override their own
    /// company. Existing materialized periods are NOT rewritten — see recompute.
    /// </summary>
    [HttpPut("scorecard-config")]
    [RequirePermission("driverscore.configure")]
    public async Task<ActionResult<ApiResponse<ScoreWeightConfigDto>>> SetConfig(ScoreWeightConfigUpdateDto dto)
    {
        if (!_tenant.IsSuperAdmin)
        {
            // Company admin: own company only, never the platform default.
            if (dto.CompanyId != _tenant.TenantId || _tenant.TenantId == null) return Forbid();
        }

        var result = await _scorecards.SetWeightsAsync(dto.CompanyId, dto.SafetyWeight, dto.ComplianceWeight,
            dto.PunctualityWeight, dto.BehaviorWeight, _tenant.UserEmail);
        return Ok(ApiResponse<ScoreWeightConfigDto>.Ok(result));
    }

    /// <summary>
    /// Explicit recompute: reapplies the CURRENT weights to all windows for the
    /// driver, updating today's anchor. Historical anchors are left untouched.
    /// </summary>
    [HttpPost("{driverId:guid}/scorecard/recompute")]
    [RequirePermission("driverscore.configure")]
    public async Task<ActionResult<ApiResponse<DriverScorecardDto>>> Recompute(Guid driverId)
    {
        var companyId = await _db.Drivers.AsNoTracking()
            .Where(d => d.Id == driverId && !d.IsDeleted).Select(d => d.CompanyId).FirstOrDefaultAsync();
        if (companyId == Guid.Empty) return NotFound();
        var scope = CompanyScopePolicy.EffectiveIds(_tenant.Scope);
        if (scope != null && !scope.Contains(companyId)) return NotFound();

        var period = await _scorecards.RecomputeAsync(driverId);
        var dto = await _scorecards.GetScorecardAsync(driverId, "30d");
        dto.Weights = await _scorecards.GetWeightsAsync(companyId);
        return Ok(ApiResponse<DriverScorecardDto>.Ok(dto));
    }

    private static string NormalizeWindow(string window)
        => window is "90d" or "all" ? window : "30d";
}