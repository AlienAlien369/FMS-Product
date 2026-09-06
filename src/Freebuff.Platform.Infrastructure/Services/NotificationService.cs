using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Dispatches in-app notifications from every event source (alert pipeline,
/// RBAC/permission changes, company config changes, administrative events).
/// Target resolution happens here once: direct user, all users of a role, all
/// Company Admins of a company, or users whose effective permissions actually
/// changed. Per-user NotificationPreference rows (personal mute) are honored
/// here — an absence of a row means the user wants the event surfaced.
/// </summary>
/// <summary>
/// Push channel for instant delivery of new notifications to connected clients
/// (SignalR). The service layer depends only on this contract so it stays
/// testable without a hub; the API layer supplies the SignalR implementation.
/// Absence of an implementation (unit tests, minimal hosts) is a silent no-op.
/// </summary>
public interface INotificationRealtimeChannel
{
    Task PushToUserAsync(Guid userId, string eventType, string title, string message, int severity);
}

public interface INotificationService
{
    /// <summary>Send to one user, honoring their personal preference mute.</summary>
    Task NotifyUserAsync(Guid companyId, Guid userId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null);

    /// <summary>Send to every user holding the given role in the company.</summary>
    Task<int> NotifyRoleUsersAsync(Guid companyId, Guid roleId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null);

    /// <summary>Send to every user with the system "Company Admin" role in the company.</summary>
    Task<int> NotifyCompanyAdminsAsync(Guid companyId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null);

    /// <summary>
    /// Send to users of the role whose own EFFECTIVE permission set actually
    /// changed (all their roles' grants ∩ company package modules, before vs
    /// after the edit). The diff is per-user total-effective, NOT just the edited
    /// role's: a user whose other role still grants a permission that this edit
    /// removed from one role is not notified — nothing they can do changed. A
    /// role edit that flips one disallowed permission to another disallowed one
    /// also notifies nobody. Returns count delivered.
    /// </summary>
    Task<int> NotifyUsersWhoseEffectivePermissionsChangedAsync(Guid companyId, Guid roleId,
        IReadOnlySet<Guid> oldPermissionIds, IReadOnlySet<Guid> newPermissionIds,
        string title, string message, int severity, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? actionUrl = null);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissionService;
    private readonly INotificationRealtimeChannel? _realtime;

    public NotificationService(ApplicationDbContext db, IPermissionService permissionService,
        INotificationRealtimeChannel? realtime = null)
    {
        _db = db;
        _permissionService = permissionService;
        _realtime = realtime;
    }

    public async Task NotifyUserAsync(Guid companyId, Guid userId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        if (!await IsEnabledForUserAsync(userId, eventType)) return;
        await PersistAsync(companyId, userId, eventType, title, message, severity, relatedEntityType, relatedEntityId, actionUrl);
    }

    public async Task<int> NotifyRoleUsersAsync(Guid companyId, Guid roleId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        var userIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == roleId && ur.User.CompanyId == companyId && !ur.User.IsDeleted && ur.User.Status == EntityStatus.Active)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();
        return await DispatchAsync(userIds, companyId, eventType, title, message, severity, relatedEntityType, relatedEntityId, actionUrl);
    }

    public async Task<int> NotifyCompanyAdminsAsync(Guid companyId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        var userIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.Role.CompanyId == companyId && ur.Role.Name == "Company Admin"
                && ur.Role.IsSystemRole && !ur.Role.IsDeleted
                && !ur.User.IsDeleted && ur.User.Status == EntityStatus.Active)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();
        return await DispatchAsync(userIds, companyId, eventType, title, message, severity, relatedEntityType, relatedEntityId, actionUrl);
    }

    public async Task<int> NotifyUsersWhoseEffectivePermissionsChangedAsync(Guid companyId, Guid roleId,
        IReadOnlySet<Guid> oldPermissionIds, IReadOnlySet<Guid> newPermissionIds,
        string title, string message, int severity, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? actionUrl = null)
    {
        // Effective set formula (mirrors PermissionService): grants ∩ company
        // package modules. A permission outside the company's allowed set never
        // becomes effective, so toggling it must not notify anyone.
        var allowedCodes = await _permissionService.GetCompanyAllowedPermissionsAsync(companyId);
        var allowedIds = (await _db.Permissions.AsNoTracking()
            .Where(p => allowedCodes.Contains(p.Code) && !p.IsDeleted)
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        // Users of the edited role in this company.
        var userIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == roleId && ur.User.CompanyId == companyId && !ur.User.IsDeleted && ur.User.Status == EntityStatus.Active)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();
        if (userIds.Count == 0) return 0;

        // Permission grants the user holds via their OTHER roles (the edited
        // role's old/new sets are supplied by the caller and handled per user).
        var otherGrantRows = await _db.UserRoles.AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId) && !ur.IsDeleted && ur.RoleId != roleId)
            .SelectMany(ur => ur.Role.RolePermissions.Where(rp => !rp.IsDeleted),
                (ur, rp) => new { ur.UserId, rp.PermissionId })
            .ToListAsync();
        var otherGrantsByUser = otherGrantRows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PermissionId).ToHashSet());

        var recipients = new List<Guid>();
        foreach (var uid in userIds)
        {
            var other = otherGrantsByUser.TryGetValue(uid, out var s) ? s : new HashSet<Guid>();
            var oldEff = new HashSet<Guid>(other);
            oldEff.UnionWith(oldPermissionIds);
            oldEff.IntersectWith(allowedIds);
            var newEff = new HashSet<Guid>(other);
            newEff.UnionWith(newPermissionIds);
            newEff.IntersectWith(allowedIds);
            if (!oldEff.SetEquals(newEff)) recipients.Add(uid);
        }
        if (recipients.Count == 0) return 0;

        return await DispatchAsync(recipients, companyId, eventType: "permission.role_updated", title, message,
            severity, relatedEntityType, relatedEntityId, actionUrl);
    }

    // ── Internals ─────────────────────────────────────────────────────────

    private async Task<bool> IsEnabledForUserAsync(Guid userId, string eventType)
    {
        var pref = await _db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.EventType == eventType && !p.IsDeleted);
        return pref == null || pref.Enabled;
    }

    private async Task<int> DispatchAsync(IReadOnlyList<Guid> userIds, Guid companyId, string eventType,
        string title, string message, int severity, string? relatedEntityType, Guid? relatedEntityId, string? actionUrl)
    {
        if (userIds.Count == 0) return 0;

        // Apply per-user mute filters in one query, then insert the rest.
        var prefs = await _db.NotificationPreferences.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.EventType == eventType && !p.IsDeleted)
            .Select(p => new { p.UserId, p.Enabled })
            .ToListAsync();
        var muted = prefs.Where(p => !p.Enabled).Select(p => p.UserId).ToHashSet();
        var recipients = userIds.Where(u => !muted.Contains(u)).ToList();
        if (recipients.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var uid in recipients)
        {
            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                TenantId = companyId,
                UserId = uid,
                EventType = eventType,
                Type = eventType,
                Title = title,
                Message = message,
                Severity = severity,
                RelatedEntityType = relatedEntityType,
                RelatedEntityId = relatedEntityId,
                ActionUrl = actionUrl,
                DeliveryChannel = "in_app",
                Status = EntityStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        await _db.SaveChangesAsync();

        // Push after the commit so a failed save never emits a phantom event.
        if (_realtime != null)
        {
            foreach (var uid in recipients)
            {
                await _realtime.PushToUserAsync(uid, eventType, title, message, severity);
            }
        }
        return recipients.Count;
    }

    private async Task PersistAsync(Guid companyId, Guid userId, string eventType, string title, string message,
        int severity, string? relatedEntityType, Guid? relatedEntityId, string? actionUrl)
    {
        _db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            TenantId = companyId,
            UserId = userId,
            EventType = eventType,
            Type = eventType,
            Title = title,
            Message = message,
            Severity = severity,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            ActionUrl = actionUrl,
            DeliveryChannel = "in_app",
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Push after the commit so a failed save never emits a phantom event.
        if (_realtime != null)
        {
            await _realtime.PushToUserAsync(userId, eventType, title, message, severity);
        }
    }
}