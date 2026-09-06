using System.Security.Claims;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/alert-types")]
public class AlertTypesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AlertTypesController(ApplicationDbContext db) => _db = db;

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);
    private bool IsSuperAdmin() => User.IsSuperAdmin();

    /// <summary>Platform catalog — Super Admin only.</summary>
    [HttpGet]
    [RequirePermission("module.view")]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] string? search)
    {
        var q = _db.AlertTypes.AsNoTracking().Where(a => !a.IsDeleted);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(a => a.Category == category);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => a.Name.Contains(search) || a.Code.Contains(search));
        var items = await q.OrderBy(a => a.DisplayOrder).Select(a => new AlertTypeDto
        {
            Id = a.Id, Code = a.Code, Name = a.Name, Description = a.Description,
            Category = a.Category, DefaultSeverity = a.DefaultSeverity,
            DisplayOrder = a.DisplayOrder, Status = (int)a.Status
        }).ToListAsync();
        return Ok(new ApiResponse<List<AlertTypeDto>> { Data = items });
    }

    [HttpPost]
    [RequirePermission("module.view")]
    public async Task<IActionResult> Create([FromBody] CreateAlertTypeDto dto)
    {
        if (await _db.AlertTypes.AnyAsync(a => a.Code == dto.Code && !a.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE", $"Alert type code '{dto.Code}' already exists."));
        var entity = new Domain.Entities.AlertType
        {
            Id = Guid.NewGuid(), Code = dto.Code, Name = dto.Name, Description = dto.Description,
            Category = dto.Category, DefaultSeverity = dto.DefaultSeverity,
            DisplayOrder = dto.DisplayOrder, Status = EntityStatus.Active
        };
        _db.AlertTypes.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<AlertTypeDto>.Ok(new AlertTypeDto
        {
            Id = entity.Id, Code = entity.Code, Name = entity.Name, Description = entity.Description,
            Category = entity.Category, DefaultSeverity = entity.DefaultSeverity,
            DisplayOrder = entity.DisplayOrder, Status = (int)entity.Status
        }));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAlertTypeDto dto)
    {
        var entity = await _db.AlertTypes.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (entity == null) return NotFound();
        if (dto.Name != null) entity.Name = dto.Name;
        if (dto.Description != null) entity.Description = dto.Description;
        if (dto.Category != null) entity.Category = dto.Category;
        if (dto.DefaultSeverity.HasValue) entity.DefaultSeverity = dto.DefaultSeverity.Value;
        if (dto.DisplayOrder.HasValue) entity.DisplayOrder = dto.DisplayOrder.Value;
        if (dto.Status.HasValue) entity.Status = (EntityStatus)dto.Status.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Alert type updated"));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entity = await _db.AlertTypes.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (entity == null) return NotFound();
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Alert type deleted"));
    }
}

