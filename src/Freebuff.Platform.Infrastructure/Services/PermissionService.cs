using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Centralized permission service. Calculates effective permissions by intersecting
/// company module entitlements with user role permissions. Single source of truth.
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

        var enabledModules = await GetEnabledModuleCodesAsync(tenantId);

        // Company-allowed: only permissions whose Module is enabled for the company
        var companyAllowedList = await _db.Permissions
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == EntityStatus.Active
                        && enabledModules.Contains(p.Module))
            .Select(p => p.Code)
            .ToListAsync();
        var companyAllowed = companyAllowedList.ToHashSet();

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

        // Effective = rolePermissions ∩ companyAllowed
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

    public async Task<HashSet<string>> GetCompanyAllowedPermissionsAsync(Guid tenantId)
    {
        var enabledModules = await GetEnabledModuleCodesAsync(tenantId);
        var list = await _db.Permissions
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == EntityStatus.Active
                        && enabledModules.Contains(p.Module))
            .Select(p => p.Code)
            .ToListAsync();
        return list.ToHashSet();
    }

    public async Task<bool> IsCompanyModuleEnabledAsync(Guid tenantId, string moduleCode)
    {
        var modules = await GetEnabledModuleCodesAsync(tenantId);
        return modules.Contains(moduleCode);
    }

    public async Task<HashSet<string>> GetEnabledModuleCodesAsync(Guid tenantId)
    {
        var list = await _db.ModuleConfigurations
            .AsNoTracking()
            .Where(mc => mc.CompanyId == tenantId && !mc.IsDeleted && mc.Status == EntityStatus.Active)
            .Join(_db.Modules.Where(m => !m.IsDeleted && m.Status == EntityStatus.Active),
                  mc => mc.ModuleId, m => m.Id, (mc, m) => m.Code)
            .ToListAsync();
        return list.ToHashSet();
    }
}
