using Freebuff.Platform.Infrastructure.Data;

namespace Freebuff.Platform.E2eTests.Rbac;

/// <summary>
/// The matrix oracle: computes, straight from the live database, what every
/// role/user SHOULD be able to do — role grants ∩ company package modules,
/// exactly the formula PermissionService.GetEffectivePermissionsAsync implements.
///
/// The permission matrix tests assert the API's actual behavior against this
/// oracle (never hand-typed expectations), and the matrix report is generated
/// from it, so the oracle and the system under test can only drift together
/// with the real schema/seed.
/// </summary>
public sealed class RbacOracle
{
    private readonly E2eDb _db;

    public RbacOracle(E2eDb db) => _db = db;

    /// <summary>Top-level module codes the company's package grants (Active modules only).</summary>
    public async Task<HashSet<string>> EnabledModuleCodesAsync(Guid companyId)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        await using var conn = _db.OpenAppConnection();
        await using var cmd = new Npgsql.NpgsqlCommand($$"""
            SELECT m."Code" FROM "Companies" c
            JOIN "PackageModules" pm ON pm."PackageId" = c."PackageId" AND pm."IsDeleted" = false
            JOIN "Modules" m ON m."Id" = pm."ModuleId" AND m."IsDeleted" = false AND m."Status" = 0
            WHERE c."Id" = '{{companyId}}' AND c."IsDeleted" = false
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) codes.Add(reader.GetString(0));
        return codes;
    }

    /// <summary>
    /// Every permission code the company may grant: codes of Active permissions
    /// whose page is neither Planned nor Inactive AND whose page's top-level
    /// module is in the company's package. Mirrors
    /// PermissionService.GetCompanyAllowedPermissionsAsync.
    /// </summary>
    public async Task<HashSet<string>> CompanyAllowedCodesAsync(Guid companyId)
    {
        var enabledModules = await EnabledModuleCodesAsync(companyId);

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        await using var conn = _db.OpenAppConnection();
        await using var cmd = new Npgsql.NpgsqlCommand($$"""
            SELECT p."Code", p."Module" FROM "Permissions" p
            WHERE p."IsDeleted" = false AND p."Status" = 0
              AND p."Module" NOT IN (
                  SELECT "Key" FROM "Pages"
                  WHERE "IsDeleted" = false AND ("Planned" = true OR "Status" <> 0))
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = reader.GetString(0);
            var pageKey = reader.GetString(1);
            if (PageRegistry.ModuleOfPage(pageKey) is string mod && enabledModules.Contains(mod))
                allowed.Add(code);
        }
        return allowed;
    }

    /// <summary>All permission codes each role (by name) is granted in a company.</summary>
    public async Task<Dictionary<string, HashSet<string>>> RoleGrantedCodesAsync(Guid companyId)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using var conn = _db.OpenAppConnection();
        await using var cmd = new Npgsql.NpgsqlCommand($$"""
            SELECT r."Name", p."Code" FROM "Roles" r
            JOIN "RolePermissions" rp ON rp."RoleId" = r."Id" AND rp."IsDeleted" = false
            JOIN "Permissions" p ON p."Id" = rp."PermissionId" AND p."IsDeleted" = false
            WHERE r."CompanyId" = '{{companyId}}' AND r."IsDeleted" = false AND r."Status" = 0
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            if (!result.TryGetValue(name, out var set))
                result[name] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(reader.GetString(1));
        }
        return result;
    }

    /// <summary>
    /// Effective codes for a user = union of their roles' grants ∩ company-allowed
    /// (the exact formula the API returns from /auth/permissions).
    /// </summary>
    public async Task<HashSet<string>> EffectiveCodesAsync(Guid userId, Guid companyId)
    {
        var allowed = await CompanyAllowedCodesAsync(companyId);

        var granted = new HashSet<string>(StringComparer.Ordinal);
        await using var conn = _db.OpenAppConnection();
        await using var cmd = new Npgsql.NpgsqlCommand($$"""
            SELECT p."Code" FROM "Users" u
            JOIN "UserRoles" ur ON ur."UserId" = u."Id" AND ur."IsDeleted" = false
            JOIN "Roles" r ON r."Id" = ur."RoleId" AND r."IsDeleted" = false AND r."Status" = 0
            JOIN "RolePermissions" rp ON rp."RoleId" = r."Id" AND rp."IsDeleted" = false
            JOIN "Permissions" p ON p."Id" = rp."PermissionId" AND p."IsDeleted" = false
            WHERE u."Id" = '{{userId}}' AND u."IsDeleted" = false
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) granted.Add(reader.GetString(0));

        granted.IntersectWith(allowed);
        return granted;
    }

    /// <summary>userId and companyId for a user email.</summary>
    public async Task<(Guid UserId, Guid CompanyId)> IdentityAsync(string email)
    {
        var row = await _db.ScalarAsync(
            $"SELECT \"Id\"::text || '|' || \"CompanyId\"::text FROM \"Users\" WHERE \"Email\" = '{email}' AND \"IsDeleted\" = false")
            ?? throw new InvalidOperationException($"User {email} not found");
        var parts = row.Split('|');
        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }

    public async Task<Guid> CompanyIdAsync(string slug)
        => Guid.Parse((await _db.ScalarAsync($"SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = '{slug}'"))!);
}