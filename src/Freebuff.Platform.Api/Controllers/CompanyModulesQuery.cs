using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Controllers;

/// <summary>
/// A company's modules, derived strictly from its package (legacy
/// ModuleConfigurations as fallback). One builder, two views:
///   - SuperAdmin (CompanyDetail Modules tab): the full detail view — every
///     module with every registered page, planned/adminOnly included, so the
///     package's full scope is reviewable.
///   - Tenant (company admin): only the live modules/pages the company can
///     actually use — the same visibility rule as the tenant /modules catalog.
/// </summary>
internal static class CompanyModulesQuery
{
    /// <summary>Returns null when the company does not exist.</summary>
    public static async Task<object?> ForCompanyAsync(ApplicationDbContext db, Guid companyId, bool tenantView)
    {
        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId && !c.IsDeleted)
            .Select(c => new { c.PackageId, c.SubscriptionId })
            .FirstOrDefaultAsync();
        if (company == null) return null;

        var packageId = company.PackageId;
        if (packageId == null && company.SubscriptionId != null)
        {
            packageId = await db.Subscriptions.AsNoTracking()
                .Where(s => s.Id == company.SubscriptionId && !s.IsDeleted && s.Status == SubscriptionStatus.Active)
                .Select(s => (Guid?)s.PackageId)
                .FirstOrDefaultAsync();
        }

        string? packageName = null;
        HashSet<Guid> includedModuleIds = new();
        if (packageId != null)
        {
            var pkg = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId.Value && !p.IsDeleted);
            packageName = pkg?.Name;
            var granted = await db.PackageModules.AsNoTracking()
                .Where(pm => pm.PackageId == packageId.Value && !pm.IsDeleted)
                .Select(pm => pm.ModuleId)
                .ToListAsync();
            includedModuleIds = granted.ToHashSet();
        }
        else
        {
            // Legacy companies without a package keep their historical rows until a package is assigned.
            var legacy = await db.ModuleConfigurations.AsNoTracking()
                .Where(mc => mc.CompanyId == companyId && !mc.IsDeleted && mc.Status == EntityStatus.Active)
                .Select(mc => mc.ModuleId)
                .ToListAsync();
            includedModuleIds = legacy.ToHashSet();
        }

        var modules = (await db.Modules.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync())
            .Where(m => !tenantView || (PageRegistry.IsLiveModuleForTenant(m) && includedModuleIds.Contains(m.Id)))
            .ToList();
        var pagesByModule = (await db.Pages.AsNoTracking().Where(p => !p.IsDeleted).ToListAsync())
            .GroupBy(p => p.ModuleId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.DisplayOrder).ToList());

        var result = modules.Select(m =>
        {
            var pages = pagesByModule.TryGetValue(m.Id, out var dbPages) && dbPages.Count > 0
                ? tenantView ? dbPages.Where(PageRegistry.IsLiveForTenant).ToList() : dbPages
                : PageRegistry.PagesInModule(m.Code)
                    .Where(p => !tenantView || (!p.Planned && !p.AdminOnly && p.Nav && p.Route != null))
                    .Select(ToPage).ToList();
            return new
            {
                m.Id, m.Code, m.Name, m.Description, m.Icon, m.IsCore,
                Status = (int)m.Status, m.DisplayOrder,
                Included = includedModuleIds.Contains(m.Id),
                PageCount = pages.Count(p => !p.Planned),
                PlannedPageCount = pages.Count(p => p.Planned),
                Pages = pages.Select(PageRegistry.PageView).ToList()
            };
        }).ToList();

        return new
        {
            PackageId = packageId,
            PackageName = packageName,
            IncludedModuleCodes = result.Where(r => r.Included).Select(r => r.Code).ToList(),
            Modules = result
        };
    }

    public static Page ToPage(PageDefinition p) => new()
    {
        Id = Guid.Empty,
        Key = p.Key,
        Name = p.Label,
        Route = p.Route,
        Icon = p.Icon,
        Nav = p.Nav,
        AdminOnly = p.AdminOnly,
        Planned = p.Planned,
        IsCore = p.IsCore,
        Status = EntityStatus.Active,
        DisplayOrder = p.Order,
        Description = p.Description
    };
}