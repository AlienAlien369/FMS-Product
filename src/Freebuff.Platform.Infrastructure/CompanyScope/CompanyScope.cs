namespace Freebuff.Platform.Infrastructure.CompanyScope;

/// <summary>
/// Stateless, header-based company scope for cross-company views.
/// The frontend sends X-Company-Scope on every API call while a scope selector
/// is active; no "current scope" state is stored server-side. Authorization is
/// derived per request from the authenticated user's permitted-company set, and
/// the header only narrows that set — never widens it.
/// </summary>
public static class CompanyScopePolicy
{
    public const string HeaderName = "X-Company-Scope";

    /// <summary>Sentinel meaning "every company the caller may access".</summary>
    public const string All = "ALL";

    /// <summary>HttpContext.Items key holding the resolved scope for the request.</summary>
    public const string ItemsKey = "freebuff:resolved-company-scope";

    /// <summary>
    /// When the caller's effective scope is constrained (list views, dashboards),
    /// returns the company ids rows must belong to; null when unconstrained
    /// (SuperAdmin viewing ALL, anonymous endpoints, no scope context).
    /// Replaces hand-rolled <c>isSuperAdmin || x.CompanyId == tenantId</c> predicates:
    /// for a normal user the effective set is always exactly their own company, so
    /// <c>ids == null || ids.Contains(x.CompanyId)</c> alone is correct for every role.
    /// </summary>
    public static List<Guid>? EffectiveIds(ResolvedCompanyScope? scope)
    {
        if (scope is not { IsConstrained: true } || scope.EffectiveCompanyIds is null)
            return null;
        return new List<Guid>(scope.EffectiveCompanyIds);
    }
}

/// <summary>
/// Effective scope for one request, computed as:
///   requested header scope ∩ user's permitted-company set.
/// Companies requested but not permitted are dropped silently (and logged) so a
/// stale or manipulated header degrades gracefully instead of failing the request.
/// </summary>
/// <param name="OwnCompanyId">Company from the JWT tenant claim (null when unauthenticated / hosted context).</param>
/// <param name="IsCrossTenant">True when the user may act across companies (SuperAdmin or a cross-tenant role).</param>
/// <param name="EffectiveCompanyIds">
/// When non-null the caller may only see rows whose company is in this list
/// (empty list = nothing visible). Null means no constraint (e.g. cross-tenant
/// user viewing ALL, or no scope context such as background jobs).
/// </param>
/// <param name="DroppedIds">Company ids the header requested that were NOT permitted (for audit logging).</param>
public sealed record ResolvedCompanyScope(
    Guid? OwnCompanyId,
    bool IsCrossTenant,
    IReadOnlyList<Guid>? EffectiveCompanyIds,
    IReadOnlyList<Guid> DroppedIds)
{
    public static ResolvedCompanyScope Unconstrained() =>
        new(null, IsCrossTenant: true, EffectiveCompanyIds: null, DroppedIds: Array.Empty<Guid>());

    public bool IsConstrained => EffectiveCompanyIds != null;
}
