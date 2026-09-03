using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ApplicationDbContext _db;
    private readonly IPermissionService _permissionService;

    public AuthController(IAuthService authService, ApplicationDbContext db, IPermissionService permissionService)
    {
        _authService = authService;
        _db = db;
        _permissionService = permissionService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginDto request)
    {
        var result = await _authService.LoginAsync(request);
        if (result == null)
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail("INVALID_CREDENTIALS", "Invalid email or password"));

        return Ok(ApiResponse<AuthResponseDto>.Ok(result));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Refresh([FromBody] RefreshTokenDto request)
    {
        var result = await _authService.RefreshTokenAsync(request);
        if (result == null)
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail("INVALID_TOKEN", "Invalid or expired token"));

        return Ok(ApiResponse<AuthResponseDto>.Ok(result));
    }

    [HttpGet("permissions")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<object>>> GetMyPermissions()
    {
        if (User.IsSuperAdmin())
        {
            // SuperAdmin gets all permissions
            var allPerms = await _db.Permissions.AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => p.Code)
                .ToListAsync();
            return Ok(ApiResponse<object>.Ok(new { permissions = allPerms }));
        }

        var userId = User.GetUserId();
        var tenantId = User.GetTenantId();
        var effectivePerms = await _permissionService.GetEffectivePermissionsAsync(userId, tenantId);

        return Ok(ApiResponse<object>.Ok(new { permissions = effectivePerms.ToList() }));
    }
}
