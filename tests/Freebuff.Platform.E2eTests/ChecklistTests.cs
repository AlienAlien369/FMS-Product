using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Checklist items 3–7 from the architecture-rework verification, exercised over
/// real HTTP against the booted API + real Postgres: module gating (403 + hidden
/// effective permissions) with immediate package-change effect, Companies CRUD
/// (incl. delete blocked for companies with active users), Users role badges
/// persisting after reload, and the localization cascade with master-list
/// validation. Each test sets its own package preconditions, so they pass in any
/// order against the shared fixture DB.
/// </summary>
public sealed class ChecklistTests : IClassFixture<E2eFixture>
{
    private readonly E2eDb _db;
    private readonly Checker _checker;

    public ChecklistTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _checker = new Checker(output);
    }

    private const string SuperAdminEmail = "admin@freebuff.com";
    private const string DemoAdminEmail = "admin@demofleet.com";
    private const string Password = "Admin@123";

    private async Task<string> SuperAdminTokenAsync() =>
        await ApiJson.LoginAsync(_db.Client, SuperAdminEmail, Password)
        ?? throw new Xunit.Sdk.XunitException("SuperAdmin login failed on fresh seed");

    private async Task<string> DemoTokenAsync() =>
        await ApiJson.LoginAsync(_db.Client, DemoAdminEmail, Password)
        ?? throw new Xunit.Sdk.XunitException("Demo admin login failed on fresh seed");

    private async Task<Guid> CompanyIdAsync(string slug) =>
        Guid.Parse((await _db.ScalarAsync($"SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = '{slug}'"))!);

    private async Task<Guid> PackageIdAsync(string name) =>
        Guid.Parse((await _db.ScalarAsync($"SELECT \"Id\"::text FROM \"Packages\" WHERE \"Name\" = '{name}'"))!);

    /// <summary>Assign a package to a company as SuperAdmin (idempotent, cache-invalidating).</summary>
    private async Task<int> AssignPackageAsync(string saToken, Guid companyId, Guid packageId)
    {
        var (status, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/admin/companies/{companyId}/subscription",
            new { CompanyId = companyId, PackageId = packageId }, saToken);
        return status;
    }

    [Fact]
    public async Task Checklist_ModuleGating_403AndImmediatePackageChange()
    {
        var sa = await SuperAdminTokenAsync();
        var demo = await DemoTokenAsync();
        var demoCompany = await CompanyIdAsync("demo-fleet");
        var basic = await PackageIdAsync("Basic");
        var pro = await PackageIdAsync("Professional");

        // Role grant alone is not enough — company package (Professional includes
        // the organization module) must allow user.view.
        var (permStatus, demoPerms) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/auth/permissions", token: demo);
        _checker.Check("Demo admin login + permissions endpoint", permStatus == 200 && ApiJson.PermissionCount(demoPerms) > 0,
            $"status={permStatus}");

        // Downgrade to Basic (dashboard + fleet only): /users (organization page)
        // must 403 IMMEDIATELY — proves the permission cache was invalidated, not
        // left to its 2-minute TTL.
        var assign1 = await AssignPackageAsync(sa, demoCompany, basic);
        _checker.Check("Assign Basic package succeeds", assign1 == 200, $"status={assign1}");

        var (uStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: demo);
        _checker.Check("Users API 403 when org module not in package", uStatus == 403, $"status={uStatus}");

        var (vStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles", token: demo);
        _checker.Check("Vehicles API 200 (fleet module in Basic)", vStatus == 200, $"status={vStatus}");

        var (_, basicPerms) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/auth/permissions", token: demo);
        _checker.Check("user.view absent from effective permissions on Basic",
            !ApiJson.ContainsPermission(basicPerms, "user.view"), ApiJson.PermissionCount(basicPerms).ToString());
        _checker.Check("vehicle.view present on Basic",
            ApiJson.ContainsPermission(basicPerms, "vehicle.view"), ApiJson.PermissionCount(basicPerms).ToString());

        // Upgrade back to Professional: access returns with zero wait.
        var assign2 = await AssignPackageAsync(sa, demoCompany, pro);
        _checker.Check("Assign Professional package succeeds", assign2 == 200, $"status={assign2}");
        var (u2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: demo);
        _checker.Check("Users API 200 again immediately after upgrade", u2 == 200, $"status={u2}");
        var (_, proPerms) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/auth/permissions", token: demo);
        _checker.Check("user.view back in effective permissions",
            ApiJson.ContainsPermission(proPerms, "user.view"), ApiJson.PermissionCount(proPerms).ToString());

        _checker.AssertAll();
    }

    [Fact]
    public async Task Checklist_CompaniesCrud_EndToEnd()
    {
        var sa = await SuperAdminTokenAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Create
        var (createStatus, created) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/admin/companies", new { Name = $"E2E Crud Co {suffix}" }, sa);
        var companyId = created?.GetProperty("id").GetGuid();
        _checker.Check("Create company", createStatus == 200 && companyId != null, $"status={createStatus}");

        // Edit
        var (editStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/admin/companies/{companyId}", new { Name = $"E2E Crud Co {suffix} Renamed" }, sa);
        _checker.Check("Edit company", editStatus == 200, $"status={editStatus}");

        var (getStatus, detail) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/companies/{companyId}", token: sa);
        var nameAfterEdit = getStatus == 200 ? detail?.GetProperty("name").GetString() : null;
        _checker.Check("Edit persisted (list/detail reflects new name)",
            nameAfterEdit == $"E2E Crud Co {suffix} Renamed", $"name={nameAfterEdit}");

        // Delete (company with no users)
        var (delStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete,
            $"/api/v1/admin/companies/{companyId}", token: sa);
        _checker.Check("Delete company without users", delStatus == 200, $"status={delStatus}");
        var (afterDel, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/companies/{companyId}", token: sa);
        _checker.Check("Deleted company no longer fetchable", afterDel == 404, $"status={afterDel}");

        // Delete must be blocked for a company with an active user
        var (c2Status, created2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/admin/companies", new { Name = $"E2E Guard Co {suffix}" }, sa);
        var c2Id = created2?.GetProperty("id").GetGuid();
        _checker.Check("Create second company", c2Status == 200 && c2Id != null, $"status={c2Status}");

        var (uStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/admin/companies/{c2Id}/users",
            new { Email = $"guard.{suffix}@e2e.test", Password = "Pass@123", FirstName = "Guard", LastName = "User" }, sa);
        _checker.Check("Create active user in company", uStatus == 200, $"status={uStatus}");

        var (blockedStatus, blockedRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Delete,
            $"/api/v1/admin/companies/{c2Id}", token: sa);
        var code = blockedRoot.ValueKind == JsonValueKind.Object && blockedRoot.TryGetProperty("code", out var cd)
            ? cd.GetString() : null;
        _checker.Check("Delete blocked for company with active users", blockedStatus == 400 && code == "HAS_USERS",
            $"status={blockedStatus}, code={code}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Checklist_UserRoleBadges_PersistOnReload()
    {
        var sa = await SuperAdminTokenAsync();
        var demoCompany = await CompanyIdAsync("demo-fleet");
        var pro = await PackageIdAsync("Professional");
        Assert.Equal(200, await AssignPackageAsync(sa, demoCompany, pro));

        // Find the Fleet Manager role of the demo company
        var (rolesStatus, roles) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/companies/{demoCompany}/roles", token: sa);
        var fleetManagerId = default(Guid?);
        if (rolesStatus == 200 && roles != null)
        {
            foreach (var r in roles.Value.EnumerateArray())
            {
                if (r.GetProperty("name").GetString() == "Fleet Manager")
                    fleetManagerId = r.GetProperty("id").GetGuid();
            }
        }
        _checker.Check("Fleet Manager role exists in demo company", fleetManagerId != null, $"status={rolesStatus}");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"badge.{suffix}@e2e.test";
        var (uStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/admin/companies/{demoCompany}/users",
            new { Email = email, Password = "Pass@123", FirstName = "Badge", LastName = "Tester", RoleIds = new[] { fleetManagerId!.Value } }, sa);
        _checker.Check("Create user with Fleet Manager role", uStatus == 200, $"status={uStatus}");

        // Reload (fresh GET) — the badge must reflect the persisted role
        var (listStatus, list) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/companies/{demoCompany}/users?search={email}&page=1&pageSize=10", token: sa);
        var badgePresent = false;
        if (listStatus == 200 && list != null && list.Value.TryGetProperty("items", out var items))
        {
            foreach (var u in items.EnumerateArray())
            {
                if (u.GetProperty("email").GetString() == email)
                {
                    badgePresent = u.TryGetProperty("roles", out var rl)
                        && rl.EnumerateArray().Any(r => r.GetString() == "Fleet Manager");
                }
            }
        }
        _checker.Check("User list shows persisted role badge after reload", badgePresent, $"status={listStatus}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Checklist_LocalizationCascade_MasterListValidation_Isolation()
    {
        var sa = await SuperAdminTokenAsync();
        var demo = await DemoTokenAsync();
        var demoCompany = await CompanyIdAsync("demo-fleet");
        var pro = await PackageIdAsync("Professional");
        Assert.Equal(200, await AssignPackageAsync(sa, demoCompany, pro));

        // Change demo company default language/currency as its admin
        var (setStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            "/api/v1/tenant/company-settings", new { DefaultLanguage = "fr", DefaultCurrency = "EUR" }, demo);
        _checker.Check("Company admin sets fr/EUR", setStatus == 200, $"status={setStatus}");

        var (getStatus, me) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: demo);
        var lang = getStatus == 200 ? me?.GetProperty("defaultLanguage").GetString() : null;
        var cur = getStatus == 200 ? me?.GetProperty("defaultCurrency").GetString() : null;
        _checker.Check("Change applies to the company (read-back fr/EUR)", lang == "fr" && cur == "EUR", $"lang={lang}, cur={cur}");

        // Multi-tenant isolation: the platform company must be untouched
        var (pStatus, plat) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: sa);
        var platLang = pStatus == 200 ? plat?.GetProperty("defaultLanguage").GetString() : null;
        var platCur = pStatus == 200 ? plat?.GetProperty("defaultCurrency").GetString() : null;
        _checker.Check("Another company unaffected (platform still en/USD)", platLang == "en" && platCur == "USD",
            $"lang={platLang}, cur={platCur}");

        // A company cannot pick an INACTIVE master-list language (deactivate 'hi')
        await _db.ExecuteAsync("UPDATE \"Languages\" SET \"Status\" = 1 WHERE \"Code\" = 'hi'");
        try
        {
            var (badStatus, badRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put,
                "/api/v1/tenant/company-settings", new { DefaultLanguage = "hi" }, demo);
            var code = badRoot.ValueKind == JsonValueKind.Object && badRoot.TryGetProperty("code", out var cd)
                ? cd.GetString() : null;
            _checker.Check("Inactive language rejected (400 INVALID_LOCALE)", badStatus == 400 && code == "INVALID_LOCALE",
                $"status={badStatus}, code={code}");
        }
        finally
        {
            await _db.ExecuteAsync("UPDATE \"Languages\" SET \"Status\" = 0 WHERE \"Code\" = 'hi'");
        }

        // Restore demo defaults for cleanliness
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            "/api/v1/tenant/company-settings", new { DefaultLanguage = "en", DefaultCurrency = "USD" }, demo);

        _checker.AssertAll();
    }

    [Fact]
    public async Task Checklist_SuperAdmin_HasUnrestrictedAccess()
    {
        var sa = await SuperAdminTokenAsync();
        var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/auth/permissions", token: sa);
        var count = ApiJson.PermissionCount(data);
        _checker.Check("SuperAdmin permissions endpoint", status == 200, $"status={status}");
        _checker.Check("SuperAdmin bypasses package/role limits (all 6-action page perms)",
            count >= 130 && ApiJson.ContainsPermission(data, "user.delete") && ApiJson.ContainsPermission(data, "platform.view"),
            $"count={count}");
        _checker.AssertAll();
    }
}
