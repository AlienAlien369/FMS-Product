using Freebuff.Platform.Infrastructure.Services;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Stub that always returns true — all alert types are entitled in unit tests.
/// Tests that need to verify enforcement behavior should use the real service.
/// </summary>
public sealed class AlwaysEntitledAlertEnforcement : IAlertTypeEnforcement
{
    public Task<bool> IsEntitledAsync(Guid companyId, string alertTypeCode) => Task.FromResult(true);
}

/// <summary>Denies every alert type — isolates the panic bypass (which must ignore enforcement).</summary>
public sealed class DenyAllAlertEnforcement : IAlertTypeEnforcement
{
    public Task<bool> IsEntitledAsync(Guid companyId, string alertTypeCode) => Task.FromResult(false);
}

/// <summary>No-op IPermissionService for constructing the real NotificationService in unit tests.</summary>
public sealed class StubPermissionService : IPermissionService
{
    public Task<HashSet<string>> GetEffectivePermissionsAsync(Guid userId, Guid tenantId) => Task.FromResult(new HashSet<string>());
    public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode) => Task.FromResult(true);
    public Task<bool> HasAnyPermissionAsync(Guid userId, Guid tenantId, IEnumerable<string> permissionCodes) => Task.FromResult(true);
    public Task<HashSet<string>> GetCompanyAllowedPermissionsAsync(Guid tenantId) => Task.FromResult(new HashSet<string>());
    public Task<bool> IsCompanyModuleEnabledAsync(Guid tenantId, string moduleCode) => Task.FromResult(true);
    public Task<HashSet<string>> GetEnabledModuleCodesAsync(Guid tenantId) => Task.FromResult(new HashSet<string>());
    public void InvalidateCache(Guid userId, Guid tenantId) { }
    public void InvalidateAllCache() { }
}

/// <summary>
/// Fake notification service that records every dispatch — lets tests assert
/// which event type + severity the alert pipeline pushes to company admins
/// without a database or a real recipient set.
/// </summary>
public sealed class CapturingNotificationService : INotificationService
{
    public List<(string EventType, int Severity, string Title, string Message)> Dispatched { get; } = new();

    public Task NotifyUserAsync(Guid companyId, Guid userId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title, message));
        return Task.CompletedTask;
    }

    public Task<int> NotifyRoleUsersAsync(Guid companyId, Guid roleId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title, message));
        return Task.FromResult(0);
    }

    public Task<int> NotifyCompanyAdminsAsync(Guid companyId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title, message));
        return Task.FromResult(1);
    }

    public Task<int> NotifyPanicAsync(Guid companyId, Guid vehicleId, string alertTypeCode, string eventType,
        string title, string message, int severity, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title, message));
        return Task.FromResult(1);
    }

    public Task<int> NotifyUsersWhoseEffectivePermissionsChangedAsync(Guid companyId, Guid roleId,
        IReadOnlySet<Guid> oldPermissionIds, IReadOnlySet<Guid> newPermissionIds,
        string title, string message, int severity, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add(("permission.role_updated", severity, title, message));
        return Task.FromResult(0);
    }
}
