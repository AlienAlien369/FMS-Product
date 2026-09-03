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

        // Modules — idempotent: load existing + add missing
        var modules = new Dictionary<string, Module>();
        var existingModuleCodes = (await db.Modules.Where(m => !m.IsDeleted).Select(m => m.Code).ToListAsync()).ToHashSet();
        foreach (var m in await db.Modules.Where(m => !m.IsDeleted).ToListAsync())
            modules[m.Code] = m;

        // Module codes MUST match permission module names (e.g. "vehicle" not "vehicles")
        // so PermissionService.GetEnabledModuleCodesAsync can match permissions to modules.
        var moduleList = new (string Code, string Name, string? Desc, bool IsCore, int Order)[]
        {
            ("vehicle", "Vehicle Management", "Vehicle CRUD and tracking", true, 1),
            ("driver", "Driver Management", "Driver profiles and management", true, 2),
            ("trip", "Trip Management", "Trip planning and execution", true, 3),
            ("geofence", "Geofencing", "Geofence creation and monitoring", false, 4),
            ("route", "Route Management", "Route planning and optimization", false, 5),
            ("alert", "Alerts & Alarms", "Configurable alert system", true, 6),
            ("fuel", "Fuel Monitoring", "Fuel level and consumption tracking", false, 7),
            ("maintenance", "Maintenance", "Preventive and corrective maintenance", false, 8),
            ("client", "Client Management", "Client profiles and management", false, 9),
            ("document", "Document Management", "Document upload and compliance", false, 10),
            ("report", "Reports & Analytics", "Reporting and dashboards", true, 11),
            ("user", "User Management", "User profiles and access control", true, 12),
            ("role", "Role Management", "Role and permission management", true, 13),
            ("company", "Company Management", "Company administration", true, 14),
            ("configuration", "Configuration", "System configuration and settings", true, 15),
            ("subscription", "Subscription Management", "Subscription and billing", false, 16),
            ("package", "Package Management", "Package and plan management", false, 17),
            ("dashboard", "Dashboard", "Analytics and overview dashboard", true, 18),
            ("notification", "Notifications", "Notification system", true, 19),
        };

        var newModules = new List<Module>();
        foreach (var (code, name, desc, isCore, modOrder) in moduleList)
        {
            if (existingModuleCodes.Contains(code)) continue;
            var module = new Module
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = desc,
                IsCore = isCore,
                DisplayOrder = modOrder,
                Status = EntityStatus.Active
            };
            newModules.Add(module);
            modules[code] = module;
        }
        if (newModules.Count > 0)
        {
            db.Modules.AddRange(newModules);
            await db.SaveChangesAsync();
        }

        // Permissions — comprehensive set covering every module × action
        // Always add missing permissions (idempotent — safe to run on existing DBs)
        var existingPermCodes = (await db.Permissions.Where(p => !p.IsDeleted).Select(p => p.Code).ToListAsync()).ToHashSet();
        var permModules = new string[] {
            "vehicle", "driver", "trip", "geofence", "route",
            "alert", "fuel", "maintenance", "client", "document",
            "report", "user", "role", "company", "configuration",
            "subscription", "package", "dashboard", "notification"
        };
        var permActions = new string[] { "view", "create", "edit", "delete", "import", "export", "assign", "track", "immobilize", "approve" };

        var order = existingPermCodes.Count;
        var newPerms = new List<Permission>();
        foreach (var mod in permModules)
        {
            foreach (var action in permActions)
            {
                var code = $"{mod}.{action}";
                if (existingPermCodes.Contains(code)) continue;
                order++;
                newPerms.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Name = $"{action} {mod}",
                    Module = mod,
                    Action = action switch
                    {
                        "view" => PermissionAction.Read,
                        "create" => PermissionAction.Create,
                        "edit" => PermissionAction.Update,
                        "delete" => PermissionAction.Delete,
                        "import" => PermissionAction.Import,
                        "export" => PermissionAction.Export,
                        "assign" => PermissionAction.Assign,
                        "track" => PermissionAction.Execute,
                        "immobilize" => PermissionAction.Manage,
                        "approve" => PermissionAction.Approve,
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
        }

        // Enable all modules for all companies (idempotent — runs every startup)
        var allModules = await db.Modules.Where(m => !m.IsDeleted).ToListAsync();
        var allCompanyIds = await db.Companies.Where(c => !c.IsDeleted).Select(c => c.Id).ToListAsync();
        foreach (var companyId in allCompanyIds)
        {
            var existingMCModuleIds = await db.ModuleConfigurations
                .Where(mc => mc.CompanyId == companyId && !mc.IsDeleted)
                .Select(mc => mc.ModuleId)
                .ToListAsync();
            foreach (var mod in allModules)
            {
                if (existingMCModuleIds.Contains(mod.Id)) continue;
                db.ModuleConfigurations.Add(new ModuleConfiguration
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    ModuleId = mod.Id,
                    Status = EntityStatus.Active,
                    TenantId = companyId
                });
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

        // Subscriptions - assign demo subscription to demo company
        if (!await db.Subscriptions.AnyAsync())
        {
            var demoCompany = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "demo-fleet");
            var basicPackage = await db.Packages.FirstOrDefaultAsync(p => p.Name == "Basic" && !p.IsDeleted);
            if (demoCompany != null && basicPackage != null)
            {
                var sub = new Subscription
                {
                    Id = Guid.NewGuid(),
                    CompanyId = demoCompany.Id,
                    PackageId = basicPackage.Id,
                    Status = SubscriptionStatus.Active,
                    StartDate = DateTime.UtcNow.AddMonths(-6),
                    EndDate = DateTime.UtcNow.AddMonths(6),
                    CurrentPrice = basicPackage.Price,
                    Currency = basicPackage.Currency,
                    BillingCycle = basicPackage.BillingCycle,
                    TenantId = demoCompany.Id
                };
                db.Subscriptions.Add(sub);
                demoCompany.SubscriptionId = sub.Id;
                demoCompany.PackageId = basicPackage.Id;
            }

            // Also give platform company a subscription
            var platformCompany = await db.Companies.FirstOrDefaultAsync(c => c.Slug == "platform");
            var enterprisePackage = await db.Packages.FirstOrDefaultAsync(p => p.Name == "Enterprise" && !p.IsDeleted);
            if (platformCompany != null && enterprisePackage != null)
            {
                var sub = new Subscription
                {
                    Id = Guid.NewGuid(),
                    CompanyId = platformCompany.Id,
                    PackageId = enterprisePackage.Id,
                    Status = SubscriptionStatus.Active,
                    StartDate = DateTime.UtcNow.AddMonths(-12),
                    EndDate = DateTime.UtcNow.AddMonths(12),
                    CurrentPrice = enterprisePackage.Price,
                    Currency = enterprisePackage.Currency,
                    BillingCycle = enterprisePackage.BillingCycle,
                    TenantId = platformCompany.Id
                };
                db.Subscriptions.Add(sub);
                platformCompany.SubscriptionId = sub.Id;
                platformCompany.PackageId = enterprisePackage.Id;
            }
        }

        await db.SaveChangesAsync();
    }
}
