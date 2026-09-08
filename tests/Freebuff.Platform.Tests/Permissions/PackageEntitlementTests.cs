using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Permissions;

/// <summary>
/// Module entitlement (Package → modules → pages → permissions):
/// fuel.view / maintenance.view are effective ONLY when the company's package
/// includes the "fleet" module — a role grant alone is never enough. This is
/// the server-side guarantee behind \"a company not entitled to these modules
/// cannot access either page even via direct URL\": the API returns 403 and
/// the frontend route guard uses the same effective permission set.
/// </summary>
public class PackageEntitlementTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("pkg_" + name + "_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<(ApplicationDbContext Db, Guid EntitledCompany, Guid UnentitledCompany)> SeedAsync(string name)
    {
        var db = NewDb(name);

        var dashboardModule = new Module { Id = Guid.NewGuid(), Code = "dashboard", Name = "Dashboard", IsCore = true, Status = EntityStatus.Active };
        var fleetModule = new Module { Id = Guid.NewGuid(), Code = "fleet", Name = "Fleet Operations", IsCore = true, Status = EntityStatus.Active };
        db.Modules.AddRange(dashboardModule, fleetModule);

        // Registered live pages (mirrors PageRegistry: Planned=false, route set).
        db.Pages.AddRange(
            new Page { Id = Guid.NewGuid(), Key = "fuel", Name = "Fuel", ModuleId = fleetModule.Id, Route = "/fuel", Nav = true, Planned = false, IsCore = false, Status = EntityStatus.Active },
            new Page { Id = Guid.NewGuid(), Key = "maintenance", Name = "Maintenance", ModuleId = fleetModule.Id, Route = "/maintenance", Nav = true, Planned = false, IsCore = false, Status = EntityStatus.Active });

        db.Permissions.AddRange(
            new Permission { Id = Guid.NewGuid(), Code = "fuel.view", Name = "view Fuel", Module = "fuel", Action = PermissionAction.Read, Status = EntityStatus.Active },
            new Permission { Id = Guid.NewGuid(), Code = "maintenance.view", Name = "view Maintenance", Module = "maintenance", Action = PermissionAction.Read, Status = EntityStatus.Active });

        // Packages: Basic grants fleet, Starter does not.
        var basic = new Package { Id = Guid.NewGuid(), Name = "Basic", Status = EntityStatus.Active };
        var starter = new Package { Id = Guid.NewGuid(), Name = "Starter", Status = EntityStatus.Active };
        db.Packages.AddRange(basic, starter);
        db.PackageModules.AddRange(
            new PackageModule { Id = Guid.NewGuid(), PackageId = basic.Id, ModuleId = dashboardModule.Id },
            new PackageModule { Id = Guid.NewGuid(), PackageId = basic.Id, ModuleId = fleetModule.Id },
            new PackageModule { Id = Guid.NewGuid(), PackageId = starter.Id, ModuleId = dashboardModule.Id });

        var entitledCompany = new Company { Id = Guid.NewGuid(), Name = "Entitled Co", Slug = "entitled", PackageId = basic.Id, Status = EntityStatus.Active };
        var unentitledCompany = new Company { Id = Guid.NewGuid(), Name = "Starter Co", Slug = "starter", PackageId = starter.Id, Status = EntityStatus.Active };
        db.Companies.AddRange(entitledCompany, unentitledCompany);
        await db.SaveChangesAsync();

        // Both companies get a user whose role grants fuel + maintenance view.
        var fuelPermId = await db.Permissions.Where(p => p.Code == "fuel.view").Select(p => p.Id).FirstAsync();
        var maintPermId = await db.Permissions.Where(p => p.Code == "maintenance.view").Select(p => p.Id).FirstAsync();
        foreach (var company in new[] { entitledCompany, unentitledCompany })
        {
            var role = new Role { Id = Guid.NewGuid(), Name = "Fleet Manager", CompanyId = company.Id, Status = EntityStatus.Active };
            db.Roles.Add(role);
            var user = new User
            {
                Id = Guid.NewGuid(), Email = $"user-{company.Name}@test.com", NormalizedEmail = $"USER-{company.Name}@TEST.COM".ToUpperInvariant(),
                FirstName = "Fleet", LastName = "Manager", CompanyId = company.Id, Status = EntityStatus.Active,
                EmailConfirmed = true
            };
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, TenantId = company.Id });
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = fuelPermId, TenantId = company.Id });
            db.RolePermissions.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = maintPermId, TenantId = company.Id });
        }
        await db.SaveChangesAsync();

        var entitledUser = await db.Users.AsNoTracking().FirstAsync(u => u.CompanyId == entitledCompany.Id);
        var unentitledUser = await db.Users.AsNoTracking().FirstAsync(u => u.CompanyId == unentitledCompany.Id);

        // Re-query companies with their users for the test to reference.
        return (db, entitledCompany.Id, unentitledCompany.Id);
    }

    [Fact]
    public async Task CompanyWithFleetModule_CanAccessFuelAndMaintenance()
    {
        var (db, entitled, unentitled) = await SeedAsync("ok");
        var service = new PermissionService(db);
        var userId = await db.Users.AsNoTracking().Where(u => u.CompanyId == entitled).Select(u => u.Id).FirstAsync();

        Assert.True(await service.HasPermissionAsync(userId, entitled, "fuel.view"));
        Assert.True(await service.HasPermissionAsync(userId, entitled, "maintenance.view"));
    }

    [Fact]
    public async Task CompanyWithoutFleetModule_CannotAccess_EvenWithRoleGrant()
    {
        var (db, entitled, unentitled) = await SeedAsync("deny");
        var service = new PermissionService(db);
        var userId = await db.Users.AsNoTracking().Where(u => u.CompanyId == unentitled).Select(u => u.Id).FirstAsync();

        // Role explicitly grants the permissions — but the package lacks fleet.
        Assert.False(await service.HasPermissionAsync(userId, unentitled, "fuel.view"));
        Assert.False(await service.HasPermissionAsync(userId, unentitled, "maintenance.view"));
    }

    [Fact]
    public async Task PlannedPage_GrantsNoAccess_EvenInEntitledPackage()
    {
        var (db, entitled, unentitled) = await SeedAsync("planned");
        // Flip the fuel page back to Planned — the module is entitled but the
        // page must not grant access (mirrors GetCompanyAllowedPermissionsAsync).
        var fuelPage = await db.Pages.FirstAsync(p => p.Key == "fuel");
        fuelPage.Planned = true;
        await db.SaveChangesAsync();

        var service = new PermissionService(db);
        var userId = await db.Users.AsNoTracking().Where(u => u.CompanyId == entitled).Select(u => u.Id).FirstAsync();

        Assert.False(await service.HasPermissionAsync(userId, entitled, "fuel.view"));
        Assert.True(await service.HasPermissionAsync(userId, entitled, "maintenance.view"));
    }
}