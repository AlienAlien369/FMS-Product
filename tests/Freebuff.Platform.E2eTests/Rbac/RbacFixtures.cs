using Freebuff.Platform.Infrastructure.Data;

namespace Freebuff.Platform.E2eTests.Rbac;

/// <summary>
/// Version-controlled fixture seed for the RBAC/package/module matrix suite.
///
/// Runs as raw SQL against the per-class e2e database (a fresh DB per test
/// class, so fixtures never leak into other suites), idempotently — re-running
/// against an already-seeded DB is a no-op. The seeded world deliberately
/// stresses every authorization boundary:
///
///   Company A  Demo Fleet Company   (Professional = dashboard+fleet+organization, en/USD)
///     Roles:   Company Admin (all perms, seeded), Fleet Manager (seeded),
///              Read Only (view-only on the 10 live pages),
///              Ops Manager (full 6-action on fleet pages)
///     Users:   admin@demofleet.com → Company Admin (seeded)
///              e2e.fleetmanager@demo.test → Fleet Manager
///              e2e.readonly@demo.test → Read Only
///              e2e.ops@demo.test → Ops Manager
///              e2e.multi@demo.test → Read Only + Ops Manager (multi-role union)
///
///   Company B  E2E Basic Co         (Basic = dashboard+fleet only, fr/EUR — the
///                                   localization-override company; organization
///                                   module deliberately NOT in the package)
///     Roles:   Basic Admin  (holds EVERY permission code — the package-gate
///                            trap: role grants exist but the package blocks
///                            organization/platform pages),
///              Basic Viewer (view-only on dashboard+fleet pages)
///     Users:   e2e.admin@basic.test → Basic Admin
///              e2e.viewer@basic.test → Basic Viewer
/// </summary>
public static class RbacFixtures
{
    public const string BasicCompanySlug = "e2e-basic";

    // Passwords match the seed world ("Admin@123") so LoginAsync works.
    public const string Password = "Admin@123";

    public const string FleetManagerEmail = "e2e.fleetmanager@demo.test";
    public const string ReadOnlyEmail = "e2e.readonly@demo.test";
    public const string OpsEmail = "e2e.ops@demo.test";
    public const string MultiRoleEmail = "e2e.multi@demo.test";
    public const string BasicAdminEmail = "e2e.admin@basic.test";
    public const string BasicViewerEmail = "e2e.viewer@basic.test";

    /// <summary>The pages a tenant can actually navigate to (live, non-adminOnly, nav+route).</summary>
    public static readonly string[] LivePageKeys =
    {
        "dashboard", "vehicle", "driver", "geofence", "route",
        "company", "user", "role", "localization", "settings"
    };

    public static readonly string[] FleetCrudPageKeys = { "vehicle", "driver", "geofence", "route" };

    public static async Task SeedAsync(E2eDb db)
    {
        await EnsureCompanyAsync(db);
        await EnsureRolesAsync(db);
        await EnsureUsersAsync(db);
    }

    // ── Company B on the Basic package, fr/EUR override ────────────────────
    private static async Task EnsureCompanyAsync(E2eDb db)
    {
        if (await db.ScalarAsync($"SELECT COUNT(*) FROM \"Companies\" WHERE \"Slug\" = '{BasicCompanySlug}'") != "0")
            return;

        var basicPkg = await db.ScalarAsync("SELECT \"Id\"::text FROM \"Packages\" WHERE \"Name\" = 'Basic' AND \"IsDeleted\" = false")
            ?? throw new InvalidOperationException("Basic package missing from seed");
        var id = Guid.NewGuid();
        await db.ExecuteAsync($$"""
            INSERT INTO "Companies"
                ("Id","TenantId","Name","Slug","ContactEmail","DefaultLanguage","DefaultTimezone","DefaultCurrency",
                 "DateFormat","TimeFormat","NumberFormat","DefaultMapProvider","Status","IsDeleted","CreatedAt","UpdatedAt","Version","PackageId")
            VALUES
                ('{{id}}','{{id}}','E2E Basic Co','{{BasicCompanySlug}}','info@e2ebasic.test','fr','UTC','EUR',
                 'yyyy-MM-dd','HH:mm','en-US',0,0,false,now(),now(),0,'{{basicPkg}}')
            """);

        // Login is gated on an ACTIVE subscription row per company (AuthService
        // blocks any non-SuperAdmin whose company has none) — mirror what the
        // seed does for the demo company.
        await db.ExecuteAsync($$"""
            INSERT INTO "Subscriptions"
                ("Id","TenantId","CompanyId","PackageId","Status","StartDate","CurrentPrice","Currency","BillingCycle",
                 "IsDeleted","CreatedAt","UpdatedAt","Version")
            VALUES
                ('{{Guid.NewGuid()}}','{{id}}','{{id}}','{{basicPkg}}',0,now(),49,'USD','monthly',false,now(),now(),0)
            """);
        await db.ExecuteAsync($$"""
            UPDATE "Companies" SET "SubscriptionId" = (
                SELECT "Id" FROM "Subscriptions" WHERE "CompanyId" = '{{id}}' AND "IsDeleted" = false LIMIT 1)
            WHERE "Id" = '{{id}}'
            """);
    }

    // ── Roles ──────────────────────────────────────────────────────────────
    private static async Task EnsureRolesAsync(E2eDb db)
    {
        await EnsureRoleAsync(db, "demo-fleet", "Read Only", false,
            LivePageKeys.Select(k => $"{k}.view"));

        await EnsureRoleAsync(db, "demo-fleet", "Ops Manager", false,
            new[] { "dashboard.view" }.Concat(
                FleetCrudPageKeys.SelectMany(PageRegistry.CodesFor)));

        // Trap role: granted EVERYTHING, but the Basic package has no
        // organization/platform modules — the package gate must still deny.
        await EnsureRoleAsync(db, BasicCompanySlug, "Basic Admin", false, (await LoadPermCodesAsync(db)).Keys);

        await EnsureRoleAsync(db, BasicCompanySlug, "Basic Viewer", false,
            new[] { "dashboard" }.Concat(FleetCrudPageKeys).Select(k => $"{k}.view"));
    }