[ApiController]
[Route("api/v1/companies/{companyId:guid}/alert-subscriptions")]
public class CompanyAlertSubscriptionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly TargetCompanyResolver _targetCompany;
    private readonly INotificationService _notificationService;
    public CompanyAlertSubscriptionsController(ApplicationDbContext db, TargetCompanyResolver targetCompany, INotificationService notificationService)
    {
        _db = db;
        _targetCompany = targetCompany;
        _notificationService = notificationService;
    }

    /// <summary>List a company's alert subscriptions — Super Admin.</summary>
    [HttpGet]
    [RequirePermission("module.view")]
    public async Task<IActionResult> List(Guid companyId)
    {
        var items = await _db.CompanyAlertSubscriptions
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsDeleted)
            .Select(s => new CompanyAlertSubscriptionDto
            {
                Id = s.Id, CompanyId = s.CompanyId,
                CompanyName = s.Company.Name,
                AlertTypeId = s.AlertTypeId,
                AlertTypeCode = s.AlertType.Code,
                AlertTypeName = s.AlertType.Name,
                AlertTypeCategory = s.AlertType.Category,
                Enabled = s.Enabled, Source = s.Source
            }).OrderBy(s => s.AlertTypeCategory).ThenBy(s => s.AlertTypeName)
            .ToListAsync();
        return Ok(new ApiResponse<List<CompanyAlertSubscriptionDto>> { Data = items });
    }

    /// <summary>Toggle a single alert type for a company — Super Admin override.</summary>
    [HttpPut("{alertTypeId:guid}")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> Toggle(Guid companyId, Guid alertTypeId, [FromBody] ToggleCompanyAlertDto dto)
    {
        var sub = await _db.CompanyAlertSubscriptions
            .Include(s => s.AlertType)
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.AlertTypeId == alertTypeId && !s.IsDeleted);
        if (sub == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Subscription not found."));
        var alertTypeName = sub.AlertType?.Name ?? alertTypeId.ToString();
        sub.Enabled = dto.Enabled;
        sub.Source = "admin_override";
        sub.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Notification: alert entitlements are a company config change the
        // company's admins must know about (what they'll stop/start receiving).
        await _notificationService.NotifyCompanyAdminsAsync(companyId, "company.config_changed",
            $"Alert entitlement {(dto.Enabled ? "enabled" : "disabled")}: {alertTypeName}",
            $"Super Admin {(dto.Enabled ? "enabled" : "disabled")} the '{alertTypeName}' alert for your company.",
            (int)Domain.Enums.AlertSeverity.Medium, "Company", companyId, "/settings");

        return Ok(ApiResponse.Ok(message: $"Alert type {(dto.Enabled ? "enabled" : "disabled")} for company"));
    }

    /// <summary>Bulk toggle all (or by category) alert types for a company.</summary>
    [HttpPut("bulk")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> BulkToggle(Guid companyId, [FromBody] BulkToggleCompanyAlertsDto dto)
    {
        var subs = await _db.CompanyAlertSubscriptions
            .Where(s => s.CompanyId == companyId && !s.IsDeleted)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(dto.Category))
        {
            var catAlertTypeIds = await _db.AlertTypes
                .Where(a => a.Category == dto.Category && !a.IsDeleted)
                .Select(a => a.Id).ToListAsync();
            subs = subs.Where(s => catAlertTypeIds.Contains(s.AlertTypeId)).ToList();
        }
        foreach (var sub in subs)
        {
            sub.Enabled = dto.Enabled;
            sub.Source = "admin_override";
            sub.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        // Notification: bulk entitlement change is a company config change the
        // company's admins must know about (same event as single toggles).
        if (subs.Count > 0)
        {
            var scope = string.IsNullOrWhiteSpace(dto.Category) ? "all alert types" : $"{dto.Category} alert types";
            await _notificationService.NotifyCompanyAdminsAsync(companyId, "company.config_changed",
                $"Alert entitlements {(dto.Enabled ? "enabled" : "disabled")}: {scope}",
                $"Super Admin {(dto.Enabled ? "enabled" : "disabled")} {subs.Count} {scope} for your company.",
                (int)Domain.Enums.AlertSeverity.Medium, "Company", companyId, "/settings");
        }
        return Ok(ApiResponse.Ok(message: $"{subs.Count} alert types {(dto.Enabled ? "enabled" : "disabled")}"));
    }
}

[ApiController]
[Route("api/v1/companies/{companyId:guid}/role-alert-visibility")]
public class RoleAlertVisibilityController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    public RoleAlertVisibilityController(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);

    /// <summary>List visibility for a role — Company Admin (own company only).</summary>
    [HttpGet("role/{roleId:guid}")]
    [RequirePermission("role.view")]
    public async Task<IActionResult> ListForRole(Guid companyId, Guid roleId)
    {
        var tenantId = GetTenantId();
        if (companyId != tenantId && !_tenant.IsSuperAdmin)
            return Forbid();

        // Get the company's enabled alert types
        var enabledAlertTypeIds = await _db.CompanyAlertSubscriptions
            .Where(s => s.CompanyId == companyId && s.Enabled && !s.IsDeleted)
            .Select(s => s.AlertTypeId).ToListAsync();

        // Get all active alert types (for the full catalog view)
        var allAlertTypes = await _db.AlertTypes
            .Where(a => !a.IsDeleted && a.Status == EntityStatus.Active)
            .OrderBy(a => a.DisplayOrder).ToListAsync();

        // Get existing visibility entries for this role
        var existingVis = await _db.RoleAlertVisibilities
            .Where(v => v.CompanyId == companyId && v.RoleId == roleId && !v.IsDeleted)
            .ToDictionaryAsync(v => v.AlertTypeId);

        var items = allAlertTypes.Select(a =>
        {
            var isEnabled = enabledAlertTypeIds.Contains(a.Id);
            var vis = existingVis.TryGetValue(a.Id, out var existing) ? existing : null;
            // Default: if no explicit entry and the type is enabled, visible = true
            var visible = vis != null ? vis.Visible : isEnabled;
            return new RoleAlertVisibilityDto
            {
                Id = vis?.Id ?? Guid.Empty,
                RoleId = roleId,
                RoleName = "", // filled below if needed
                AlertTypeId = a.Id,
                AlertTypeCode = a.Code,
                AlertTypeName = a.Name,
                AlertTypeCategory = a.Category,
                Visible = visible
            };
        }).ToList();

        return Ok(new ApiResponse<List<RoleAlertVisibilityDto>> { Data = items });
    }

    /// <summary>Toggle visibility for a single alert type for a role.</summary>
    [HttpPut("role/{roleId:guid}/alert/{alertTypeId:guid}")]
    [RequirePermission("role.view")]
    public async Task<IActionResult> Toggle(Guid companyId, Guid roleId, Guid alertTypeId,
        [FromBody] ToggleRoleAlertVisibilityDto dto)
    {
        var tenantId = GetTenantId();
        if (companyId != tenantId && !_tenant.IsSuperAdmin)
            return Forbid();

        // Enforce: cannot make visible an alert type the company isn't entitled to
        var isEntitled = await _db.CompanyAlertSubscriptions
            .AnyAsync(s => s.CompanyId == companyId && s.AlertTypeId == alertTypeId
                          && s.Enabled && !s.IsDeleted);
        if (!isEntitled && dto.Visible)
            return BadRequest(ApiResponse.Fail("NOT_ENTITLED",
                "Cannot enable visibility for an alert type the company is not entitled to."));

        var vis = await _db.RoleAlertVisibilities
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.RoleId == roleId
                                     && v.AlertTypeId == alertTypeId && !v.IsDeleted);
        if (vis == null)
        {
            vis = new Domain.Entities.RoleAlertVisibility
            {
                Id = Guid.NewGuid(), CompanyId = companyId, RoleId = roleId,
                AlertTypeId = alertTypeId, Visible = dto.Visible
            };
            _db.RoleAlertVisibilities.Add(vis);
        }
        else
        {
            vis.Visible = dto.Visible;
            vis.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: $"Visibility {(dto.Visible ? "enabled" : "disabled")}"));
    }

    /// <summary>Bulk toggle visibility for all (or by category) alert types for a role.</summary>
    [HttpPut("role/{roleId:guid}/bulk")]
    [RequirePermission("role.view")]
    public async Task<IActionResult> BulkToggle(Guid companyId, Guid roleId,
        [FromBody] BulkToggleRoleAlertsDto dto)
    {
        var tenantId = GetTenantId();
        if (companyId != tenantId && !_tenant.IsSuperAdmin)
            return Forbid();

        // Get enabled alert types for this company
        var enabledAlertTypeIds = await _db.CompanyAlertSubscriptions
            .Where(s => s.CompanyId == companyId && s.Enabled && !s.IsDeleted)
            .Select(s => s.AlertTypeId).ToListAsync();

        var query = _db.AlertTypes.Where(a => !a.IsDeleted && a.Status == EntityStatus.Active);
        if (!string.IsNullOrWhiteSpace(dto.Category))
            query = query.Where(a => a.Category == dto.Category);
        var alertTypeIds = await query.Select(a => a.Id).ToListAsync();

        foreach (var atId in alertTypeIds)
        {
            // Skip alert types the company isn't entitled to
            if (!enabledAlertTypeIds.Contains(atId)) continue;

            var vis = await _db.RoleAlertVisibilities
                .FirstOrDefaultAsync(v => v.CompanyId == companyId && v.RoleId == roleId
                                         && v.AlertTypeId == atId && !v.IsDeleted);
            if (vis == null)
            {
                _db.RoleAlertVisibilities.Add(new Domain.Entities.RoleAlertVisibility
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, RoleId = roleId,
                    AlertTypeId = atId, Visible = dto.Visible
                });
            }
            else
            {
                vis.Visible = dto.Visible;
                vis.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: $"Visibility updated for {alertTypeIds.Count} alert types"));
    }
}
