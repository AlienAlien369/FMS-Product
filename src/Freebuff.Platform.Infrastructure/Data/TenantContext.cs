using System.Security.Claims;
using Freebuff.Platform.Infrastructure.CompanyScope;
using Microsoft.AspNetCore.Http;

namespace Freebuff.Platform.Infrastructure.Data;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ResolvedCompanyScope? Scope
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            if (http != null
                && http.Items.TryGetValue(CompanyScopePolicy.ItemsKey, out var resolved)
                && resolved is ResolvedCompanyScope scope)
            {
                return scope;
            }

            // Fallback when the scope middleware did not run (unit tests, hosted
            // jobs): mirror the historical claims-based behavior — SuperAdmin
            // unconstrained, everyone else constrained to their own tenant.
            return SynthesizeFromClaims(http?.User);
        }
    }

    private static ResolvedCompanyScope? SynthesizeFromClaims(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        Guid? tenantId = user.FindFirst("tenant_id")?.Value is { } t && Guid.TryParse(t, out var tid) ? tid : null;
        if (user.IsInRole("SuperAdmin"))
            return new ResolvedCompanyScope(tenantId, IsCrossTenant: true, EffectiveCompanyIds: null, DroppedIds: Array.Empty<Guid>());
        return tenantId.HasValue
            ? new ResolvedCompanyScope(tenantId, IsCrossTenant: false, EffectiveCompanyIds: new List<Guid> { tenantId.Value }, DroppedIds: Array.Empty<Guid>())
            : null;
    }

    public Guid? TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var tenantIdClaim = user?.FindFirst("tenant_id")?.Value;
            return tenantIdClaim != null ? Guid.Parse(tenantIdClaim) : null;
        }
    }

    public string? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("sub")?.Value;
        }
    }

    public string? UserRole
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.Role)?.Value;
        }
    }

    public bool IsSuperAdmin
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.IsInRole("SuperAdmin") ?? false;
        }
    }
}
