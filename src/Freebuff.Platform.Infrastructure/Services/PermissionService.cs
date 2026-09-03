using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Centralized permission service. Calculates effective permissions as
///
///   (pages whose top-level module is in the company's package)
///   ∩ (permissions granted by the user's roles)
///
/// Module access is derived ONLY from the company's assigned package
/// (company.PackageId → package.PackageModules → module codes). There is no
/// per-company module override: granting more access means changing package.
/// </summary>
public interface IPermissionService
{
    Task<HashSet<string>> GetEffectivePermissionsAsync(Guid userId, Guid tenantId);
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode);
    Task<bool> HasAnyPermissionAsync(Guid userId, Guid tenantId, IEnumerable<string> permissionCodes);
    Task<HashSet<string>> GetCompanyAllowedPermissionsAsync(Guid tenantId);
    Task<bool> IsCompanyModuleEnabledAsync(Guid tenantId, string moduleCode);
    Task<HashSet<string>> GetEnabledModuleCodesAsync(Guid tenantId);
    void InvalidateCache(Guid userId, Guid tenantId);
    void InvalidateAllCache();
}

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<(Guid userId, Guid tenantId), (HashSet<string> perms, DateTime cachedAt)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    public PermissionService(ApplicationDbContext db) => _db = db;

    public void InvalidateCache(Guid userId, Guid tenantId) => _cache.Remove((userId, tenantId));
    public void InvalidateAllCache() => _cache.Clear();

    public async Task<HashSet<string>> GetEffectivePermissionsAsync(Guid userId, Guid tenantId)
    {
        var key = (userId, tenantId);
        if (_cache.TryGetValue(key, out var cached) && (DateTime.UtcNow - cached.cachedAt) < CacheDuration)
            return cached.perms;

        var companyAllowed = await GetCompanyAllowedPermissionsAsync(tenantId);

        // User's role permissions (union of all roles)
        var rolePermList = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId && !ur.IsDeleted
                         && ur.Role.CompanyId == tenantId && !ur.Role.IsDeleted
                         && ur.Role.Status == EntityStatus.Active)
            .SelectMany(ur => ur.Role.RolePermissions
                .Where(rp => !rp.IsDeleted)
                .Select(rp => rp.Permission.Code))
            .Distinct()
            .ToListAsync();
        var rolePermissions = rolePermList.ToHashSet();

        // Effective = rolePermissions ∩ company-allowed (package-derived)
        var effective = rolePermissions.Intersect(companyAllowed).ToHashSet();

        _cache[key] = (effective, DateTime.UtcNow);
        return effective;
    }

    public async Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode)
    {
        var perms = await GetEffectivePermissionsAsync(userId, tenantId);
        return perms.Contains(permissionCode);
    }

    public async Task<bool> HasAnyPermissionAsync(Guid userId, Guid tenantId, IEnumerable<string> permissionCodes)
    {
        var perms = await GetEffectivePermissionsAsync(userId, tenantId);
        return permissionCodes.Any(p => perms.Contains(p));
    }

    public async Task<bool> IsCompanyModuleEnabledAsync(Guid tenantId, string moduleCode)
    {
        var modules = await GetEnabledModuleCodesAsync(tenantId);
        return modules.Contains(moduleCode);
    }

    /// <summary>Top-level module codes (fleet, organization, …) the company's package grants.</summary>
    public async Task<HashSet<string>> GetEnabledModuleCodesAsync(Guid tenantId)
    {
        var company = await _db.Companies
            .AsNoTracking()
            .Where(c => c.Id == tenantId && !c.IsDeleted)
            .Select(c => new { c.PackageId, c.SubscriptionId })
            .FirstOrDefaultAsync();
        if (company == null) return new HashSet<string>();

        var packageId = company.PackageId;
        if (packageId == null && company.SubscriptionId != null)
        {
            packageId = await _db.Subscriptions
                .AsNoTracking()
                .Where(s => s.Id == company.SubscriptionId && !s.IsDeleted && s.Status == SubscriptionStatus.Active)
                .Select(s => (Guid?)s.PackageId)
                .FirstOrDefaultAsync();
        }

        if (packageId != null)
        {
            var codes = await _db.PackageModules
                .AsNoTracking()
                .Where(pm => pm.PackageId == packageId.Value && !pm.IsDeleted)
                .Join(_db.Modules.Where(m => !m.IsDeleted && m.Status == EntityStatus.Active),
                      pm => pm.ModuleId, m => m.Id, (pm, m) => m.Code)
                .ToListAsync();
            return codes.ToHashSet();
        }

        // Legacy fallback: companies that predate package-driven access keep their
        // historical per-company module rows until a package is assigned.
        var legacy = await _db.ModuleConfigurations
            .AsNoTracking()
            .Where(mc => mc.CompanyId == tenantId && !mc.IsDeleted && mc.Status == EntityStatus.Active)
            .Join(_db.Modules.Where(m => !m.IsDeleted && m.Status == EntityStatus.Active),
                  mc => mc.ModuleId, m => m.Id, (mc, m) => m.Code)
            .ToListAsync();
        return legacy.ToHashSet();
    }

    /// <summary>
    /// All permission codes a company may grant: every registered page whose
    /// top-level module is included in the company's package. Unknown/planned
    /// pages are handled here — a page must be registered AND its module enabled.
    /// </summary>
    public async Task<HashSet<string>> GetCompanyAllowedPermissionsAsync(Guid tenantId)
    {
        var enabledModules = await GetEnabledModuleCodesAsync(tenantId);

        var permissionRows = await _db.Permissions
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == EntityStatus.Active)
            .Select(p => new { p.Code, p.Module })
            .ToListAsync();

        var allowed = permissionRows
            .Where(p => PageRegistry.ModuleOfPage(p.Module) is string mod && enabledModules.Contains(mod))
            .Select(p => p.Code)
            .ToHashSet();

        return allowed;
    }
}
