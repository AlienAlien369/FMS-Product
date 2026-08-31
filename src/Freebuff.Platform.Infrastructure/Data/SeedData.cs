using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Data;

public static class SeedData
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        // Languages
        if (!await db.Languages.AnyAsync())
        {
            db.Languages.AddRange(
                new Language { Id = Guid.NewGuid(), Code = "en", Name = "English", NativeName = "English", IsDefault = true, Status = EntityStatus.Active, DisplayOrder = 1 },
                new Language { Id = Guid.NewGuid(), Code = "ar", Name = "Arabic", NativeName = "العربية", IsRightToLeft = true, Status = EntityStatus.Active, DisplayOrder = 2 },
                new Language { Id = Guid.NewGuid(), Code = "hi", Name = "Hindi", NativeName = "हिन्दी", Status = EntityStatus.Active, DisplayOrder = 3 },
                new Language { Id = Guid.NewGuid(), Code = "es", Name = "Spanish", NativeName = "Español", Status = EntityStatus.Active, DisplayOrder = 4 },
                new Language { Id = Guid.NewGuid(), Code = "fr", Name = "French", NativeName = "Français", Status = EntityStatus.Active, DisplayOrder = 5 }
            );
        }

        // Currencies
        if (!await db.Currencies.AnyAsync())
        {
            db.Currencies.AddRange(
                new Currency { Id = Guid.NewGuid(), Code = "USD", Name = "US Dollar", Symbol = "$", IsDefault = true, Status = EntityStatus.Active, DisplayOrder = 1 },
                new Currency { Id = Guid.NewGuid(), Code = "EUR", Name = "Euro", Symbol = "€", Status = EntityStatus.Active, DisplayOrder = 2 },
                new Currency { Id = Guid.NewGuid(), Code = "GBP", Name = "British Pound", Symbol = "£", Status = EntityStatus.Active, DisplayOrder = 3 },
                new Currency { Id = Guid.NewGuid(), Code = "INR", Name = "Indian Rupee", Symbol = "₹", Status = EntityStatus.Active, DisplayOrder = 4 },
                new Currency { Id = Guid.NewGuid(), Code = "AED", Name = "UAE Dirham", Symbol = "د.إ", Status = EntityStatus.Active, DisplayOrder = 5 }
            );
        }

        // Modules
        var modules = new Dictionary<string, Module>();
        if (!await db.Modules.AnyAsync())
        {
            var moduleList = new (string Code, string Name, string? Desc, bool IsCore, int Order)[]
            {
                ("fleet", "Fleet Management", "Core fleet management module", true, 1),
                ("vehicles", "Vehicle Management", "Vehicle CRUD and tracking", true, 2),
                ("drivers", "Driver Management", "Driver profiles and management", true, 3),
                ("geofencing", "Geofencing", "Geofence creation and monitoring", false, 4),
                ("trips", "Trip Management", "Trip planning and execution", true, 5),
                ("tracking", "Live Tracking", "Real-time vehicle tracking", true, 6),
                ("fuel", "Fuel Monitoring", "Fuel level and consumption tracking", false, 7),
                ("maintenance", "Maintenance", "Preventive and corrective maintenance", false, 8),
                ("alerts", "Alerts & Alarms", "Configurable alert system", true, 9),
                ("compliance", "Compliance", "Document and compliance management", false, 10),
                ("pod", "Proof of Delivery", "Digital proof of delivery", false, 11),
                ("cctv", "CCTV / Video Telematics", "Video monitoring and playback", false, 12),
                ("route-optimization", "Route Optimization", "Optimal route calculation", false, 13),
                ("reports", "Reports & Analytics", "Reporting and dashboards", true, 14)
            };

            foreach (var (code, name, desc, isCore, order) in moduleList)
            {
                var module = new Module
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Name = name,
                    Description = desc,
                    IsCore = isCore,
                    DisplayOrder = order,
                    Status = EntityStatus.Active
                };
                db.Modules.Add(module);
                modules[code] = module;
            }
        }

        // Permissions
        if (!await db.Permissions.AnyAsync())
        {
            var actions = new[] { "view", "create", "edit", "delete", "export", "assign", "track", "immobilize" };
            var modulesList = new[] { "vehicle", "driver", "trip", "geofence", "alert", "fuel", "maintenance", "client", "document", "report", "user", "role", "company", "configuration", "subscription" };

            foreach (var mod in modulesList)
            {
                foreach (var action in actions)
                {
                    db.Permissions.Add(new Permission
                    {
                        Id = Guid.NewGuid(),
                        Code = $"{mod}.{action}",
                        Name = $"{action} {mod}",
                        Module = mod,
                        Action = action switch
                        {
                            "view" => PermissionAction.Read,
                            "create" => PermissionAction.Create,
                            "edit" => PermissionAction.Update,
                            "delete" => PermissionAction.Delete,
                            "export" => PermissionAction.Export,
                            "assign" => PermissionAction.Assign,
                            "track" => PermissionAction.Execute,
                            "immobilize" => PermissionAction.Manage,
                            _ => PermissionAction.Read
                        },
                        Status = EntityStatus.Active
                    });
                }
            }
        }

        // Packages
        if (!await db.Packages.AnyAsync())
        {
            var basic = new Package
            {
                Id = Guid.NewGuid(), Name = "Basic", Description = "Basic fleet management", Price = 49, Currency = "USD",
                BillingCycle = "monthly", IsDefault = true, MaxUsers = 5, MaxVehicles = 20, MaxDrivers = 20,
                Status = EntityStatus.Active, DisplayOrder = 1
            };
            var pro = new Package
            {
                Id = Guid.NewGuid(), Name = "Professional", Description = "Advanced fleet management", Price = 149, Currency = "USD",
                BillingCycle = "monthly", MaxUsers = 25, MaxVehicles = 100, MaxDrivers = 100,
                Status = EntityStatus.Active, DisplayOrder = 2
            };
            var enterprise = new Package
            {
                Id = Guid.NewGuid(), Name = "Enterprise", Description = "Complete fleet management platform", Price = 499, Currency = "USD",
                BillingCycle = "monthly", MaxUsers = -1, MaxVehicles = -1, MaxDrivers = -1,
                MaxAlertRules = -1, MaxGeofences = -1, Status = EntityStatus.Active, DisplayOrder = 3
            };
            db.Packages.AddRange(basic, pro, enterprise);
        }

        // Super Admin user (in a platform company)
        if (!await db.Users.AnyAsync())
        {
            var platformCompany = new Company
            {
                Id = Guid.NewGuid(),
                Name = "Freebuff Platform",
                Slug = "platform",
                DefaultLanguage = "en",
                DefaultTimezone = "UTC",
                DefaultCurrency = "USD",
                Status = EntityStatus.Active
            };
            db.Companies.Add(platformCompany);

            var superAdmin = new User
            {
                Id = Guid.NewGuid(),
                Email = "admin@freebuff.com",
                NormalizedEmail = "ADMIN@FREEBUFF.COM",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                FirstName = "Super",
                LastName = "Admin",
                CompanyId = platformCompany.Id,
                Status = EntityStatus.Active,
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            db.Users.Add(superAdmin);

            var superAdminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "SuperAdmin",
                Description = "Platform super administrator with full access",
                CompanyId = platformCompany.Id,
                IsSystemRole = true,
                Status = EntityStatus.Active
            };
            db.Roles.Add(superAdminRole);

            db.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(),
                UserId = superAdmin.Id,
                RoleId = superAdminRole.Id,
                TenantId = platformCompany.Id
            });

            // Demo Company
            var demoCompany = new Company
            {
                Id = Guid.NewGuid(),
                Name = "Demo Fleet Company",
                Slug = "demo-fleet",
                ContactEmail = "info@demofleet.com",
                DefaultLanguage = "en",
                DefaultTimezone = "UTC",
                DefaultCurrency = "USD",
                Status = EntityStatus.Active
            };
            db.Companies.Add(demoCompany);

            var adminRole = new Role
            {
                Id = Guid.NewGuid(), Name = "Company Admin", Description = "Company administrator",
                CompanyId = demoCompany.Id, IsSystemRole = true, Status = EntityStatus.Active
            };
            var fleetManagerRole = new Role
            {
                Id = Guid.NewGuid(), Name = "Fleet Manager", Description = "Fleet operations manager",
                CompanyId = demoCompany.Id, Status = EntityStatus.Active
            };
            db.Roles.AddRange(adminRole, fleetManagerRole);

            var demoAdmin = new User
            {
                Id = Guid.NewGuid(), Email = "admin@demofleet.com", NormalizedEmail = "ADMIN@DEMOFLEET.COM",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                FirstName = "Demo", LastName = "Admin", CompanyId = demoCompany.Id,
                Status = EntityStatus.Active, EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString()
            };
            db.Users.Add(demoAdmin);
            db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = demoAdmin.Id, RoleId = adminRole.Id, TenantId = demoCompany.Id });
        }

        await db.SaveChangesAsync();
    }
}
