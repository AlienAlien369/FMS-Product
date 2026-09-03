using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// SuperAdmin-only Module + Page/Form registry CRUD (the /api/v1/admin/modules
/// and /api/v1/admin/pages surface): role gating (403 for non-SuperAdmin),
/// slug/duplicate validation, core-module/page protection, cascade delete guard,
/// permission rows created for new pages, and reorder persistence.
/// </summary>
public sealed class RegistryCrudTests : IClassFixture<E2eFixture>
{
    private readonly E2eDb _db;
    private readonly Checker _checker;

    public RegistryCrudTests(E2eFixture fixture, ITestOutputHelper output)
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

    private async Task<Guid> ModuleIdAsync(string code) =>
        Guid.Parse((await _db.ScalarAsync($"SELECT \"Id\"::text FROM \"Modules\" WHERE \"Code\" = '{code}' AND \"IsDeleted\" = false"))!);

    private async Task<int> PermissionCountForPageAsync(string key) =>
        int.Parse((await _db.ScalarAsync($"SELECT COUNT(*)::text FROM \"Permissions\" WHERE \"Module\" = '{key}' AND \"IsDeleted\" = false"))!);

    [Fact]
    public async Task Registry_WriteEndpoints_403ForNonSuperAdmin()
    {
        var demo = await DemoTokenAsync();

        var (m1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules",
            new { code = "e2e-hack", name = "Hack" }, demo);
        _checker.Check("POST /admin/modules 403 for company admin", m1 == 403, $"status={m1}");

        var (m2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/admin/modules/reorder",
            new { moduleIds = new Guid[] { } }, demo);
        _checker.Check("PUT /admin/modules/reorder 403 for company admin", m2 == 403, $"status={m2}");

        var (m3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules/00000000-0000-0000-0000-000000000001/pages",
            new { key = "x", name = "X" }, demo);
        _checker.Check("POST module pages 403 for company admin", m3 == 403, $"status={m3}");

        var (m4, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/admin/pages/00000000-0000-0000-0000-000000000002",
            new { name = "X" }, demo);
        _checker.Check("PUT /admin/pages 403 for company admin", m4 == 403, $"status={m4}");

        // Read-only catalog stays available to logged-in users.
        var (r, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/modules?pageSize=50", token: demo);
        _checker.Check("GET /modules (read-only) still 200 for company admin", r == 200, $"status={r}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Registry_ModuleCrud_SlugValidation_CoreProtection()
    {
        var sa = await SuperAdminTokenAsync();
        var slug = $"e2e-mod-{Guid.NewGuid():N}"[..24];

        // Invalid slug (uppercase, spaces)
        var (bad, badRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules",
            new { code = "Bad Slug", name = "Bad" }, sa);
        _checker.Check("Module create rejects invalid slug", bad == 400
            && badRoot.TryGetProperty("code", out var bc) && bc.GetString() == "INVALID_SLUG", $"status={bad}");

        // Create
        var (created, createdData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules",
            new { code = slug, name = "E2E Module", description = "created by e2e", isCore = false }, sa);
        _checker.Check("Module create succeeds", created == 200 && createdData != null, $"status={created}");
        var moduleId = createdData!.Value.GetProperty("id").GetGuid();

        // Duplicate slug
        var (dup, dupRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules",
            new { code = slug, name = "Duplicate" }, sa);
        _checker.Check("Module create rejects duplicate slug", dup == 400
            && dupRoot.TryGetProperty("code", out var dc) && dc.GetString() == "DUPLICATE_SLUG", $"status={dup}");

        // Detail
        var (detail, detailData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/admin/modules/{moduleId}", token: sa);
        _checker.Check("Module detail returns the module", detail == 200
            && detailData!.Value.GetProperty("code").GetString() == slug, $"status={detail}");

        // Non-core module can be renamed
        var (renamed, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{moduleId}",
            new { name = "E2E Module Renamed" }, sa);
        _checker.Check("Non-core module rename succeeds", renamed == 200, $"status={renamed}");

        // Core module (dashboard) cannot be renamed or deleted, but status toggle works
        var dashboardId = await ModuleIdAsync("dashboard");
        var (coreRename, coreRenameRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}",
            new { name = "Hacked" }, sa);
        _checker.Check("Core module rename blocked", coreRename == 400
            && coreRenameRoot.TryGetProperty("code", out var crc) && crc.GetString() == "FORBIDDEN", $"status={coreRename}");
        var (coreDelete, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/admin/modules/{dashboardId}?cascade=true", token: sa);
        _checker.Check("Core module delete blocked", coreDelete == 400, $"status={coreDelete}");
        var (coreToggle, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}",
            new { status = 1 }, sa);
        _checker.Check("Core module status toggle allowed", coreToggle == 200, $"status={coreToggle}");
        // Restore status so other tests see an active dashboard module.
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}", new { status = 0 }, sa);

        // Delete the created module (no pages → no cascade needed)
        var (deleted, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/admin/modules/{moduleId}", token: sa);
        _checker.Check("Module delete succeeds when empty", deleted == 200, $"status={deleted}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Registry_ModuleCascadeDelete_HasPagesGuard()
    {
        var sa = await SuperAdminTokenAsync();
        var slug = $"e2e-cas-{Guid.NewGuid():N}"[..24];

        var (created, createdData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/modules",
            new { code = slug, name = "Cascade Module", isCore = false }, sa);
        var moduleId = createdData!.Value.GetProperty("id").GetGuid();

        var (pageCreated, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/admin/modules/{moduleId}/pages",
            new { key = $"{slug}-page", name = "Cascade Page" }, sa);
        _checker.Check("Page create inside new module", pageCreated == 200, $"status={pageCreated}");

        // Without cascade → refused
        var (noCascade, noCascadeRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Delete, $"/api/v1/admin/modules/{moduleId}", token: sa);
        _checker.Check("Module delete refused when it has pages", noCascade == 400
            && noCascadeRoot.TryGetProperty("code", out var hc) && hc.GetString() == "HAS_PAGES", $"status={noCascade}");

        // With cascade → module + pages gone
        var (withCascade, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/admin/modules/{moduleId}?cascade=true", token: sa);
        _checker.Check("Module cascade delete succeeds", withCascade == 200, $"status={withCascade}");
        var liveModule = await _db.ScalarAsync($"SELECT COUNT(*)::text FROM \"Modules\" WHERE \"Id\" = '{moduleId}' AND \"IsDeleted\" = false");
        var livePage = await _db.ScalarAsync($"SELECT COUNT(*)::text FROM \"Pages\" WHERE \"ModuleId\" = '{moduleId}' AND \"IsDeleted\" = false");
        var livePerms = await _db.ScalarAsync($"SELECT COUNT(*)::text FROM \"Permissions\" WHERE \"Module\" = '{slug}-page' AND \"IsDeleted\" = false");
        _checker.Check("Module soft-deleted", liveModule == "0", $"count={liveModule}");
        _checker.Check("Child page soft-deleted", livePage == "0", $"count={livePage}");
        _checker.Check("Page permissions soft-deleted with it", livePerms == "0", $"count={livePerms}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Registry_PageCrud_PermissionRows_KeyProtection()
    {
        var sa = await SuperAdminTokenAsync();
        var fleetId = await ModuleIdAsync("fleet");
        var key = $"e2e-pg-{Guid.NewGuid():N}"[..16];

        // Create page (defaults to Planned)
        var (created, createdData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/admin/modules/{fleetId}/pages",
            new { key, name = "E2E Page" }, sa);
        _checker.Check("Page create succeeds", created == 200, $"status={created}");
        var pageId = createdData!.Value.GetProperty("id").GetGuid();

        // 6 permission rows created for RBAC
        var permCount = await PermissionCountForPageAsync(key);
        _checker.Check("New page gets exactly 6 permissions", permCount == 6, $"count={permCount}");
        var codes = await _db.ScalarAsync(
            $"SELECT string_agg(\"Code\", ',' ORDER BY \"Code\") FROM \"Permissions\" WHERE \"Module\" = '{key}' AND \"IsDeleted\" = false");
        _checker.Check("Permission codes are key.action × 6", codes == string.Join(",", new[]
        {
            $"{key}.create", $"{key}.delete", $"{key}.export", $"{key}.import", $"{key}.update", $"{key}.view"
        }), $"codes={codes}");

        // Duplicate key rejected (global uniqueness)
        var (dup, dupRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, $"/api/v1/admin/modules/{fleetId}/pages",
            new { key, name = "Dup" }, sa);
        _checker.Check("Duplicate page key rejected", dup == 400
            && dupRoot.TryGetProperty("code", out var dc) && dc.GetString() == "DUPLICATE_SLUG", $"status={dup}");

        // Update display name + un-plan
        var (updated, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{pageId}",
            new { name = "E2E Page Live", planned = false, nav = true, route = "/e2e-page" }, sa);
        _checker.Check("Page update succeeds", updated == 200, $"status={updated}");
        var planned = await _db.ScalarAsync($"SELECT \"Planned\"::text FROM \"Pages\" WHERE \"Id\" = '{pageId}'");
        _checker.Check("Planned flag persisted", planned != null && planned.Equals("false", StringComparison.OrdinalIgnoreCase), $"planned={planned}");

        // Registry-known page key is locked (rename only display name). Geofence
        // is a non-core registry page: key rename must be blocked, name edit allowed.
        var geoPageId = Guid.Parse((await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Pages\" WHERE \"Key\" = 'geofence' AND \"IsDeleted\" = false"))!);
        var (keyChange, keyChangeRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{geoPageId}",
            new { key = "geo-zone" }, sa);
        _checker.Check("Registry page key rename blocked", keyChange == 400
            && keyChangeRoot.TryGetProperty("code", out var kc) && kc.GetString() == "FORBIDDEN", $"status={keyChange}");
        var (nameChange, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{geoPageId}",
            new { name = "Geofences" }, sa);
        _checker.Check("Registry page display-name edit allowed", nameChange == 200, $"status={nameChange}");

        // Delete the created page → its permissions go too
        var (deleted, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/admin/pages/{pageId}", token: sa);
        _checker.Check("Page delete succeeds", deleted == 200, $"status={deleted}");
        var permCountAfter = await PermissionCountForPageAsync(key);
        _checker.Check("Page permissions removed on delete", permCountAfter == 0, $"count={permCountAfter}");
        var livePage = await _db.ScalarAsync($"SELECT COUNT(*)::text FROM \"Pages\" WHERE \"Id\" = '{pageId}' AND \"IsDeleted\" = false");
        _checker.Check("Page soft-deleted", livePage == "0", $"count={livePage}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Registry_CoreItems_StatusAndOrderOnly()
    {
        var sa = await SuperAdminTokenAsync();
        var dashboardId = await ModuleIdAsync("dashboard");
        var vehiclePageId = Guid.Parse((await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Pages\" WHERE \"Key\" = 'vehicle' AND \"IsDeleted\" = false"))!);

        // Core module: any protected-field change → 400, status toggle → 200.
        var (descChange, descRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}",
            new { description = "Hacked description" }, sa);
        _checker.Check("Core module description change blocked", descChange == 400
            && descRoot.TryGetProperty("code", out var dc) && dc.GetString() == "FORBIDDEN", $"status={descChange}");
        var (iconChange, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}",
            new { icon = "skull" }, sa);
        _checker.Check("Core module icon change blocked", iconChange == 400, $"status={iconChange}");
        var (statusOn, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}", new { status = 1 }, sa);
        var (statusOff, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/modules/{dashboardId}", new { status = 0 }, sa);
        _checker.Check("Core module status toggle allowed both ways", statusOn == 200 && statusOff == 200,
            $"on={statusOn} off={statusOff}");

        // Core page: route/planned changes blocked, status toggle allowed.
        var (routeChange, routeRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{vehiclePageId}",
            new { route = "/hacked" }, sa);
        _checker.Check("Core page route change blocked", routeChange == 400
            && routeRoot.TryGetProperty("code", out var rc) && rc.GetString() == "FORBIDDEN", $"status={routeChange}");
        var (plannedChange, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{vehiclePageId}",
            new { planned = true }, sa);
        _checker.Check("Core page planned-flag change blocked", plannedChange == 400, $"status={plannedChange}");
        var (pStatusOn, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{vehiclePageId}", new { status = 1 }, sa);
        var (pStatusOff, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{vehiclePageId}", new { status = 0 }, sa);
        _checker.Check("Core page status toggle allowed both ways", pStatusOn == 200 && pStatusOff == 200,
            $"on={pStatusOn} off={pStatusOff}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Registry_PlannedPage_RevokesApiAccess()
    {
        var sa = await SuperAdminTokenAsync();
        var demo = await DemoTokenAsync();

        // Baseline: demo admin holds geofence.view (Company Admin role + fleet
        // module in the demo company's Professional package) and can hit the API.
        var (before, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/geofences", token: demo);
        _checker.Check("Geofence API 200 before planned toggle", before == 200, $"status={before}");

        var geoPageId = Guid.Parse((await _db.ScalarAsync(
            "SELECT \"Id\"::text FROM \"Pages\" WHERE \"Key\" = 'geofence' AND \"IsDeleted\" = false"))!);

        // SuperAdmin toggles the page to Planned.
        var (toggle, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{geoPageId}",
            new { planned = true }, sa);
        _checker.Check("Toggle geofence page to Planned succeeds", toggle == 200, $"status={toggle}");

        try
        {
            // Immediate API 403 (cache invalidated by the toggle, not left to TTL).
            var (after, afterRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, "/api/v1/geofences", token: demo);
            _checker.Check("Geofence API 403 after planned toggle", after == 403, $"status={after}");

            // Effective permissions no longer include the page's codes.
            var (perms, permData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                "/api/v1/auth/permissions", token: demo);
            _checker.Check("geofence.view absent from effective permissions",
                perms == 200 && !ApiJson.ContainsPermission(permData, "geofence.view"),
                $"status={perms}, count={ApiJson.PermissionCount(permData)}");
        }
        finally
        {
            // Restore so other tests / future runs see geofence active again.
            await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/pages/{geoPageId}",
                new { planned = false }, sa);
        }

        var (restored, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/geofences", token: demo);
        _checker.Check("Geofence API 200 again after restoring", restored == 200, $"status={restored}");

        _checker.AssertAll();
    }

    [Fact]
    public async Task Registry_Reorder_Persists()
    {
        var sa = await SuperAdminTokenAsync();

        var (get, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/modules?pageSize=50", token: sa);
        _checker.Check("Module catalog readable", get == 200 && data != null, $"status={get}");
        var items = data!.Value.GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("id").GetGuid()).ToList();

        // Reverse the order
        ids.Reverse();
        var (reorder, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/admin/modules/reorder",
            new { moduleIds = ids }, sa);
        _checker.Check("Module reorder succeeds", reorder == 200, $"status={reorder}");

        var (get2, data2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/modules?pageSize=50", token: sa);
        var items2 = data2!.Value.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        _checker.Check("Module order persisted", items2.SequenceEqual(ids), $"expected={string.Join(',', ids)} actual={string.Join(',', items2)}");

        // Restore original order (keeps other tests' expectations of the canonical order).
        ids.Reverse();
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/admin/modules/reorder", new { moduleIds = ids }, sa);

        _checker.AssertAll();
    }
}