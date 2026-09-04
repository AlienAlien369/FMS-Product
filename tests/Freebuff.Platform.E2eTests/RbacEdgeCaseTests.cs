using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Negative / edge-case coverage for the RBAC + package/module system:
/// zero-permission roles, package downgrade (data intact, access blocked,
/// no crash), deleting a role that is assigned to users (graceful rule),
/// deleting a module/page that a role's permissions reference (no corruption),
/// multi-role effective-permission union, and export/import gating consistency.
/// </summary>
public sealed class RbacEdgeCaseTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly RbacOracle _oracle;
    private readonly Checker _checker;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public RbacEdgeCaseTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _checker = new Checker(output);
        _oracle = new RbacOracle(_db);
    }

    public async Task InitializeAsync() => await RbacFixtures.SeedAsync(_db);
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> TokenAsync(string email, string password = RbacFixtures.Password)
    {
        var key = $"{email}|{password}";
        if (_tokens.TryGetValue(key, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[key] = token;
        return token;
    }

    private const string SuperAdminEmail = "admin@freebuff.com";
    private const string DemoAdminEmail = "admin@demofleet.com";

    private static HashSet<string> PermSet(JsonElement? data)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (data != null && data.Value.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
            foreach (var p in perms.EnumerateArray()) set.Add(p.GetString()!);
        return set;
    }

    private async Task<Guid> DemoCompanyIdAsync() => await _oracle.CompanyIdAsync("demo-fleet");

    // ───────────────────────────────────────────────────────────────────────
    // 1. Zero-permission role → empty nav, no error state, everything 403
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Edge_ZeroPermissionRole_NoCrash_AllDenied()
    {
        var demo = await TokenAsync(DemoAdminEmail);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create a role with NO permissions via the API (SuperAdmin can do it;
        // here we use the company admin who has role.create).
        var (rs, rdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/roles",
            new { name = $"Ghost Role {suffix}", description = "zero perms" }, demo);
        _checker.Check("Company Admin creates zero-permission role", rs is 200 or 201, $"status={rs}");
        var roleId = rdata!.Value.GetProperty("id").GetGuid();

        // Assign to a brand-new user.
        var email = $"ghost.{suffix}@demo.test";
        var (us, udata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/users",
            new { email, password = "Pass@123", firstName = "Ghost", lastName = "User", roleIds = new[] { roleId } }, demo);
        _checker.Check("Assign zero-permission role to new user", us is 200 or 201, $"status={us}");

        // Login works; effective permissions = EMPTY (default deny).
        var ghost = await TokenAsync(email, "Pass@123");
        var (ps, pdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: ghost);
        _checker.Check("Ghost user /auth/permissions 200", ps == 200, $"status={ps}");
        _checker.Check("Ghost user effective permissions = empty (default deny)", PermSet(pdata).Count == 0,
            $"count={PermSet(pdata).Count}");

        // Every page endpoint denies; nothing 500s; read-only tenant data still loads (UI won't blank).
        var (v1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles", token: ghost);
        _checker.Check("Ghost user GET /vehicles = 403", v1 == 403, $"status={v1}");
        var (v2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: ghost);
        _checker.Check("Ghost user GET /users = 403", v2 == 403, $"status={v2}");
        var (v3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/dashboard/stats", token: ghost);
        _checker.Check("Ghost user GET /dashboard/stats = 403", v3 == 403, $"status={v3}");
        var (v4, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/modules?pageSize=50", token: ghost);
        _checker.Check("Ghost user GET /modules catalog = 200 (page shell loads)", v4 == 200, $"status={v4}");
        var (v5, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: ghost);
        _checker.Check("Ghost user GET /tenant/company = 200 (layout loads)", v5 == 200, $"status={v5}");

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 2. Package downgrade: data intact, access blocked, no 500s
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Edge_PackageDowngrade_DataIntact_AccessBlocked()
    {
        var sa = await TokenAsync(SuperAdminEmail);
        var demo = await TokenAsync(DemoAdminEmail);
        var demoCompany = await DemoCompanyIdAsync();
        var basic = Guid.Parse((await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Packages\" WHERE \"Name\" = 'Basic'"))!);
        var pro = Guid.Parse((await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Packages\" WHERE \"Name\" = 'Professional'"))!);

        // Snapshot existing data for the org module (users) and fleet (vehicles).
        var usersBefore = await _db.ScalarAsync($"SELECT COUNT(*) FROM \"Users\" WHERE \"CompanyId\" = '{demoCompany}' AND \"IsDeleted\" = false");
        var vehiclesBefore = await _db.ScalarAsync($"SELECT COUNT(*) FROM \"Vehicles\" WHERE \"CompanyId\" = '{demoCompany}' AND \"IsDeleted\" = false");

        // Downgrade to Basic (org module drops out).
        var (assign, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/admin/companies/{demoCompany}/subscription",
            new { CompanyId = demoCompany, PackageId = basic }, sa);
        _checker.Check("Assign Basic package succeeds", assign == 200, $"status={assign}");

        try
        {
            // Access blocked for the org-module pages — immediately (cache invalidated).
            var (u1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: demo);
            _checker.Check("After downgrade GET /users = 403 (immediate)", u1 == 403, $"status={u1}");
            var (u2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/roles", token: demo);
            _checker.Check("After downgrade GET /roles = 403", u2 == 403, $"status={u2}");

            // Fleet pages still work AND existing data is NOT deleted by the downgrade.
            var (u3, d3) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles?pageSize=100", token: demo);
            var vehiclesAfter = await _db.ScalarAsync($"SELECT COUNT(*) FROM \"Vehicles\" WHERE \"CompanyId\" = '{demoCompany}' AND \"IsDeleted\" = false");
            _checker.Check("After downgrade GET /vehicles = 200", u3 == 200, $"status={u3}");
            _checker.Check("Vehicle data intact after downgrade", vehiclesAfter == vehiclesBefore,
                $"before={vehiclesBefore}, after={vehiclesAfter}");
            _checker.Check("User data intact after downgrade",
                usersBefore == await _db.ScalarAsync($"SELECT COUNT(*) FROM \"Users\" WHERE \"CompanyId\" = '{demoCompany}' AND \"IsDeleted\" = false"));

            // No 500s anywhere on the read surface.
            var (u4, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/geofences", token: demo);
            var (u5, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/dashboard/stats", token: demo);
            _checker.Check("After downgrade /geofences + /dashboard/stats not 500",
                u4 is 200 or 403 && u5 is 200 or 403, $"geof={u4}, dash={u5}");
        }
        finally
        {
            // Restore Professional so sibling tests see the demo company intact.
            await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                $"/api/v1/admin/companies/{demoCompany}/subscription",
                new { CompanyId = demoCompany, PackageId = pro }, sa);
        }

        var (u6, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: demo);
        _checker.Check("After restore GET /users = 200 again", u6 == 200, $"status={u6}");

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 3. Deleting a role that is assigned to users — graceful, no dangling crash
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Edge_DeleteAssignedRole_Graceful_NoDangling()
    {
        var demo = await TokenAsync(DemoAdminEmail);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Role with a single permission (vehicle.view).
        var perms = await RbacFixtures.LoadPermCodesAsync(_db);
        var viewId = perms["vehicle.view"];
        var (rs, rdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/roles",
            new { name = $"Disposable {suffix}", description = "will be deleted", permissionIds = new[] { viewId } }, demo);
        _checker.Check("Create disposable role", rs is 200 or 201, $"status={rs}");
        var roleId = rdata!.Value.GetProperty("id").GetGuid();

        var email = $"disp.{suffix}@demo.test";
        var (us, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/users",
            new { email, password = "Pass@123", firstName = "Disp", lastName = "User", roleIds = new[] { roleId } }, demo);
        _checker.Check("Assign disposable role to user", us is 200 or 201, $"status={us}");

        var token = await TokenAsync(email, "Pass@123");
        var (b1, bd1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: token);
        _checker.Check("User effective perms include vehicle.view before deletion",
            b1 == 200 && PermSet(bd1).Contains("vehicle.view"), $"status={b1}");

        // The business rule: the role CAN be deleted; the assignment row stays but
        // the role's grants stop applying — the user must keep working, no 500s.
        var (del, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/roles/{roleId}", token: demo);
        _checker.Check("Delete role succeeds", del == 200, $"status={del}");

        var (a1, ad1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: token);
        _checker.Check("After role deletion /auth/permissions still 200 (no crash)", a1 == 200, $"status={a1}");
        _checker.Check("vehicle.view dropped from effective perms (no dangling grant)",
            !PermSet(ad1).Contains("vehicle.view"));

        var (a2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles", token: token);
        _checker.Check("Vehicle API now 403 for the user", a2 == 403, $"status={a2}");
        var (a3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: token);
        _checker.Check("User still loads own company (UI not broken)", a3 == 200, $"status={a3}");

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 4. Deleting a module/page referenced by a role's permissions
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Edge_DeleteModuleWithRolePerms_NoCorruption()
    {
        var sa = await TokenAsync(SuperAdminEmail);

        // Create a NEW module + page (SuperAdmin registry CRUD). A page created
        // here is NOT in the C# PageRegistry, so its permissions are default-deny
        // for tenants even when a role is granted them — that's the designed
        // "register in the matrix before it grants access" contract.
        var modSlug = $"e2e-edge-{Guid.NewGuid():N}"[..20];
        var (ms, mdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules",
            new { code = modSlug, name = "E2E Edge Module", isCore = false }, sa);
        _checker.Check("Create edge module", ms == 200, $"status={ms}");
        var moduleId = mdata!.Value.GetProperty("id").GetGuid();

        var pageKey = $"{modSlug}-page";
        var (pcs, pcdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/admin/modules/{moduleId}/pages",
            new { key = pageKey, name = "E2E Edge Page" }, sa);
        _checker.Check("Create edge page", pcs == 200, $"status={pcs}");
        var pageId = pcdata!.Value.GetProperty("id").GetGuid();

        // Make it live (un-plan + nav + route) — still NOT in the C# registry.
        var (pub, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{pageId}",
            new { planned = false, nav = true, route = $"/{pageKey}" }, sa);
        _checker.Check("Publish edge page (un-plan + nav + route)", pub == 200, $"status={pub}");

        // Grant the page's view permission to the Fleet Manager role (via SQL,
        // exactly what a role-configuration would persist).
        var fmRoleId = Guid.Parse((await _db.ScalarAsync(
            "SELECT \"Id\"::text FROM \"Roles\" WHERE \"Name\" = 'Fleet Manager' AND \"IsDeleted\" = false LIMIT 1"))!);
        var perms = await RbacFixtures.LoadPermCodesAsync(_db);
        var demoCompany = await DemoCompanyIdAsync();
        if (perms.TryGetValue($"{pageKey}.view", out var permId))
        {
            await _db.ExecuteAsync($$"""
                INSERT INTO "RolePermissions" ("Id","TenantId","RoleId","PermissionId","IsDeleted","CreatedAt","UpdatedAt","Version")
                VALUES ('{{Guid.NewGuid()}}','{{demoCompany}}','{{fmRoleId}}','{{permId}}',false,now(),now(),0)
                """);
        }

        // Default-deny: the granted-but-unregistered code is NOT effective for
        // any tenant user (role grants ∩ registry-known company-allowed set).
        var fm = await TokenAsync(RbacFixtures.FleetManagerEmail);
        var (e1, ed1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: fm);
        _checker.Check("Unregistered page's permission is default-deny for tenant users",
            e1 == 200 && !PermSet(ed1).Contains($"{pageKey}.view"), $"status={e1}");

        // SuperAdmin cascade-deletes the module (page + its permissions go too).
        var (del, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/admin/modules/{moduleId}?cascade=true", token: sa);
        _checker.Check("Cascade-delete module with page", del == 200, $"status={del}");

        // No corruption: everything still 200s, the role keeps working.
        var (e2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: fm);
        _checker.Check("After module delete /auth/permissions still 200", e2 == 200, $"status={e2}");
        var (e3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles", token: fm);
        _checker.Check("Fleet Manager still works after module deletion", e3 == 200, $"status={e3}");

        // All the page's permissions were soft-deleted with it — no RolePermission
        // can reference a live permission of the deleted page.
        var dangling = await _db.ScalarAsync($$"""
            SELECT COUNT(*) FROM "RolePermissions" rp
            JOIN "Permissions" p ON p."Id" = rp."PermissionId"
            WHERE p."Module" = '{{pageKey}}' AND p."IsDeleted" = false
            """);
        _checker.Check("No live RolePermission references the deleted page's permissions",
            dangling == "0", $"count={dangling}");

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 5. Multi-role union
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Edge_MultiRole_EffectiveIsUnion()
    {
        var token = await TokenAsync(RbacFixtures.MultiRoleEmail); // Read Only + Ops Manager
        var (uid, cid) = await _oracle.IdentityAsync(RbacFixtures.MultiRoleEmail);
        var expected = await _oracle.EffectiveCodesAsync(uid, cid);
        var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: token);
        var actual = PermSet(data);

        _checker.Check("Multi-role user permissions endpoint 200", status == 200, $"status={status}");
        _checker.Check("Multi-role effective == union of both roles ∩ package (oracle)",
            actual.SetEquals(expected), $"got={actual.Count}, want={expected.Count}");

        // Union carries the strongest of each role:
        _checker.Check("vehicle.view present (both roles)", actual.Contains("vehicle.view"));
        _checker.Check("vehicle.delete present (Ops Manager only)", actual.Contains("vehicle.delete"));
        _checker.Check("route.export present (Ops Manager only)", actual.Contains("route.export"));
        _checker.Check("company.view present (Read Only only)", actual.Contains("company.view"));
        _checker.Check("user.view present (Read Only only)", actual.Contains("user.view"));
        _checker.Check("company.delete absent (neither role)", !actual.Contains("company.delete"));
        _checker.Check("package.view absent (platform module not in package)", !actual.Contains("package.view"));

        // HTTP agrees with the union: DELETE a vehicle (Ops grant) works,
        // /users list (Read Only grant) works.
        var demo = await TokenAsync(DemoAdminEmail);
        var (cs, cd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            new { registrationNumber = $"E2E-{Guid.NewGuid():N}"[..18], name = "Union Vehicle", vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1 }, demo);
        var vid = cs is 200 or 201 ? cd!.Value.GetProperty("id").GetGuid() : Guid.Empty;
        if (vid != Guid.Empty)
        {
            var (delSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/vehicles/{vid}", token: token);
            _checker.Check("Multi-role user DELETE vehicle = 200 (union has vehicle.delete)", delSt == 200, $"status={delSt}");
        }
        var (usersSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: token);
        _checker.Check("Multi-role user GET /users = 200 (union has user.view)", usersSt == 200, $"status={usersSt}");

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 6. Export/Import gating consistency
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Edge_ExportImport_GatedLikeOtherActions()
    {
        var demo = await TokenAsync(DemoAdminEmail);
        var ops = await TokenAsync(RbacFixtures.OpsEmail);
        var readOnly = await TokenAsync(RbacFixtures.ReadOnlyEmail);

        // Ops Manager (all 6 on vehicle) — export/import present.
        var (o1, od1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: ops);
        _checker.Check("Ops Manager has vehicle.export", o1 == 200 && PermSet(od1).Contains("vehicle.export"));
        _checker.Check("Ops Manager has vehicle.import", o1 == 200 && PermSet(od1).Contains("vehicle.import"));

        // Read Only (view-only) — export/import absent.
        var (r1, rd1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: readOnly);
        _checker.Check("Read Only lacks vehicle.export", r1 == 200 && !PermSet(rd1).Contains("vehicle.export"));
        _checker.Check("Read Only lacks vehicle.import", r1 == 200 && !PermSet(rd1).Contains("vehicle.import"));

        // Selector offers them to roles that may grant them, and they are
        // grantable through the role API (persist + show up in effective set).
        var perms = await RbacFixtures.LoadPermCodesAsync(_db);
        var exportId = perms["vehicle.export"];
        var importId = perms["vehicle.import"];
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var (gs, gd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/permissions/grouped", token: demo);
        var vehicleGroup = gd != null ? gd.Value.EnumerateArray().FirstOrDefault(g => g.GetProperty("module").GetString() == "vehicle") : default;
        var offered = vehicleGroup.ValueKind == JsonValueKind.Object
            && vehicleGroup.GetProperty("permissions").EnumerateArray()
                .Select(p => p.GetProperty("code").GetString()).ToHashSet().IsSupersetOf(new[] { "vehicle.export", "vehicle.import" });
        _checker.Check("Selector offers vehicle.export/import to Company Admin", gs == 200 && offered, $"status={gs}");

        // Role create with export/import ids persists them.
        var (rs, rdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/roles",
            new { name = $"Export Role {suffix}", description = "export/import", permissionIds = new[] { exportId, importId } }, demo);
        _checker.Check("Role create with export/import ids succeeds", rs is 200 or 201, $"status={rs}");
        var roleId = rdata!.Value.GetProperty("id").GetGuid();
        var email = $"exp.{suffix}@demo.test";
        var (us, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/users",
            new { email, password = "Pass@123", firstName = "Exp", lastName = "User", roleIds = new[] { roleId } }, demo);
        _checker.Check("Assign export role to user", us is 200 or 201, $"status={us}");

        var userToken = await TokenAsync(email, "Pass@123");
        var (p2, pd2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: userToken);
        var set = PermSet(pd2);
        _checker.Check("New user effective perms include vehicle.export", p2 == 200 && set.Contains("vehicle.export"));
        _checker.Check("New user effective perms include vehicle.import", p2 == 200 && set.Contains("vehicle.import"));

        _checker.AssertAll();
    }
}