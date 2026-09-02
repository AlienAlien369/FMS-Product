using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Api.Identity.Data;

public static class IdentitySeedData
{
    public static async Task SeedAsync(IdentityDbContext db)
    {
        if (await db.Users.AnyAsync())
            return; // Already seeded

        var platformCompanyId = Guid.NewGuid();
        var demoCompanyId = Guid.NewGuid();

        // Platform company
        // (We don't have Company in this DbContext, but we reference CompanyId by Guid)

        // Super Admin role
        var superAdminRole = new Role
        {
            Id = Guid.NewGuid(),
            Name = "SuperAdmin",
            Description = "Platform super administrator with full access",
            CompanyId = platformCompanyId,
            IsSystemRole = true,
            Status = EntityStatus.Active
        };
        db.Roles.Add(superAdminRole);

        // Super Admin user
        var superAdmin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@freebuff.com",
            NormalizedEmail = "ADMIN@FREEBUFF.COM",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            FirstName = "Super",
            LastName = "Admin",
            CompanyId = platformCompanyId,
            Status = EntityStatus.Active,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(superAdmin);

        db.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = superAdmin.Id,
            RoleId = superAdminRole.Id,
            TenantId = platformCompanyId
        });

        // Demo Company roles
        var adminRole = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Company Admin",
            Description = "Company administrator",
            CompanyId = demoCompanyId,
            IsSystemRole = true,
            Status = EntityStatus.Active
        };
        var fleetManagerRole = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Fleet Manager",
            Description = "Fleet operations manager",
            CompanyId = demoCompanyId,
            Status = EntityStatus.Active
        };
        db.Roles.AddRange(adminRole, fleetManagerRole);

        // Demo Admin user
        var demoAdmin = new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@demofleet.com",
            NormalizedEmail = "ADMIN@DEMOFLEET.COM",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            FirstName = "Demo",
            LastName = "Admin",
            CompanyId = demoCompanyId,
            Status = EntityStatus.Active,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(demoAdmin);

        db.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = demoAdmin.Id,
            RoleId = adminRole.Id,
            TenantId = demoCompanyId
        });

        await db.SaveChangesAsync();
    }
}
