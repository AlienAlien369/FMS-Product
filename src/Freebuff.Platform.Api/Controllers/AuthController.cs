using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Infrastructure.Data;
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

    public AuthController(IAuthService authService, ApplicationDbContext db)
    {
        _authService = authService;
        _db = db;
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
        var userId = User.GetUserId();
        var permissions = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.IsDeleted)
            .SelectMany(ur => ur.Role.RolePermissions
                .Where(rp => !rp.IsDeleted)
                .Select(rp => rp.Permission.Code))
            .Distinct()
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new { permissions }));
    }
}
