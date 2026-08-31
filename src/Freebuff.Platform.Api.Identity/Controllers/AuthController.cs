using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Api.Identity.Services;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Identity.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IdentityAuthService _authService;

    public AuthController(IdentityAuthService authService) => _authService = authService;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginDto request)
    {
        var result = await _authService.LoginAsync(request);
        if (result == null)
            return Unauthorized(ApiResponse<AuthResponseDto>.Fail("INVALID_CREDENTIALS", "Invalid email or password"));
        return Ok(ApiResponse<AuthResponseDto>.Ok(result));
    }
}
