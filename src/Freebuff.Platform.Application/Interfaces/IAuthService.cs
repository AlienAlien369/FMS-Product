using Freebuff.Platform.Application.DTOs;

namespace Freebuff.Platform.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto?> LoginAsync(LoginDto request);
    Task<AuthResponseDto?> RefreshTokenAsync(RefreshTokenDto request);
}
