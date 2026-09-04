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

        // ── Legacy migrations FIRST (before module/permission creation so the
        //    canonical loops below never collide with renamed rows) ─────────────

        // Legacy '.edit' codes → '.update' (rename in place so role grants survive)
        var legacyEditPerms = await db.Permissions
            .Where(p => !p.IsDeleted && p.Code.EndsWith(".edit"))
            .ToListAsync();
        if (legacyEditPerms.Count > 0)
        {
            foreach (var p in legacyEditPerms)
            {
                p.Code = p.Code.Replace(".edit", ".update");
                p.Name = p.Name.Replace("Edit ", "Update ");
            }
            await db.SaveChangesAsync();
        }

        // Legacy 'configuration' catch-all gated three distinct pages (Modules,
        // Localization, Settings). Rename it to 'settings' IN PLACE (FKs unchanged,
        // role grants survive); 'module'/'localization'/'platform' are added below.
        var legacyModuleRow = await db.Modules
            .FirstOrDefaultAsync(m => m.Code == "configuration" && !m.IsDeleted);
        if (legacyModuleRow != null)
        {
            legacyModuleRow.Code = "settings";
            legacyModuleRow.Name = "Settings";
            legacyModuleRow.Description = "Company settings and preferences";
            legacyModuleRow.Icon = "settings";
            legacyModuleRow.Route = "/settings";
            legacyModuleRow.IsCore = false;
            legacyModuleRow.DisplayOrder = 13;
            await db.SaveChangesAsync();
        }

        var legacyConfigPerms = await db.Permissions.Where(p => p.Module == "configuration" && !p.IsDeleted).ToListAsync();
        if (legacyConfigPerms.Count > 0)
        {
            foreach (var p in legacyConfigPerms)
            {
                var action = p.Code.Split('.').Last();
                p.Code = $"settings.{action}";
                p.Module = "settings";
                p.Name = $"{action} Settings";
            }
            await db.SaveChangesAsync();
        }

        // ── Modules — driven by the canonical PageRegistry ───────────────────
        // A Module is the TOP-LEVEL GROUPING entity (dashboard / fleet operations /
        // organization & access / platform administration). Pages belong to modules
        // via the registry; packages grant whole modules to companies. This block
        // is idempotent and also performs the legacy migration from the era when
        // every page had its own Module row (vehicle, driver, …) and duplicates
        // could exist: page-level rows are merged into their module and retired.
        var liveModules = await db.Modules.Where(m => !m.IsDeleted).ToListAsync();

        // 1) Retire duplicate rows that share a code (keep the first, move children).
        foreach (var dupGroup in liveModules.GroupBy(m => m.Code).Where(g => g.Count() > 1))
        {
            var keeper = dupGroup.OrderBy(m => m.CreatedAt).First();
            foreach (var dup in dupGroup.Where(m => m.Id != keeper.Id))
            {
                await MigrateModuleLinksAsync(db, dup.Id, keeper.Id);
                dup.IsDeleted = true;
                dup.DeletedAt = DateTime.UtcNow;
                Console.WriteLine($"[Seed] Module dedupe: merged duplicate '{dup.Code}' (id {dup.Id}) into id {keeper.Id}");
            }
        }
        await db.SaveChangesAsync();

        // Build maps from the deduplicated rows only.
        var moduleById = liveModules.Where(m => !m.IsDeleted).ToDictionary(m => m.Id);
        var moduleByCode = liveModules.Where(m => !m.IsDeleted && m.Code != null).ToDictionary(m => m.Code, m => m.Id);

        // 2) Upsert the canonical group modules.
        foreach (var def in PageRegistry.Modules)
        {
            if (moduleByCode.TryGetValue(def.Code, out var existingId) && moduleById.TryGetValue(existingId, out var existing))
            {
                existing.Name = def.Label;
                existing.Description = def.Description;
                existing.Icon = def.Icon;
                existing.IsCore = def.IsCore;
                existing.DisplayOrder = def.Order;
                existing.Route = null;
            }
            else
            {
                var module = new Module
                {
                    Id = Guid.NewGuid(),
                    Code = def.Code,
                    Name = def.Label,
                    Description = def.Description,
                    Icon = def.Icon,
                    IsCore = def.IsCore,
                    DisplayOrder = def.Order,
                    Status = EntityStatus.Active
                };
                db.Modules.Add(module);
                moduleById[module.Id] = module;
                moduleByCode[def.Code] = module.Id;
            }
        }
        await db.SaveChangesAsync();

        // 3) Legacy migration: page-level module rows (and any other row that is not
        //    a canonical group) collapse into their owning module. Per-company
        //    ModuleConfiguration grants follow the merge so no access is lost.
        var groupCodes = PageRegistry.Modules.Select(x => x.Code).ToHashSet();
        var pageCodes = PageRegistry.All.Select(p => p.Key).ToHashSet();
        var migratedOrRetired = 0;
        foreach (var m in await db.Modules.Where(m => !m.IsDeleted).ToListAsync())
        {
            if (groupCodes.Contains(m.Code)) continue;
            var target = pageCodes.Contains(m.Code) ? PageRegistry.ModuleOfPage(m.Code) : null;
            if (target == null)
            {
                // Legacy/unknown rows (e.g. "vehicle-management") — retire them.
                m.IsDeleted = true;
                m.DeletedAt = DateTime.UtcNow;
                migratedOrRetired++;
                Console.WriteLine($"[Seed] Module drift: retired unregistered module '{m.Code}' ({m.Name})");
                continue;
            }
            if (!moduleByCode.TryGetValue(target, out var targetId)) continue;
            await MigrateModuleLinksAsync(db, m.Id, targetId);
            m.IsDeleted = true;
            m.DeletedAt = DateTime.UtcNow;
            migratedOrRetired++;
            Console.WriteLine($"[Seed] Module migration: page-row '{m.Code}' merged into module '{target}'");
        }
        if (migratedOrRetired > 0)
        {
            await db.SaveChangesAsync();
        }

        // ── Pages — DB rows seeded from the canonical PageRegistry ──────────
        // Page rows are the runtime state SuperAdmin manages (status, order,
        // planned flags, names). The seed only creates missing rows and fills
        // unset presentation fields — it never overwrites a SuperAdmin's edits.
        // Registry-known pages that were soft-deleted are restored (they are
        // canonical); user-created (non-registry) pages are left untouched.
        var dbPagesAll = await db.Pages.ToListAsync();
        var livePages = dbPagesAll.Where(p => !p.IsDeleted).ToList();
        var pageByKey = livePages
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.CreatedAt).First());
        var seededPages = 0;
        foreach (var def in PageRegistry.All)
        {
            if (!moduleByCode.TryGetValue(def.Module, out var moduleId) || !moduleById.ContainsKey(moduleId))
                continue;
            if (pageByKey.TryGetValue(def.Key, out var existing))
            {
                // Re-home if the registry moved it; fill unset presentation fields.
                if (existing.ModuleId != moduleId) existing.ModuleId = moduleId;
                existing.Route ??= def.Route;
                existing.Icon ??= def.Icon;
                existing.Description ??= def.Description;
            }
            else
            {
                var resurrect = dbPagesAll.FirstOrDefault(p => p.IsDeleted && p.Key == def.Key);
                if (resurrect != null)
                {
                    resurrect.IsDeleted = false;
                    resurrect.DeletedAt = null;
                    resurrect.ModuleId = moduleId;
                    resurrect.Name = def.Label;
                    resurrect.Route ??= def.Route;
                    resurrect.Icon ??= def.Icon;
                    resurrect.Planned = def.Planned;
                    resurrect.IsCore = def.IsCore;
                    resurrect.Nav = def.Nav;
                    resurrect.AdminOnly = def.AdminOnly;
                    resurrect.Description ??= def.Description;
                    pageByKey[def.Key] = resurrect;
                }
                else
                {
                    db.Pages.Add(new Page
                    {
                        Id = Guid.NewGuid(),
                        ModuleId = moduleId,
                        Key = def.Key,
                        Name = def.Label,
                        Route = def.Route,
                        Icon = def.Icon,
                        Nav = def.Nav,
                        AdminOnly = def.AdminOnly,
                        Planned = def.Planned,
                        IsCore = def.IsCore,
                        Status = EntityStatus.Active,
                        DisplayOrder = def.Order,
                        Description = def.Description
                    });
                    seededPages++;
                }
            }
        }
        if (seededPages > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Seeded {seededPages} page rows from PageRegistry");
        }

        // Permissions — exactly 6 actions (view/create/update/delete/export/import) per registered page.
        var existingPermCodes = (await db.Permissions.Where(p => !p.IsDeleted).Select(p => p.Code).ToListAsync()).ToHashSet();
        var order = existingPermCodes.Count;
        var newPerms = new List<Permission>();
        foreach (var def in PageRegistry.All)
        {
            foreach (var action in PageRegistry.Actions)
            {
                var code = $"{def.Key}.{action}";
                if (existingPermCodes.Contains(code)) continue;
                order++;
                newPerms.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Name = $"{action} {def.Label}",
                    Module = def.Key,
                    Action = action switch
                    {
                        "view" => PermissionAction.Read,
                        "create" => PermissionAction.Create,
                        "update" => PermissionAction.Update,
                        "delete" => PermissionAction.Delete,
                        "export" => PermissionAction.Export,
                        "import" => PermissionAction.Import,
                        _ => PermissionAction.Read
                    },
                    Status = EntityStatus.Active,
                    DisplayOrder = order
                });
            }
        }
        if (newPerms.Count > 0)
        {
            db.Permissions.AddRange(newPerms);
            await db.SaveChangesAsync();
        }

        // ── Drift check: permissions whose page no longer exists ─────────────
        // Permission rows follow LIVE pages (registry-known OR SuperAdmin-created
        // via the admin API), not just the static registry — otherwise a page
        // created through CRUD would lose its 6 permissions on the next restart.
        var livePageKeys = (await db.Pages.AsNoTracking().Where(p => !p.IsDeleted).Select(p => p.Key).ToListAsync())
            .ToHashSet();

        var unknownPerms = await db.Permissions.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new { p.Id, p.Code, p.Module })
            .ToListAsync();
        var orphans = unknownPerms.Where(p => !livePageKeys.Contains(p.Module)).ToList();
        if (orphans.Count > 0)
        {
            var orphanIds = orphans.Select(o => o.Id).ToHashSet();
            var orphanRps = await db.RolePermissions.Where(rp => orphanIds.Contains(rp.PermissionId) && !rp.IsDeleted).ToListAsync();
            db.RolePermissions.RemoveRange(orphanRps);
            await db.SaveChangesAsync();

            var permsToDelete = await db.Permissions.Where(p => orphanIds.Contains(p.Id)).ToListAsync();
            foreach (var p in permsToDelete)
            {
                p.IsDeleted = true;
                p.DeletedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Drift check: soft-deleted {orphans.Count} permission rows not in PageRegistry: {string.Join(", ", orphans.Select(o => o.Code))}");
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
            await db.SaveChangesAsync();
        }

        // ── Package → Modules: seed module grants for packages missing them ──
        // A package grants whole modules; a company's accessible modules are exactly
        // the modules of its package (see PermissionService). Seed defaults by tier;
        // the Packages UI lets SuperAdmin pick modules explicitly afterwards.
        var defaultModuleCodesByName = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["basic"] = new[] { "dashboard", "fleet" },
            ["professional"] = new[] { "dashboard", "fleet", "organization" },
            ["enterprise"] = new[] { "dashboard", "fleet", "organization", "platform" },
            ["starter"] = new[] { "dashboard" }
        };
        foreach (var pkg in await db.Packages.Where(p => !p.IsDeleted).ToListAsync())
        {
            var hasGrants = await db.PackageModules.AnyAsync(pm => pm.PackageId == pkg.Id && !pm.IsDeleted);
            if (hasGrants) continue;

            var moduleCodes = defaultModuleCodesByName.TryGetValue(pkg.Name.Trim(), out var codes)
                ? codes
                : new[] { "dashboard" };
            var moduleIds = await db.Modules
                .Where(m => moduleCodes.Contains(m.Code) && !m.IsDeleted)
                .Select(m => m.Id)
                .ToListAsync();
            foreach (var mid in moduleIds)
            {
                db.PackageModules.Add(new PackageModule
                {
                    Id = Guid.NewGuid(),
                    PackageId = pkg.Id,
                    ModuleId = mid,
                    TenantId = pkg.TenantId
                });
            }
        }
        await db.SaveChangesAsync();

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

            // Save users, roles, companies first so we can query permissions
            await db.SaveChangesAsync();

            // Assign ALL permissions to Company Admin role
            var allPermissions = await db.Permissions.Where(p => !p.IsDeleted).ToListAsync();
            foreach (var perm in allPermissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = adminRole.Id,
                    PermissionId = perm.Id,
                    TenantId = demoCompany.Id
                });
            }

            // Also assign vehicle + driver + client + trip + alert + fuel + maintenance + report permissions to Fleet Manager
            var fmPermissions = allPermissions.Where(p =>
                p.Module is "vehicle" or "driver" or "trip" or "geofence" or "alert" or "fuel" or "maintenance" or "report" or "client");
            foreach (var perm in fmPermissions)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    RoleId = fleetManagerRole.Id,
                    PermissionId = perm.Id,
                    TenantId = demoCompany.Id
                });
            }

            // Persist the demo role grants NOW: the idempotent "every startup"
            // grant blocks below query the database for existing grants, and if
            // these tracked rows are unsaved they are invisible to that query —
            // the same (RoleId, PermissionId) pairs would be re-added and the
            // unique index would reject the duplicate on SaveChanges.
            await db.SaveChangesAsync();
        }

        // Company module access is NOT stored per company any more — it is a pure
        // function of the company's package (company.PackageId → package modules).
        // The legacy ModuleConfigurations rows below are left untouched only as a
        // fallback for companies that predate the package model; no new rows are
        // created here.
        // (Remove this comment once every company has an assigned package.)

        // ── Idempotent: assign ALL permissions to Company Admin roles ──
        // This runs every startup to handle cases where permissions were wiped
        var companyAdminRoles = await db.Roles
            .Where(r => r.Name == "Company Admin" && r.IsSystemRole && !r.IsDeleted && r.Status == EntityStatus.Active)
            .ToListAsync();
        var allPerms = await db.Permissions.Where(p => !p.IsDeleted).ToListAsync();
        foreach (var role in companyAdminRoles)
        {
            var existingPermIds = await db.RolePermissions
                .Where(rp => rp.RoleId == role.Id && !rp.IsDeleted)
                .Select(rp => rp.PermissionId)
                .ToListAsync();
            var missingPerms = allPerms.Where(p => !existingPermIds.Contains(p.Id)).ToList();
            if (missingPerms.Count > 0)
            {
                foreach (var perm in missingPerms)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id,
                        TenantId = role.CompanyId
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        // ── Idempotent: assign fleet permissions to Fleet Manager roles ──
        var fleetManagerRoles = await db.Roles
            .Where(r => r.Name == "Fleet Manager" && !r.IsDeleted && r.Status == EntityStatus.Active)
            .ToListAsync();
        var fleetModules = new[] { "vehicle", "device", "driver", "trip", "geofence", "alert", "fuel", "maintenance", "report", "client" };
        foreach (var role in fleetManagerRoles)
        {
            var existingFmPermIds = await db.RolePermissions
                .Where(rp => rp.RoleId == role.Id && !rp.IsDeleted)
                .Select(rp => rp.PermissionId)
                .ToListAsync();
            var missingFmPerms = allPerms.Where(p => fleetModules.Contains(p.Module) && !existingFmPermIds.Contains(p.Id)).ToList();
            if (missingFmPerms.Count > 0)
            {
                foreach (var perm in missingFmPerms)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = perm.Id,
                        TenantId = role.CompanyId
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        // Ensure lakshya@gmail.com test user exists in Demo Fleet Company
        if (!await db.Users.AnyAsync(u => u.Email == "lakshya@gmail.com" && !u.IsDeleted))
        {
            var demoCo = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "demo-fleet");
            if (demoCo != null)
            {
                var testUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = "lakshya@gmail.com",
                    NormalizedEmail = "LAKSHYA@GMAIL.COM",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
                    FirstName = "Lakshya",
                    LastName = "Grover",
                    CompanyId = demoCo.Id,
                    Status = EntityStatus.Active,
                    EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString()
                };
                db.Users.Add(testUser);

                // Assign the Company Admin role
                var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Company Admin" && r.CompanyId == demoCo.Id && !r.IsDeleted);
                if (adminRole != null)
                {
                    db.UserRoles.Add(new UserRole
                    {
                        Id = Guid.NewGuid(),
                        UserId = testUser.Id,
                        RoleId = adminRole.Id,
                        TenantId = demoCo.Id
                    });
                }
                await db.SaveChangesAsync();
            }
        }

        // Demo Vehicles & Drivers (in platform company)
        if (!await db.Vehicles.AnyAsync())
        {
            // Get the platform company
            var platformCompany = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "platform");
            if (platformCompany != null)
            {
                var drivers = new List<Driver>();
                var driverData = new[]
                {
                    ("EMP-001", "Raj", "Sharma", "+91-98765-43210", "raj@demo.com", "DL-2024-001", DriverStatus.Active, 92m, 88m),
                    ("EMP-002", "Priya", "Patel", "+91-98765-43211", "priya@demo.com", "DL-2024-002", DriverStatus.Active, 87m, 91m),
                    ("EMP-003", "Ahmed", "Khan", "+971-50-123-4567", "ahmed@demo.com", "DL-2024-003", DriverStatus.Active, 95m, 90m),
                    ("EMP-004", "Maria", "Garcia", "+1-555-0101", "maria@demo.com", "DL-2024-004", DriverStatus.OnTrip, 78m, 82m),
                    ("EMP-005", "James", "Wilson", "+44-7911-123456", "james@demo.com", "DL-2024-005", DriverStatus.Active, 89m, 85m),
                    ("EMP-006", "Fatima", "Ali", "+971-55-987-6543", "fatima@demo.com", "DL-2024-006", DriverStatus.OffDuty, 83m, 80m),
                    ("EMP-007", "Chen", "Wei", "+86-138-0001-2345", "chen@demo.com", "DL-2024-007", DriverStatus.Active, 91m, 87m),
                    ("EMP-008", "Sarah", "Johnson", "+1-555-0202", "sarah@demo.com", "DL-2024-008", DriverStatus.Active, 94m, 92m),
                };
                foreach (var (empId, first, last, phone, email, lic, status, safety, behaviour) in driverData)
                {
                    var d = new Driver
                    {
                        Id = Guid.NewGuid(), EmployeeId = empId, FirstName = first, LastName = last,
                        PhoneNumber = phone, Email = email, LicenseNumber = lic,
                        LicenseExpiry = DateTime.UtcNow.AddYears(2), CompanyId = platformCompany.Id,
                        Status = status, SafetyScore = safety, BehaviourScore = behaviour
                    };
                    drivers.Add(d);
                }
                db.Drivers.AddRange(drivers);
                await db.SaveChangesAsync();

                var vehicleData = new[]
                {
                    ("MH-12-AB-1234", "Toyota Hilux", "Truck", "Toyota", "Hilux", 2023, FuelType.Diesel, VehicleStatus.Active, 0),
                    ("MH-12-CD-5678", "Tata Ace", "Mini Truck", "Tata", "Ace Gold", 2022, FuelType.Diesel, VehicleStatus.Active, 1),
                    ("DL-01-EF-9012", "Mahindra Bolero", "SUV", "Mahindra", "Bolero Pickup", 2023, FuelType.Diesel, VehicleStatus.Active, 2),
                    ("KA-05-GH-3456", "Eicher Pro", "Heavy Truck", "Eicher", "Pro 2049", 2021, FuelType.Diesel, VehicleStatus.Active, 3),
                    ("GJ-06-IJ-7890", "Ashok Leyland", "Bus", "Ashok Leyland", "Viking", 2022, FuelType.Diesel, VehicleStatus.InMaintenance, -1),
                    ("TN-09-KL-1122", "Tata Nexon EV", "Car", "Tata", "Nexon EV", 2024, FuelType.Electric, VehicleStatus.Active, 4),
                    ("UP-32-MN-3344", "Force Traveller", "Van", "Force", "Traveller 26", 2023, FuelType.Diesel, VehicleStatus.Active, 5),
                    ("RJ-14-OP-5566", "Maruti Eeco", "Van", "Maruti Suzuki", "Eeco", 2022, FuelType.Petrol, VehicleStatus.Active, -1),
                    ("AP-09-QR-7788", "Volvo FM", "Heavy Truck", "Volvo", "FM 440", 2023, FuelType.Diesel, VehicleStatus.Active, 6),
                    ("MH-01-ST-9900", "Tata Tigor EV", "Car", "Tata", "Tigor EV", 2024, FuelType.Electric, VehicleStatus.Active, 7),
                    ("KL-07-UV-2233", "Isuzu D-Max", "Pickup", "Isuzu", "D-Max V-Cross", 2023, FuelType.Diesel, VehicleStatus.Active, -1),
                    ("MP-09-WX-4455", "Mahindra Treo", "Electric Auto", "Mahindra", "Treo Zor", 2024, FuelType.Electric, VehicleStatus.Inactive, -1),
                };
                foreach (var (reg, name, type, make, model, year, fuel, status, driverIdx) in vehicleData)
                {
                    var v = new Vehicle
                    {
                        Id = Guid.NewGuid(), RegistrationNumber = reg, Name = name,
                        VehicleType = type, Make = make, Model = model, Year = year,
                        FuelType = fuel, CompanyId = platformCompany.Id, Status = status,
                        LastLatitude = 19.0 + Random.Shared.NextDouble() * 10,
                        LastLongitude = 73.0 + Random.Shared.NextDouble() * 10,
                        LastSpeed = status == VehicleStatus.Active ? Random.Shared.Next(0, 80) : 0,
                        IgnitionStatus = status == VehicleStatus.Active,
                        DriverId = driverIdx >= 0 && driverIdx < drivers.Count ? drivers[driverIdx].Id : null
                    };
                    db.Vehicles.Add(v);
                }
                await db.SaveChangesAsync();
            }

            // Demo Company: Clients, Drivers, Vehicles
            var demoCo = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "demo-fleet");
            if (demoCo != null)
            {
                var clients = new List<Client>();
                var cData = new[]
                {
                    ("Reliance Industries", "Amit Shah", "amit@reliance.com", "+91-22-1234-5678", "Mumbai"),
                    ("Tata Steel", "Vikram Singh", "vikram@tatasteel.com", "+91-657-234-5678", "Jamshedpur"),
                    ("Wipro Logistics", "Deepak Mehta", "deepak@wipro.com", "+91-80-5678-1234", "Bangalore"),
                    ("Infosys Supply Chain", "Neha Gupta", "neha@infosys.com", "+91-80-9876-5432", "Bangalore"),
                    ("Adani Ports", "Rajesh Kumar", "rajesh@adani.com", "+91-79-2345-6789", "Ahmedabad"),
                };
                foreach (var (name, contact, email, phone, city) in cData)
                    clients.Add(new Client { Id = Guid.NewGuid(), Name = name, ContactPerson = contact, ContactEmail = email, ContactPhone = phone, Address = $"{city} Business District", CompanyId = demoCo.Id, Status = EntityStatus.Active });
                db.Clients.AddRange(clients);
                await db.SaveChangesAsync();

                var demoDrivers = new List<Driver>();
                var dData = new[]
                {
                    ("DF-001", "Vikram", "Rathore", "+91-99887-66554", "DL-DF-001", DriverStatus.Active, 90m, 86m),
                    ("DF-002", "Suresh", "Reddy", "+91-99887-66555", "DL-DF-002", DriverStatus.Active, 88m, 84m),
                    ("DF-003", "Anita", "Desai", "+91-99887-66556", "DL-DF-003", DriverStatus.OnTrip, 93m, 91m),
                    ("DF-004", "Mohammed", "Irfan", "+91-99887-66557", "DL-DF-004", DriverStatus.Active, 85m, 80m),
                    ("DF-005", "Pooja", "Nair", "+91-99887-66558", "DL-DF-005", DriverStatus.OffDuty, 79m, 76m),
                    ("DF-006", "Karan", "Malhotra", "+91-99887-66559", "DL-DF-006", DriverStatus.Active, 91m, 88m),
                };
                foreach (var (empId, first, last, phone, lic, status, safety, behaviour) in dData)
                    demoDrivers.Add(new Driver { Id = Guid.NewGuid(), EmployeeId = empId, FirstName = first, LastName = last, PhoneNumber = phone, LicenseNumber = lic, LicenseExpiry = DateTime.UtcNow.AddYears(2), CompanyId = demoCo.Id, Status = status, SafetyScore = safety, BehaviourScore = behaviour });
                db.Drivers.AddRange(demoDrivers);
                await db.SaveChangesAsync();

                var vd = new[]
                {
                    ("GJ-01-XX-1001", "Tata Prima", "Heavy Truck", "Tata", "Prima 4040.K", 2023, FuelType.Diesel, VehicleStatus.Active, 0, 0),
                    ("GJ-01-XX-1002", "Ashok Leyland Dost", "Mini Truck", "Ashok Leyland", "Dost Plus", 2022, FuelType.Diesel, VehicleStatus.Active, 1, 1),
                    ("GJ-01-XX-1003", "Mahindra Blazo", "Heavy Truck", "Mahindra", "Blazo 28", 2023, FuelType.Diesel, VehicleStatus.Active, 2, 2),
                    ("GJ-01-XX-1004", "Eicher Skyline", "Bus", "Eicher", "Skyline 2045", 2022, FuelType.Diesel, VehicleStatus.InMaintenance, -1, 3),
                    ("GJ-01-XX-1005", "Tata Altroz EV", "Car", "Tata", "Altroz EV", 2024, FuelType.Electric, VehicleStatus.Active, 3, 4),
                    ("GJ-01-XX-1006", "Force Gurkha", "SUV", "Force", "Gurkha 5 Door", 2023, FuelType.Diesel, VehicleStatus.Active, 4, -1),
                    ("GJ-01-XX-1007", "Tata Ace EV", "Mini Truck", "Tata", "Ace EV", 2024, FuelType.Electric, VehicleStatus.Active, 5, -1),
                    ("GJ-01-XX-1008", "Mahindra Treo Zor", "Electric Auto", "Mahindra", "Treo Zor", 2024, FuelType.Electric, VehicleStatus.Inactive, -1, -1),
                };
                var colors = new[] { "White", "Blue", "Red", "Silver", "Green" };
                foreach (var (reg, name, type, make, model, year, fuel, status, di, ci) in vd)
                    db.Vehicles.Add(new Vehicle
                    {
                        Id = Guid.NewGuid(), RegistrationNumber = reg, Name = name, VehicleType = type, Make = make, Model = model, Year = year,
                        FuelType = fuel, CompanyId = demoCo.Id, Status = status, Color = colors[Random.Shared.Next(5)],
                        FuelTankCapacity = Random.Shared.Next(40, 120),
                        LastLatitude = 23.0 + Random.Shared.NextDouble() * 2, LastLongitude = 72.0 + Random.Shared.NextDouble() * 2,
                        LastSpeed = status == VehicleStatus.Active ? Random.Shared.Next(0, 80) : 0, IgnitionStatus = status == VehicleStatus.Active,
                        OdometerReading = Random.Shared.Next(5000, 80000), EngineHours = Random.Shared.Next(200, 3000),
                        DeviceImei = $"8600{Random.Shared.Next(100000000, 999999999)}", DeviceType = "GPS Tracker",
                        DriverId = di >= 0 && di < demoDrivers.Count ? demoDrivers[di].Id : null,
                        ClientId = ci >= 0 && ci < clients.Count ? clients[ci].Id : null
                    });
                await db.SaveChangesAsync();
            }
        }

        // ── Demo Geofences ──
        if (!await db.Geofences.AnyAsync())
        {
            var platformCompany = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "platform");
            var demoCo = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "demo-fleet");

            if (platformCompany != null)
            {
                var geofences = new List<Geofence>();
                geofences.Add(new Geofence
                {
                    Id = Guid.NewGuid(), Name = "Mumbai Warehouse", Description = "Main distribution center in Mumbai",
                    Type = GeofenceType.Rectangle, Status = EntityStatus.Active,
                    Coordinates = "[{\"lat\":19.05,\"lng\":72.85},{\"lat\":19.08,\"lng\":72.88},{\"lat\":19.03,\"lng\":72.90},{\"lat\":19.01,\"lng\":72.86}]",
                    CenterLatitude = 19.05, CenterLongitude = 72.87, FillColor = "#4CAF5033", BorderColor = "#4CAF50", BorderWidth = 2,
                    ViolationCount = 12, LastViolationAt = DateTime.UtcNow.AddDays(-2),
                    CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                geofences.Add(new Geofence
                {
                    Id = Guid.NewGuid(), Name = "Delhi NCR Hub", Description = "Northern region coverage area",
                    Type = GeofenceType.Circle, Status = EntityStatus.Active,
                    Coordinates = "[]", CenterLatitude = 28.61, CenterLongitude = 77.21, Radius = 15000,
                    FillColor = "#2196F333", BorderColor = "#2196F3", BorderWidth = 2,
                    ViolationCount = 5, LastViolationAt = DateTime.UtcNow.AddDays(-5),
                    CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                geofences.Add(new Geofence
                {
                    Id = Guid.NewGuid(), Name = "Bangalore Tech Park", Description = "Restricted area - IT corridor",
                    Type = GeofenceType.Polygon, Status = EntityStatus.Active,
                    Coordinates = "[{\"lat\":12.91,\"lng\":77.64},{\"lat\":12.94,\"lng\":77.67},{\"lat\":12.92,\"lng\":77.70},{\"lat\":12.89,\"lng\":77.68}]",
                    CenterLatitude = 12.92, CenterLongitude = 77.67, FillColor = "#FF980033", BorderColor = "#FF9800", BorderWidth = 3,
                    ViolationCount = 23, LastViolationAt = DateTime.UtcNow.AddDays(-1),
                    CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                db.Geofences.AddRange(geofences);
            }

            if (demoCo != null)
            {
                var geofences = new List<Geofence>();
                geofences.Add(new Geofence
                {
                    Id = Guid.NewGuid(), Name = "Ahmedabad Depot", Description = "Primary logistics depot",
                    Type = GeofenceType.Circle, Status = EntityStatus.Active,
                    Coordinates = "[]",                    CenterLatitude = 23.02, CenterLongitude = 72.57, Radius = 8000,
                    FillColor = "#9C27B033", BorderColor = "#9C27B0", BorderWidth = 2,
                    ViolationCount = 8, LastViolationAt = DateTime.UtcNow.AddDays(-3),
                    CompanyId = demoCo.Id, TenantId = demoCo.Id
                });
                geofences.Add(new Geofence
                {
                    Id = Guid.NewGuid(), Name = "Surat Industrial Zone", Description = "Manufacturing area boundary",
                    Type = GeofenceType.Rectangle, Status = EntityStatus.Active,
                    Coordinates = "[{\"lat\":21.15,\"lng\":72.80},{\"lat\":21.20,\"lng\":72.85},{\"lat\":21.13,\"lng\":72.87},{\"lat\":21.10,\"lng\":72.82}]",
                    CenterLatitude = 21.17, CenterLongitude = 72.83, FillColor = "#F4433633", BorderColor = "#F44336", BorderWidth = 2,
                    ViolationCount = 3, LastViolationAt = DateTime.UtcNow.AddDays(-7),
                    CompanyId = demoCo.Id, TenantId = demoCo.Id
                });
                geofences.Add(new Geofence
                {
                    Id = Guid.NewGuid(), Name = "Rajkot Customer Zone", Description = "Customer delivery area",
                    Type = GeofenceType.Polygon, Status = EntityStatus.Active,
                    Coordinates = "[{\"lat\":22.29,\"lng\":70.78},{\"lat\":22.32,\"lng\":70.82},{\"lat\":22.28,\"lng\":70.84},{\"lat\":22.26,\"lng\":70.80}]",
                    CenterLatitude = 22.30, CenterLongitude = 70.81, FillColor = "#00BCD433", BorderColor = "#00BCD4", BorderWidth = 2,
                    ViolationCount = 15, LastViolationAt = DateTime.UtcNow.AddDays(-1),
                    CompanyId = demoCo.Id, TenantId = demoCo.Id
                });
                db.Geofences.AddRange(geofences);
            }

            await db.SaveChangesAsync();

            // Assign vehicles to geofences
            var allGeofences = await db.Geofences.Where(g => !g.IsDeleted).Include(g => g.Company).ToListAsync();
            foreach (var gf in allGeofences)
            {
                var vehicles = await db.Vehicles.Where(v => v.CompanyId == gf.CompanyId && !v.IsDeleted).Take(3).ToListAsync();
                var drivers = await db.Drivers.Where(d => d.CompanyId == gf.CompanyId && !d.IsDeleted).Take(3).ToListAsync();
                for (int i = 0; i < vehicles.Count; i++)
                {
                    var alreadyExists = await db.VehicleGeofences.AnyAsync(vg => vg.GeofenceId == gf.Id && vg.VehicleId == vehicles[i].Id && !vg.IsDeleted);
                    if (!alreadyExists)
                    {
                        db.VehicleGeofences.Add(new VehicleGeofence
                        {
                            Id = Guid.NewGuid(), GeofenceId = gf.Id, VehicleId = vehicles[i].Id,
                            DriverId = i < drivers.Count ? drivers[i].Id : null,
                            AlertOnEntry = true, AlertOnExit = true, AlertOnDwell = i == 0, DwellTimeMinutes = i == 0 ? 15 : null,
                            TenantId = gf.TenantId
                        });
                    }
                }
            }
            await db.SaveChangesAsync();
        }

        // ── Demo Routes ──
        if (!await db.Routes.AnyAsync())
        {
            var platformCompany = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "platform");
            var demoCo = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "demo-fleet");

            if (platformCompany != null)
            {
                var platformVehicles = await db.Vehicles.Where(v => v.CompanyId == platformCompany.Id && !v.IsDeleted).Take(4).ToListAsync();
                var platformDrivers = await db.Drivers.Where(d => d.CompanyId == platformCompany.Id && !d.IsDeleted).Take(4).ToListAsync();

                var routes = new List<Route>();
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Mumbai-Pune Express", Description = "Highway route via expressway",
                    Type = RouteType.Optimized, Status = RouteStatus.Active, IsOptimized = true,
                    OriginName = "Mumbai Warehouse", OriginLatitude = 19.05, OriginLongitude = 72.85,
                    DestinationName = "Pune Distribution Center", DestinationLatitude = 18.52, DestinationLongitude = 73.85,
                    Waypoints = "[{\"name\":\"Thane\",\"lat\":19.12,\"lng\":72.97},{\"name\":\"Lonavala\",\"lat\":18.75,\"lng\":73.40}]",
                    TotalDistance = 148, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromHours(2.5),
                    EstimatedFuelCost = 1200, EstimatedTollCost = 350, Currency = "INR", TrafficLevel = 65,
                    Priority = 4, MaxVehicles = 10, CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Delhi-Jaipur Highway", Description = "Daily freight route",
                    Type = RouteType.Standard, Status = RouteStatus.Active, IsOptimized = false,
                    OriginName = "Delhi NCR Hub", OriginLatitude = 28.61, OriginLongitude = 77.21,
                    DestinationName = "Jaipur Depot", DestinationLatitude = 26.91, DestinationLongitude = 75.79,
                    TotalDistance = 281, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromHours(5),
                    EstimatedFuelCost = 2400, EstimatedTollCost = 500, Currency = "INR", TrafficLevel = 40,
                    Priority = 3, MaxVehicles = 15, CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Bangalore City Circuit", Description = "Multi-stop urban delivery route",
                    Type = RouteType.MultiStop, Status = RouteStatus.Draft, IsOptimized = true, IsTemplate = true,
                    OriginName = "Bangalore Tech Park", OriginLatitude = 12.92, OriginLongitude = 77.67,
                    DestinationName = "Whitefield IT Park", DestinationLatitude = 12.97, DestinationLongitude = 77.75,
                    Waypoints = "[{\"name\":\"Koramangala\",\"lat\":12.93,\"lng\":77.62},{\"name\":\"HSR Layout\",\"lat\":12.91,\"lng\":77.64},{\"name\":\"Electronic City\",\"lat\":12.85,\"lng\":77.66}]",
                    TotalDistance = 42, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromHours(3),
                    EstimatedFuelCost = 350, Currency = "INR", TrafficLevel = 80,
                    Priority = 2, MaxVehicles = 5, CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Chennai Port Express", Description = "Container transport route to Chennai port",
                    Type = RouteType.Express, Status = RouteStatus.Completed, IsOptimized = true,
                    OriginName = "Chennai Industrial Area", OriginLatitude = 13.05, OriginLongitude = 80.25,
                    DestinationName = "Chennai Port Trust", DestinationLatitude = 13.08, DestinationLongitude = 80.29,
                    TotalDistance = 18, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromMinutes(45),
                    EstimatedFuelCost = 150, Currency = "INR", TrafficLevel = 55,
                    Priority = 5, MaxVehicles = 3, CompanyId = platformCompany.Id, TenantId = platformCompany.Id
                });
                db.Routes.AddRange(routes);
            }

            if (demoCo != null)
            {
                var demoVehicles = await db.Vehicles.Where(v => v.CompanyId == demoCo.Id && !v.IsDeleted).Take(3).ToListAsync();
                var demoDrivers = await db.Drivers.Where(d => d.CompanyId == demoCo.Id && !d.IsDeleted).Take(3).ToListAsync();

                var routes = new List<Route>();
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Ahmedabad-Surat Corridor", Description = "Daily industrial goods transport",
                    Type = RouteType.Optimized, Status = RouteStatus.Active, IsOptimized = true,
                    OriginName = "Ahmedabad Depot", OriginLatitude = 23.02, OriginLongitude = 72.57,
                    DestinationName = "Surat Industrial Zone", DestinationLatitude = 21.17, DestinationLongitude = 72.83,
                    Waypoints = "[{\"name\":\"Vadodara\",\"lat\":22.31,\"lng\":73.19}]",
                    TotalDistance = 265, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromHours(4.5),
                    EstimatedFuelCost = 2200, EstimatedTollCost = 400, Currency = "INR", TrafficLevel = 35,
                    Priority = 4, MaxVehicles = 8, CompanyId = demoCo.Id, TenantId = demoCo.Id
                });
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Rajkot Local Delivery", Description = "Intra-city delivery circuit",
                    Type = RouteType.MultiStop, Status = RouteStatus.Active, IsOptimized = true,
                    OriginName = "Rajkot Main Depot", OriginLatitude = 22.30, OriginLongitude = 70.80,
                    DestinationName = "Rajkot Main Depot", DestinationLatitude = 22.30, DestinationLongitude = 70.80,
                    Waypoints = "[{\"name\":\"Zone 1\",\"lat\":22.32,\"lng\":70.82},{\"name\":\"Zone 2\",\"lat\":22.28,\"lng\":70.84},{\"name\":\"Zone 3\",\"lat\":22.31,\"lng\":70.78}]",
                    TotalDistance = 35, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromHours(4),
                    EstimatedFuelCost = 300, Currency = "INR", TrafficLevel = 50,
                    Priority = 2, MaxVehicles = 5, CompanyId = demoCo.Id, TenantId = demoCo.Id
                });
                routes.Add(new Route
                {
                    Id = Guid.NewGuid(), Name = "Gujarat State Express", Description = "Long-haul route across Gujarat",
                    Type = RouteType.RoundTrip, Status = RouteStatus.Draft, IsTemplate = true,
                    OriginName = "Ahmedabad", OriginLatitude = 23.02, OriginLongitude = 72.57,
                    DestinationName = "Bhuj", DestinationLatitude = 23.25, DestinationLongitude = 69.67,
                    TotalDistance = 330, DistanceUnit = "km", EstimatedDuration = TimeSpan.FromHours(6),
                    EstimatedFuelCost = 2800, EstimatedTollCost = 250, Currency = "INR", TrafficLevel = 20,
                    Priority = 3, MaxVehicles = 6, CompanyId = demoCo.Id, TenantId = demoCo.Id
                });
                db.Routes.AddRange(routes);
            }

            await db.SaveChangesAsync();

            // Assign vehicles to routes
            var allRoutes = await db.Routes.Where(r => !r.IsDeleted).Include(r => r.Company).ToListAsync();
            foreach (var route in allRoutes)
            {
                var vehicles = await db.Vehicles.Where(v => v.CompanyId == route.CompanyId && !v.IsDeleted).Take(3).ToListAsync();
                var drivers = await db.Drivers.Where(d => d.CompanyId == route.CompanyId && !d.IsDeleted).Take(3).ToListAsync();
                for (int i = 0; i < vehicles.Count; i++)
                {
                    db.RouteVehicles.Add(new RouteVehicle
                    {
                        Id = Guid.NewGuid(), RouteId = route.Id, VehicleId = vehicles[i].Id,
                        DriverId = i < drivers.Count ? drivers[i].Id : null,
                        SequenceOrder = i + 1, AssignedDate = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
                        StartTime = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 10)),
                        ActualDistance = (route.TotalDistance ?? 0) + Random.Shared.Next(-5, 10),
                        TenantId = route.TenantId
                    });
                }
            }
            await db.SaveChangesAsync();
        }

        // ── Company → Package repair (idempotent) ───────────────────────────
        // A company's accessible modules derive ONLY from its assigned package,
        // so a company whose PackageId points at a package that no longer exists
        // (legacy reseed, package deletion, DB restore) would silently lose every
        // module — and its users would be locked out with 0 effective permissions.
        // Re-home such companies: demo-fleet → Professional, platform → Enterprise,
        // anything else dangling → the default package. Never touches companies
        // that already resolve to a live package (their entitlement is explicit).
        var livePackages = await db.Packages.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Status == EntityStatus.Active)
            .Select(p => new { p.Id, p.Name, p.IsDefault })
            .ToListAsync();
        var liveIds = livePackages.Select(p => p.Id).ToHashSet();

        // Fresh databases create the platform + demo companies without a package;
        // they would otherwise get zero modules and be locked out. The canonical
        // seeded companies are assigned their tier defaults here; other companies
        // without a package are left alone (a deliberate package-less draft state).
        var repairCompanies = await db.Companies
            .Where(c => !c.IsDeleted
                && ((c.PackageId == null && (c.Slug == "demo-fleet" || c.Slug == "platform"))
                    || (c.PackageId != null && !liveIds.Contains(c.PackageId.Value))))
            .ToListAsync();
        foreach (var company in repairCompanies)
        {
            var target = (company.Slug, company.Name) switch
            {
                ("demo-fleet", _) => livePackages.FirstOrDefault(p => p.Name == "Professional"),
                ("platform", _) => livePackages.FirstOrDefault(p => p.Name == "Enterprise"),
                _ => livePackages.FirstOrDefault(p => p.IsDefault) ?? livePackages.FirstOrDefault()
            };
            if (target == null) continue;

            // Retire the stale active subscription (kept as history) and open a new one
            var staleSubs = await db.Subscriptions
                .Where(s => s.CompanyId == company.Id && !s.IsDeleted && s.Status == SubscriptionStatus.Active)
                .ToListAsync();
            foreach (var s in staleSubs)
            {
                s.Status = SubscriptionStatus.Canceled;
                s.CanceledAt = DateTime.UtcNow;
            }

            var pkgEntity = await db.Packages.FirstOrDefaultAsync(p => p.Id == target.Id);
            var sub = new Subscription
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                PackageId = target.Id,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.UtcNow.AddMonths(-6),
                EndDate = DateTime.UtcNow.AddMonths(6),
                CurrentPrice = pkgEntity?.Price ?? 0,
                Currency = pkgEntity?.Currency ?? "USD",
                BillingCycle = pkgEntity?.BillingCycle ?? "monthly",
                TenantId = company.Id
            };
            db.Subscriptions.Add(sub);
            company.SubscriptionId = sub.Id;
            company.PackageId = target.Id;

            Console.WriteLine($"[Seed] Repaired company '{company.Name}': re-homed dangling package → '{target.Name}'");
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Moves per-company module links (ModuleConfigurations) and package-module
    /// links from one module row to another during dedupe/merge, so no company or
    /// package loses module access. Duplicate targets are retired rather than kept.
    /// </summary>
    private static async Task MigrateModuleLinksAsync(ApplicationDbContext db, Guid fromModuleId, Guid toModuleId)
    {
        // ModuleConfiguration (company ↔ module)
        var configs = await db.ModuleConfigurations
            .Where(mc => mc.ModuleId == fromModuleId && !mc.IsDeleted)
            .ToListAsync();
        if (configs.Count > 0)
        {
            var occupied = await db.ModuleConfigurations
                .Where(mc => mc.ModuleId == toModuleId && !mc.IsDeleted)
                .Select(mc => mc.CompanyId)
                .ToListAsync();
            var occupiedSet = occupied.ToHashSet();
            foreach (var mc in configs)
            {
                if (occupiedSet.Contains(mc.CompanyId))
                {
                    mc.IsDeleted = true;
                    mc.DeletedAt = DateTime.UtcNow;
                }
                else
                {
                    mc.ModuleId = toModuleId;
                    occupiedSet.Add(mc.CompanyId);
                }
            }
        }

        // PackageModule (package ↔ module)
        var packageModules = await db.PackageModules
            .Where(pm => pm.ModuleId == fromModuleId && !pm.IsDeleted)
            .ToListAsync();
        if (packageModules.Count > 0)
        {
            var occupiedPkgs = await db.PackageModules
                .Where(pm => pm.ModuleId == toModuleId && !pm.IsDeleted)
                .Select(pm => pm.PackageId)
                .ToListAsync();
            var occupiedPkgSet = occupiedPkgs.ToHashSet();
            foreach (var pm in packageModules)
            {
                if (occupiedPkgSet.Contains(pm.PackageId))
                {
                    pm.IsDeleted = true;
                    pm.DeletedAt = DateTime.UtcNow;
                }
                else
                {
                    pm.ModuleId = toModuleId;
                    occupiedPkgSet.Add(pm.PackageId);
                }
            }
        }

        // Persist the moves before the next link batch checks occupancy, so later
        // merges onto the same target module are not treated as duplicates.
        await db.SaveChangesAsync();
    }
}
