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

/// <summary>
/// Fake notification service that records every dispatch — lets tests assert
/// which event type + severity the alert pipeline pushes to company admins
/// without a database or a real recipient set.
/// </summary>
public sealed class CapturingNotificationService : INotificationService
{
    public List<(string EventType, int Severity, string Title)> Dispatched { get; } = new();

    public Task NotifyUserAsync(Guid companyId, Guid userId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title));
        return Task.CompletedTask;
    }

    public Task<int> NotifyRoleUsersAsync(Guid companyId, Guid roleId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title));
        return Task.FromResult(0);
    }

    public Task<int> NotifyCompanyAdminsAsync(Guid companyId, string eventType, string title, string message,
        int severity, string? relatedEntityType = null, Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add((eventType, severity, title));
        return Task.FromResult(1);
    }

    public Task<int> NotifyUsersWhoseEffectivePermissionsChangedAsync(Guid companyId, Guid roleId,
        IReadOnlySet<Guid> oldPermissionIds, IReadOnlySet<Guid> newPermissionIds,
        string title, string message, int severity, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? actionUrl = null)
    {
        Dispatched.Add(("permission.role_updated", severity, title));
        return Task.FromResult(0);
    }
}
