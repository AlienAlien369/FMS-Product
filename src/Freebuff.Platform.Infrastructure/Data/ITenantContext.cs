using System.Security.Claims;

namespace Freebuff.Platform.Infrastructure.Data;

/// <summary>
/// Provides current tenant (company) and user context for multi-tenancy enforcement.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    string? UserId { get; }
    string? UserRole { get; }
    bool IsSuperAdmin { get; }
}
