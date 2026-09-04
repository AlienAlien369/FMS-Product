using System.Security.Claims;

namespace Freebuff.Platform.Infrastructure.CompanyScope;

/// <summary>
/// Resolves the effective company scope for a request from the (never trusted)
/// X-Company-Scope header and the user's permitted-company set.
/// </summary>
public interface ICompanyScopeResolver
{
    /// <summary>
    /// Computes effective scope = requested scope ∩ permitted set.
    /// A missing header defaults to the user's own company for normal users and
    /// ALL (unconstrained) for cross-tenant users.
    /// </summary>
    Task<ResolvedCompanyScope> ResolveAsync(ClaimsPrincipal user, string? headerValue, CancellationToken ct = default);
}
