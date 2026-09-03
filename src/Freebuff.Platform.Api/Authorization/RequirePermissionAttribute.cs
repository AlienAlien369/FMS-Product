using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;

namespace Freebuff.Platform.Api.Authorization;

/// <summary>
/// Declarative permission check. Apply to any controller action:
///   [RequirePermission("driver.create")]
/// SuperAdmin always passes. Returns 403 if user lacks the permission.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string Permission { get; }

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // SuperAdmin bypasses all permission checks
        if (context.HttpContext.User.IsSuperAdmin())
            return;

        var permissionService = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();

        var userId = context.HttpContext.User.GetUserId();
        var tenantId = context.HttpContext.User.GetTenantId();

        var hasPermission = await permissionService.HasPermissionAsync(userId, tenantId, Permission);

        if (!hasPermission)
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                code = "FORBIDDEN",
                message = $"Permission denied: {Permission}"
            })
            {
                StatusCode = 403
            };
        }
    }
}

/// <summary>
/// Requires ANY of the listed permissions. SuperAdmin always passes.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireAnyPermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string[] Permissions { get; }

    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.IsSuperAdmin())
            return;

        var permissionService = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        var userId = context.HttpContext.User.GetUserId();
        var tenantId = context.HttpContext.User.GetTenantId();

        var hasAny = await permissionService.HasAnyPermissionAsync(userId, tenantId, Permissions);

        if (!hasAny)
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                code = "FORBIDDEN",
                message = $"Permission denied: requires any of [{string.Join(", ", Permissions)}]"
            })
            {
                StatusCode = 403
            };
        }
    }
}
