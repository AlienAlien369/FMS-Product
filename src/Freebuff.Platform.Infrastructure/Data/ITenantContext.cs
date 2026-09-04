using Freebuff.Platform.Infrastructure.CompanyScope;

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

    /// <summary>
    /// Effective company scope resolved for the current request (from the
    /// X-Company-Scope header ∩ permitted set). Null when no request context
    /// (background jobs, seeding).
    /// </summary>
    ResolvedCompanyScope? Scope { get; }
}
