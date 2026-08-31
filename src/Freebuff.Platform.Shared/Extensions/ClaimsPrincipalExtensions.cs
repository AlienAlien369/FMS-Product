using System.Security.Claims;

namespace Freebuff.Platform.Shared.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetTenantId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst("tenant_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No tenant context");
    }

    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("sub")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No user context");
    }

    public static string GetUserIdString(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst("sub")?.Value
               ?? throw new UnauthorizedAccessException("No user context");
    }

    public static string GetEmail(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Email)?.Value
               ?? throw new UnauthorizedAccessException("No email claim");
    }

    public static bool IsSuperAdmin(this ClaimsPrincipal principal)
    {
        return principal.IsInRole("SuperAdmin");
    }

    public static List<string> GetRoles(this ClaimsPrincipal principal)
    {
        return principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
    }
}
