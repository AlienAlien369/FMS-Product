using System.Text.Json;
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

/// <summary>
/// Centralized Audit Log — query-only by design. There is deliberately NO
/// create/update/delete/restore route on entries themselves: entries are
/// append-only, written exclusively by <see cref="AuditLogService"/> from the
/// instrumented action points. Even SuperAdmin gets a clean 404/405 for any
/// mutate verb on this surface.
///
/// Visibility:
///  - SuperAdmin: every entry, filterable by actor / action code / target
///    company / date range.
///  - Company Admin (audit.view): ONLY entries targeting their own company,
///    and never SuperAdmin scope-switch or cross-tenant metadata — that is
///    not their business to see.
/// </summary>
[Route("api/v1/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private const string CrossTenantSource = TargetCompanyResolver.CrossTenantSource;

    private readonly ApplicationDbContext _db;
    private readonly AuditLogService _audit;

    public AuditController(ApplicationDbContext db, AuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    [RequirePermission("audit.view")]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogEntryDto>>>> Query(
        [FromQuery] string? actionCode,
        [FromQuery] string? actor,
        [FromQuery] Guid? targetCompanyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] PagedRequest filter)
    {
        var tenantId = User.GetTenantId();
        var isSuperAdmin = User.IsSuperAdmin();

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        // Company Admin: hard-scoped to their own company, and the SuperAdmin's
        // cross-tenant machinery is invisible to them regardless of filters.
        if (!isSuperAdmin)
        {
            query = query.Where(a => a.TenantId == tenantId
                && (a.ActionCode == null || a.ActionCode != "scope.switched")
                && (a.Source == null || a.Source != CrossTenantSource));
        }

        if (!string.IsNullOrWhiteSpace(actionCode))
            query = query.Where(a => a.ActionCode == actionCode);
        if (!string.IsNullOrWhiteSpace(actor))
            query = query.Where(a => (a.UserName ?? "").ToLower().Contains(actor.ToLower()));
        if (targetCompanyId.HasValue)
            query = query.Where(a => a.TenantId == targetCompanyId.Value);
        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value.ToUniversalTime());
        if (to.HasValue)
            query = query.Where(a => a.CreatedAt <= to.Value.ToUniversalTime());

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var items = rows.Select(ToDto).ToList();
        return Ok(ApiResponse<PagedResult<AuditLogEntryDto>>.Ok(new PagedResult<AuditLogEntryDto>
        {
            Items = items,
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        }));
    }

    /// <summary>
    /// Records a SuperAdmin scope switch (the act of switching into a company's
    /// view). The switch itself is logged even though it is a read-only action —
    /// cross-tenant VIEWING is worth a lightweight entry. Never routed to a
    /// target company: it is a view preference, not an action against a company.
    /// </summary>
    [HttpPost("scope-switch")]
    [RequirePermission("audit.view")]
    public IActionResult ScopeSwitch([FromBody] ScopeSwitchDto dto)
    {
        if (!User.IsSuperAdmin())
            return Forbid();

        _audit.TryRecord(new AuditLogRecord
        {
            ActorUserId = User.GetUserId(),
            ActorRole = "SuperAdmin",
            ActorEmail = User.GetEmail(),
            Action = AuditAction.Other,
            ActionCode = "scope.switched",
            EntityType = EntityType.Company,
            EntityId = Guid.Empty,
            EntityName = dto.CompanyIds.Count switch { 1 => "single company", 0 => "all companies", _ => "multiple companies" },
            AfterState = JsonSerializer.Serialize(new { companyIds = dto.CompanyIds }),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Source = "SuperAdmin scope switch"
        });
        return Ok(ApiResponse.Ok(message: "Scope switch recorded"));
    }

    internal static AuditLogEntryDto ToDto(AuditLog a) => new()
    {
        Id = a.Id,
        ActorUserId = a.UserId == Guid.Empty ? null : a.UserId,
        ActorEmail = a.UserName,
        ActorRole = a.ActorRole,
        ActionCode = a.ActionCode ?? a.Action.ToString(),
        Action = a.Action.ToString().ToLowerInvariant(),
        TargetEntityType = a.EntityType.ToString().ToLowerInvariant(),
        TargetEntityId = a.EntityId == Guid.Empty ? null : a.EntityId,
        TargetEntityName = a.EntityName,
        TargetCompanyId = a.TenantId,
        BeforeState = TryParse(a.OldValues),
        AfterState = TryParse(a.NewValues),
        IpAddress = a.IpAddress,
        Source = a.Source,
        Timestamp = a.CreatedAt
    };

    private static JsonElement? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}