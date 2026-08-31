using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Shared test helper for creating JWT tokens and HTTP test utilities.
/// Tests seed data exclusively via HTTP POST calls to avoid dual-provider InMemory DB issues.
/// </summary>
public static class TestHelper
{
    public const string JwtKey = "TestHelperKey_32Chars_Required!!!";

    /// <summary>
    /// Creates a valid JWT token with the specified claims, signed with the test key.
    /// Use this to bootstrap API calls when no user exists in the DB yet.
    /// </summary>
    public static string CreateToken(Guid? userId = null, Guid? companyId = null, params string[] roles)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (userId ?? Guid.NewGuid()).ToString()),
            new("tenant_id", (companyId ?? Guid.NewGuid()).ToString()),
            new(ClaimTypes.Email, "test@freebuff.com"),
            new("full_name", "Test User"),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Sets the Authorization header on the HttpClient to use a test JWT.
    /// </summary>
    public static void SetAuth(HttpClient client, Guid? companyId = null, params string[] roles)
    {
        client.DefaultRequestHeaders.Authorization =
            new("Bearer", CreateToken(companyId: companyId, roles: roles));
    }
}
