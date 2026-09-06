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
