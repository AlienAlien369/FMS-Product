using System.Security.Claims;
using Freebuff.Platform.Api.Authorization;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// Personal notifications: bell data (unread count + recent), full history,
/// mark-read, and per-user mute preferences. Notifications are addressed to a
/// user; Super Admin additionally sees them scoped by the Company Scope
/// Selector (a notification never crosses company boundaries otherwise).
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    public NotificationsController(ApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid GetUserId() => User.GetUserId();

    /// <summary>My notifications, paginated. Filters: read/unread, eventType, severity.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> List(
        [FromQuery] PagedRequest filter, [FromQuery] bool? unread, [FromQuery] string? eventType, [FromQuery] int? severity)
    {
        var userId = GetUserId();
        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted && n.Status == EntityStatus.Active);

        // Super Admin: honor the company scope selector (ALL = unconstrained).
        if (_tenant.IsSuperAdmin && _tenant.Scope != null && _tenant.Scope.EffectiveCompanyIds != null)
        {
            var scopeIds = _tenant.Scope.EffectiveCompanyIds;
            query = query.Where(n => scopeIds.Contains(n.CompanyId));
        }

        if (unread.HasValue) query = query.Where(n => n.IsRead == !unread.Value);
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(n => n.EventType == eventType);
        if (severity.HasValue) query = query.Where(n => n.Severity == severity.Value);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(n => n.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id, Title = n.Title, Message = n.Message, EventType = n.EventType,
                ActionUrl = n.ActionUrl, Severity = n.Severity,
                RelatedEntityType = n.RelatedEntityType, RelatedEntityId = n.RelatedEntityId,
                IsRead = n.IsRead, CreatedAt = n.CreatedAt
            }).ToListAsync();

        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(new PagedResult<NotificationDto>
        {
            Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize
        }));
    }

    /// <summary>Unread badge count for the bell icon.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<UnreadCountDto>>> UnreadCount()
    {
        var userId = GetUserId();
        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted && n.Status == EntityStatus.Active && !n.IsRead);
        if (_tenant.IsSuperAdmin && _tenant.Scope != null && _tenant.Scope.EffectiveCompanyIds != null)
        {
            var scopeIds = _tenant.Scope.EffectiveCompanyIds;
            query = query.Where(n => scopeIds.Contains(n.CompanyId));
        }
        return Ok(ApiResponse<UnreadCountDto>.Ok(new UnreadCountDto { Count = await query.CountAsync() }));
    }

    /// <summary>Most recent N for the bell dropdown (default 10).</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> Recent([FromQuery] int limit = 10)
    {
        var userId = GetUserId();
        var query = _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsDeleted && n.Status == EntityStatus.Active);
        if (_tenant.IsSuperAdmin && _tenant.Scope != null && _tenant.Scope.EffectiveCompanyIds != null)
        {
            var scopeIds = _tenant.Scope.EffectiveCompanyIds;
            query = query.Where(n => scopeIds.Contains(n.CompanyId));
        }
        var items = await query.OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(n => new NotificationDto
            {
                Id = n.Id, Title = n.Title, Message = n.Message, EventType = n.EventType,
                ActionUrl = n.ActionUrl, Severity = n.Severity,
                RelatedEntityType = n.RelatedEntityType, RelatedEntityId = n.RelatedEntityId,
                IsRead = n.IsRead, CreatedAt = n.CreatedAt
            }).ToListAsync();
        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(new PagedResult<NotificationDto>
        {
            Items = items, TotalCount = items.Count, Page = 1, PageSize = items.Count
        }));
    }

    /// <summary>Mark one notification read (own only) and return the deep-link target.</summary>
    [HttpPut("{id:guid}/read")]
    public async Task<ActionResult<ApiResponse<MarkReadDto>>> MarkRead(Guid id)
    {
        var userId = GetUserId();
        var n = await _db.Notifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && !x.IsDeleted);
        if (n == null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Notification not found"));
        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        n.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<MarkReadDto>.Ok(new MarkReadDto { Success = true }));
    }

    /// <summary>Mark all my notifications read (respecting the Super Admin scope filter).</summary>
    [HttpPut("read-all")]
    public async Task<ActionResult<ApiResponse>> MarkAllRead()
    {
        var userId = GetUserId();
        var query = _db.Notifications
            .Where(n => n.UserId == userId && !n.IsDeleted && !n.IsRead);
        if (_tenant.IsSuperAdmin && _tenant.Scope != null && _tenant.Scope.EffectiveCompanyIds != null)
        {
            var scopeIds = _tenant.Scope.EffectiveCompanyIds;
            query = query.Where(n => scopeIds.Contains(n.CompanyId));
        }
        var unread = await query.ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
            n.UpdatedAt = now;
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: $"{unread.Count} notifications marked read"));
    }

    /// <summary>My preference toggles across the full event-type catalog.</summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<ApiResponse<List<NotificationPreferenceDto>>>> GetPreferences()
    {
        var userId = GetUserId();
        var prefs = await _db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToDictionaryAsync(p => p.EventType, p => p.Enabled);
        var catalog = await _db.NotificationEventTypes.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status == EntityStatus.Active)
            .OrderBy(e => e.DisplayOrder).ToListAsync();
        var items = catalog.Select(e => new NotificationPreferenceDto
        {
            EventType = e.Code,
            EventTypeName = e.Name,
            Category = e.Category,
            Enabled = prefs.TryGetValue(e.Code, out var en) ? en : true
        }).ToList();
        return Ok(ApiResponse<List<NotificationPreferenceDto>>.Ok(items));
    }

    /// <summary>Bulk update my per-user mute preferences.</summary>
    [HttpPut("preferences")]
    public async Task<ActionResult<ApiResponse>> UpdatePreferences([FromBody] List<UpdateNotificationPreferenceDto> dtos)
    {
        var userId = GetUserId();
        var companyId = User.GetTenantId();
        var validCodes = (await _db.NotificationEventTypes.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Status == EntityStatus.Active)
            .Select(e => e.Code).ToListAsync()).ToHashSet();

        var existing = await _db.NotificationPreferences
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToDictionaryAsync(p => p.EventType);
        foreach (var dto in dtos)
        {
            if (!validCodes.Contains(dto.EventType)) continue; // unknown/retired event — ignore
            if (existing.TryGetValue(dto.EventType, out var row))
            {
                row.Enabled = dto.Enabled;
                row.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.NotificationPreferences.Add(new Domain.Entities.NotificationPreference
                {
                    Id = Guid.NewGuid(), UserId = userId, CompanyId = companyId,
                    EventType = dto.EventType, Enabled = dto.Enabled,
                    TenantId = companyId
                });
            }
        }
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Preferences updated"));
    }

    // ── Event-type registry (Super Admin manages; everyone reads) ─────────

    [HttpGet("event-types")]
    public async Task<ActionResult<ApiResponse<List<NotificationEventTypeDto>>>> ListEventTypes(
        [FromQuery] string? category)
    {
        var q = _db.NotificationEventTypes.AsNoTracking().Where(e => !e.IsDeleted);
        if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category);
        var items = await q.OrderBy(e => e.DisplayOrder).Select(e => new NotificationEventTypeDto
        {
            Id = e.Id, Code = e.Code, Name = e.Name, Description = e.Description,
            Category = e.Category, DefaultSeverity = e.DefaultSeverity,
            DisplayOrder = e.DisplayOrder, Status = (int)e.Status
        }).ToListAsync();
        return Ok(ApiResponse<List<NotificationEventTypeDto>>.Ok(items));
    }

    [HttpPost("event-types")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> CreateEventType([FromBody] CreateNotificationEventTypeDto dto)
    {
        if (await _db.NotificationEventTypes.AnyAsync(e => e.Code == dto.Code && !e.IsDeleted))
            return BadRequest(ApiResponse.Fail("DUPLICATE", $"Event type code '{dto.Code}' already exists."));
        var entity = new Domain.Entities.NotificationEventType
        {
            Id = Guid.NewGuid(), Code = dto.Code, Name = dto.Name, Description = dto.Description,
            Category = dto.Category, DefaultSeverity = dto.DefaultSeverity,
            DisplayOrder = dto.DisplayOrder, Status = EntityStatus.Active
        };
        _db.NotificationEventTypes.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<NotificationEventTypeDto>.Ok(new NotificationEventTypeDto
        {
            Id = entity.Id, Code = entity.Code, Name = entity.Name, Description = entity.Description,
            Category = entity.Category, DefaultSeverity = entity.DefaultSeverity,
            DisplayOrder = entity.DisplayOrder, Status = (int)entity.Status
        }));
    }

    [HttpPut("event-types/{id:guid}")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> UpdateEventType(Guid id, [FromBody] UpdateNotificationEventTypeDto dto)
    {
        var entity = await _db.NotificationEventTypes.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (entity == null) return NotFound();
        if (dto.Name != null) entity.Name = dto.Name;
        if (dto.Description != null) entity.Description = dto.Description;
        if (dto.Category != null) entity.Category = dto.Category;
        if (dto.DefaultSeverity.HasValue) entity.DefaultSeverity = dto.DefaultSeverity.Value;
        if (dto.DisplayOrder.HasValue) entity.DisplayOrder = dto.DisplayOrder.Value;
        if (dto.Status.HasValue) entity.Status = (EntityStatus)dto.Status.Value;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Event type updated"));
    }

    [HttpDelete("event-types/{id:guid}")]
    [RequirePermission("module.view")]
    public async Task<IActionResult> DeleteEventType(Guid id)
    {
        var entity = await _db.NotificationEventTypes.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (entity == null) return NotFound();
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ApiResponse.Ok(message: "Event type deleted"));
    }
}