    private static async Task EnsureRoleAsync(E2eDb db, string companySlug, string roleName,
        bool systemRole, IEnumerable<string> codes)
    {
        var exists = await db.ScalarAsync($$"""
            SELECT COUNT(*) FROM "Roles" r JOIN "Companies" c ON c."Id" = r."CompanyId"
            WHERE c."Slug" = '{{companySlug}}' AND r."Name" = '{{roleName}}' AND r."IsDeleted" = false
            """);
        if (exists != "0") return;

        var companyId = await db.ScalarAsync($"SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = '{companySlug}'")
            ?? throw new InvalidOperationException($"Company {companySlug} missing");
        var roleId = Guid.NewGuid();
        await db.ExecuteAsync($$"""
            INSERT INTO "Roles" ("Id","TenantId","Name","Description","CompanyId","Status","IsSystemRole","DisplayOrder","IsDeleted","CreatedAt","UpdatedAt","Version")
            VALUES ('{{roleId}}','{{companyId}}','{{roleName}}','e2e fixture role','{{companyId}}',0,{{(systemRole ? "true" : "false")}},0,false,now(),now(),0)
            """);

        var perms = await LoadPermCodesAsync(db);
        var rows = codes.Where(c => perms.ContainsKey(c))
            .Select(c => $"('{Guid.NewGuid()}','{companyId}','{roleId}','{perms[c]}',false,now(),now(),0)")
            .ToList();
        if (rows.Count > 0)
        {
            await db.ExecuteAsync($$"""
                INSERT INTO "RolePermissions" ("Id","TenantId","RoleId","PermissionId","IsDeleted","CreatedAt","UpdatedAt","Version")
                VALUES {{string.Join(",\n", rows)}}
                """);
        }
    }

    // ── Users ──────────────────────────────────────────────────────────────
    private static async Task EnsureUsersAsync(E2eDb db)
    {
        await EnsureUserAsync(db, "demo-fleet", FleetManagerEmail, "E2E", "FleetManager", "Fleet Manager");
        await EnsureUserAsync(db, "demo-fleet", ReadOnlyEmail, "E2E", "ReadOnly", "Read Only");
        await EnsureUserAsync(db, "demo-fleet", OpsEmail, "E2E", "Ops", "Ops Manager");
        await EnsureUserAsync(db, "demo-fleet", MultiRoleEmail, "E2E", "Multi", "Read Only", "Ops Manager");
        await EnsureUserAsync(db, BasicCompanySlug, BasicAdminEmail, "E2E", "BasicAdmin", "Basic Admin");
        await EnsureUserAsync(db, BasicCompanySlug, BasicViewerEmail, "E2E", "BasicViewer", "Basic Viewer");
    }

    private static async Task EnsureUserAsync(E2eDb db, string companySlug, string email,
        string firstName, string lastName, params string[] roleNames)
    {
        if (await db.ScalarAsync($"SELECT COUNT(*) FROM \"Users\" WHERE \"Email\" = '{email}' AND \"IsDeleted\" = false") != "0")
            return;

        var companyId = await db.ScalarAsync($"SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = '{companySlug}'")
            ?? throw new InvalidOperationException($"Company {companySlug} missing");
        var userId = Guid.NewGuid();
        var hash = BCrypt.Net.BCrypt.HashPassword(Password);
        var stamp = Guid.NewGuid().ToString();
        await db.ExecuteAsync($$"""
            INSERT INTO "Users"
                ("Id","TenantId","Email","NormalizedEmail","PasswordHash","FirstName","LastName","CompanyId",
                 "Language","Timezone","Currency","Status","EmailConfirmed","PhoneNumberConfirmed","TwoFactorEnabled",
                 "AccessFailedCount","LockoutEnabled","SecurityStamp","IsDeleted","CreatedAt","UpdatedAt","Version")
            VALUES
                ('{{userId}}','{{companyId}}','{{email}}','{{email.ToUpperInvariant()}}','{{hash}}','{{firstName}}','{{lastName}}','{{companyId}}',
                 'en','UTC','USD',0,true,false,false,0,false,'{{stamp}}',false,now(),now(),0)
            """);

        foreach (var roleName in roleNames)
        {
            var roleId = await db.ScalarAsync($$"""
                SELECT r."Id"::text FROM "Roles" r JOIN "Companies" c ON c."Id" = r."CompanyId"
                WHERE c."Slug" = '{{companySlug}}' AND r."Name" = '{{roleName}}' AND r."IsDeleted" = false
                """) ?? throw new InvalidOperationException($"Role {roleName} in {companySlug} missing");
            await db.ExecuteAsync($$"""
                INSERT INTO "UserRoles" ("Id","TenantId","UserId","RoleId","IsDeleted","CreatedAt","UpdatedAt","Version")
                VALUES ('{{Guid.NewGuid()}}','{{companyId}}','{{userId}}','{{roleId}}',false,now(),now(),0)
                """);
        }
    }

    /// <summary>code → permission id for every live permission row.</summary>
    public static async Task<Dictionary<string, Guid>> LoadPermCodesAsync(E2eDb db)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var conn = db.OpenAppConnection();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT \"Id\"::text, \"Code\" FROM \"Permissions\" WHERE \"IsDeleted\" = false", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(1)] = Guid.Parse(reader.GetString(0));
        return result;
    }
}