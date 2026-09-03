using System.Security.Claims;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Freebuff.Platform.Api.Middleware;

/// <summary>
/// Declarative permission attribute. Apply to controller actions to enforce authorization.
/// SuperAdmin always passes. For company users, checks effective permissions via PermissionService.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string[] Permissions { get; }
    public bool RequireAll { get; }

    /// <param name="permissions">Permission codes required (e.g. "driver.view", "driver.create")</param>
    /// <param name="requireAll">If true, user must have ALL listed permissions. If false, ANY suffices.</param>
    public RequirePermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
        RequireAll = false;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // SuperAdmin bypasses all permission checks
        if (user.IsInRole("SuperAdmin"))
            return;

        var permissionService = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        var tenantIdClaim = user.FindFirst("tenant_id")?.Value;

        if (userIdClaim == null || tenantIdClaim == null)
        {
            context.Result = new ForbidResult();
            return;
        }

        var userId = Guid.Parse(userIdClaim);
        var tenantId = Guid.Parse(tenantIdClaim);

        bool hasPermission;
        if (RequireAll)
        {
            hasPermission = true;
            foreach (var p in Permissions)
            {
                if (!await permissionService.HasPermissionAsync(userId, tenantId, p))
                {
                    hasPermission = false;
                    break;
                }
            }
        }
        else
        {
            hasPermission = await permissionService.HasAnyPermissionAsync(userId, tenantId, Permissions);
        }

        if (!hasPermission)
        {
            context.Result = new ObjectResult(new { success = false, message = "You don't have permission to perform this action." })
            {
                StatusCode = 403
            };
        }
    }
}
