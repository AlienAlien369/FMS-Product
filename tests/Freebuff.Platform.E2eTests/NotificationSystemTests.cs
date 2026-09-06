using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Notification system wiring — the two end-to-end flows that make the bell
/// meaningful:
///   1. Super Admin changes a company's package → company.package_changed →
///      delivered to that company's Admin users (bell count + My Notifications).
///   2. A role's permissions are edited → permission.role_updated →
///      (a) the company's Admins always, (b) only users whose EFFECTIVE
///      permissions actually changed (diff computed server-side), and the
///      affected user's permission set must refresh without a logout.
/// Plus: per-user preference mute is honored by the dispatcher.
///
/// Isolation: every mutation (package assign, preference mute) is scoped to a
/// fresh entity or restored afterward so tests never depend on each other's
/// side effects (a muted CA preference or a swapped company package would
/// otherwise break whichever test runs next).
/// </summary>
public sealed class NotificationSystemTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public NotificationSystemTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    // ─────────────────────────────────────────────────────────────────────────
    // Flow 1 — company.package_changed → company admins' bell
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PackageChange_NotifiesCompanyAdmin_BellAndPage()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var ca = await LoginAsync("admin@demofleet.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // Snapshot the original package so this test restores it afterward
        // (other tests assume the demo company keeps its seeded package).
        var originalPkg = await GetAssignedPackageIdAsync(sa, co);

        try
        {
            var before = await UnreadCountAsync(ca);

            var packages = await GetItemsAsync(sa, "/api/v1/admin/packages?pageSize=50");
            Assert.True(packages.Count > 0, "No packages available to assign");
            var pkgId = packages[0].GetProperty("id").GetGuid();
            var (as_, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                $"/api/v1/admin/companies/{co}/subscription",
                new { companyId = co, packageId = pkgId, startDate = DateTime.UtcNow }, sa);
            Assert.Equal(200, as_);

            // The company admin's bell count goes up…
            var after = await UnreadCountAsync(ca);
            Assert.True(after > before, $"Expected unread to increase, before={before} after={after}");

            // …and the notification is persisted in My Notifications with the event code.
            var items = await ListMyNotificationsAsync(ca, eventType: "company.package_changed");
            Assert.True(items.Count > 0, "No company.package_changed notification for the company admin");
            var notif = items[0];
            Assert.Contains("package", notif.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("company.package_changed", notif.GetProperty("eventType").GetString());
            _output.WriteLine($"PASS  package change notified company admin (unread {before} → {after})");

            // Mark-read endpoint works on the persisted row.
            var id = notif.GetProperty("id").GetGuid();
            var (mr, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/notifications/{id}/read", null, ca);
            Assert.Equal(200, mr);
            var unreadNow = await UnreadCountAsync(ca);
            Assert.Equal(after - 1, unreadNow);
            _output.WriteLine("PASS  mark-read decrements the bell badge");
        }
        finally
        {
            if (originalPkg != Guid.Empty)
            {
                await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                    $"/api/v1/admin/companies/{co}/subscription",
                    new { companyId = co, packageId = originalPkg, startDate = DateTime.UtcNow }, sa);
            }
        }
    }

    [Fact]
    public async Task PackageChange_MutedUser_IsNotNotified()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // Create a FRESH company-admin user so muting them cannot affect the
        // seeded admin used by other tests.
        var adminRole = await GetCompanyAdminRoleIdAsync(sa, co);
        var email = $"muted_{Guid.NewGuid():N}@test.com";
        var (uc, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/users",
            new { email, password = "TestPass123!", firstName = "Muted", lastName = "Admin", companyId = co, roleIds = new[] { adminRole } }, sa);
        Assert.True(uc is 200 or 201, $"user create {uc}");
        var mutedToken = await LoginAsync(email, "TestPass123!");

        // Mute company.package_changed for this user (personal preference).
        var (up, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            "/api/v1/notifications/preferences",
            new[] { new { eventType = "company.package_changed", enabled = false } }, mutedToken);
        Assert.Equal(200, up);

        var originalPkg = await GetAssignedPackageIdAsync(sa, co);
        try
        {
            var before = await UnreadCountAsync(mutedToken);

            var packages = await GetItemsAsync(sa, "/api/v1/admin/packages?pageSize=50");
            var pkgId = packages[0].GetProperty("id").GetGuid();
            var (as_, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                $"/api/v1/admin/companies/{co}/subscription",
                new { companyId = co, packageId = pkgId, startDate = DateTime.UtcNow }, sa);
            Assert.Equal(200, as_);

            // Muted → no new notification lands in the bell or page.
            var after = await UnreadCountAsync(mutedToken);
            Assert.Equal(before, after);
            _output.WriteLine("PASS  muted user is not notified (preference honored)");
        }
        finally
        {
            if (originalPkg != Guid.Empty)
            {
                await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                    $"/api/v1/admin/companies/{co}/subscription",
                    new { companyId = co, packageId = originalPkg, startDate = DateTime.UtcNow }, sa);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Flow 2 — permission.role_updated → admins always, affected users only
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoleEdit_RemovesPermission_NotifiesAdminAndAffectedUser_AndPermissionRefreshes()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // ── Create a test role with a known permission set (role.view + role.update). ──
        var perms = await GetPermissionsAsync(sa);
        var roleView = perms.First(p => p.Code == "role.view");
        var roleUpdate = perms.First(p => p.Code == "role.update");
        var roleDelete = perms.First(p => p.Code == "role.delete");
        var (cr, crData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/roles",
            new
            {
                name = $"Notif Role {Guid.NewGuid():N}"[..20],
                companyId = co,
                permissionIds = new[] { roleView.Id, roleUpdate.Id }
            }, sa);
        Assert.True(cr is 200 or 201, $"role create {cr}");
        var roleId = crData!.Value.GetProperty("id").GetGuid();

        // ── Create a user holding that role. ──
        var email = $"notifuser_{Guid.NewGuid():N}@test.com";
        var (uc, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/users",
            new { email, password = "TestPass123!", firstName = "Notif", lastName = "User", companyId = co, roleIds = new[] { roleId } }, sa);
        Assert.True(uc is 200 or 201, $"user create {uc}");
        var userToken = await LoginAsync(email, "TestPass123!");

        var startPerms = await EffectivePermissionsAsync(userToken);
        Assert.Contains("role.view", startPerms);
        Assert.Contains("role.update", startPerms);

        // Baseline: admin unread before any edit.
        var ca = await LoginAsync("admin@demofleet.com");
        var adminUnreadBefore = await UnreadCountAsync(ca);

        // ── No-op edit (same permission ids) → user NOT notified, admin IS. ──
        var (noop, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/roles/{roleId}",
            new { name = $"Notif Role {Guid.NewGuid():N}"[..20], permissionIds = new[] { roleView.Id, roleUpdate.Id } }, sa);
        Assert.Equal(200, noop);
        var userTargetedAfterNoop = await CountTargetedUserNotificationsAsync(userToken);
        var adminUnreadMid = await UnreadCountAsync(ca);
        Assert.True(adminUnreadMid > adminUnreadBefore, "Admin should be notified even on a no-op role edit");
        Assert.Equal(0, userTargetedAfterNoop); // no user-targeted notification for a no-op edit

        // ── Real change: remove role.update (a permission the user relies on). ──
        var (edit, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/roles/{roleId}",
            new { name = $"Notif Role {Guid.NewGuid():N}"[..20], permissionIds = new[] { roleView.Id, roleDelete.Id } }, sa);
        Assert.Equal(200, edit);

        // User's effective permissions changed → they get the targeted notification.
        var userTargetedAfterReal = await CountTargetedUserNotificationsAsync(userToken);
        Assert.True(userTargetedAfterReal > userTargetedAfterNoop, "User whose effective perms changed must be notified");

        // The user's LIVE permission set has refreshed (no stale cache): role.update
        // is gone and the newly granted role.delete is present — this is what the
        // UI reads to hide buttons without a logout.
        var endPerms = await EffectivePermissionsAsync(userToken);
        Assert.DoesNotContain("role.update", endPerms);
        Assert.Contains("role.delete", endPerms);

        // The admin also still sees the event.
        Assert.True(await UnreadCountAsync(ca) > adminUnreadMid);
        _output.WriteLine("PASS  role edit: admin notified always, affected user notified + perms refreshed");
    }

    [Fact]
    public async Task RoleEdit_MultiRole_NoEffectiveChange_DoesNotNotifyUser()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);
        var perms = await GetPermissionsAsync(sa);
        var vehicleView = perms.First(p => p.Code == "vehicle.view");
        var vehicleDelete = perms.First(p => p.Code == "vehicle.delete");

        // Two roles, BOTH granting vehicle.view + vehicle.delete.
        var (cr1, d1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/roles",
            new { name = $"MR A {Guid.NewGuid():N}"[..16], companyId = co, permissionIds = new[] { vehicleView.Id, vehicleDelete.Id } }, sa);
        Assert.True(cr1 is 200 or 201, $"role A create {cr1}");
        var roleA = d1!.Value.GetProperty("id").GetGuid();
        var (cr2, d2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/roles",
            new { name = $"MR B {Guid.NewGuid():N}"[..16], companyId = co, permissionIds = new[] { vehicleView.Id, vehicleDelete.Id } }, sa);
        Assert.True(cr2 is 200 or 201, $"role B create {cr2}");
        var roleB = d2!.Value.GetProperty("id").GetGuid();

        // User holds BOTH roles.
        var email = $"multirole_{Guid.NewGuid():N}@test.com";
        var (uc, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/users",
            new { email, password = "TestPass123!", firstName = "Multi", lastName = "Role", companyId = co, roleIds = new[] { roleA, roleB } }, sa);
        Assert.True(uc is 200 or 201, $"user create {uc}");
        var userToken = await LoginAsync(email, "TestPass123!");

        // ── Edit role B to REMOVE vehicle.delete. User still has it via role A →
        //    their own effective set is unchanged → NO targeted notification. ──
        var (editB, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/roles/{roleB}",
            new { name = $"MR B {Guid.NewGuid():N}"[..16], permissionIds = new[] { vehicleView.Id } }, sa);
        Assert.Equal(200, editB);
        var afterEditB = await CountTargetedUserNotificationsAsync(userToken);
        Assert.Equal(0, afterEditB);

        // The user's live effective set genuinely still includes vehicle.delete.
        var permsAfterB = await EffectivePermissionsAsync(userToken);
        Assert.Contains("vehicle.delete", permsAfterB);

        // ── Now edit role A to remove vehicle.delete too → the user ACTUALLY
        //    loses it (no other role grants it) → targeted notification fires. ──
        var (editA, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/roles/{roleA}",
            new { name = $"MR A {Guid.NewGuid():N}"[..16], permissionIds = new[] { vehicleView.Id } }, sa);
        Assert.Equal(200, editA);
        var afterEditA = await CountTargetedUserNotificationsAsync(userToken);
        Assert.True(afterEditA > afterEditB, "User whose TOTAL effective perms changed must be notified");
        var permsAfterA = await EffectivePermissionsAsync(userToken);
        Assert.DoesNotContain("vehicle.delete", permsAfterA);
        _output.WriteLine("PASS  multi-role diff: no notification while another role covers it, notification once it truly disappears");
    }

    [Fact]
    public async Task PackageModuleEdit_NotifiesAffectedCompanyAdmins()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var ca = await LoginAsync("admin@demofleet.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // The demo company is on the Professional package; editing that package's
        // module grants is a config change its admins must hear about.
        var originalPkg = await GetAssignedPackageIdAsync(sa, co);
        Assert.NotEqual(Guid.Empty, originalPkg);
        var (pg, pd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/packages/{originalPkg}", null, sa);
        Assert.Equal(200, pg);
        var moduleIds = pd!.Value.GetProperty("moduleIds").EnumerateArray()
            .Select(m => m.GetGuid()).ToList();
        Assert.True(moduleIds.Count > 0);

        var before = await UnreadCountAsync(ca);
        // Re-write the same module grants (a no-op edit still fires the config
        // event — module access for the company was (re)declared by Super Admin).
        var (up, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/admin/packages/{originalPkg}",
            new { moduleIds }, sa);
        Assert.Equal(200, up);

        var after = await UnreadCountAsync(ca);
        Assert.True(after > before, $"Company admin should be notified on package module edit, before={before} after={after}");
        var items = await ListMyNotificationsAsync(ca, eventType: "company.config_changed");
        Assert.True(items.Count > 0, "No company.config_changed notification for the company admin");
        Assert.Contains("modules", items[0].GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("PASS  package module edit notified affected company admins");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Registry + reachability
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotificationRegistry_IsSeeded_AndPagesReachable()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var ca = await LoginAsync("admin@demofleet.com");

        // Event-type catalog is readable by any authenticated user.
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/notifications/event-types", null, ca);
        Assert.Equal(200, s);
        var codes = d!.Value.EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        Assert.Contains("company.package_changed", codes);
        Assert.Contains("permission.role_updated", codes);
        Assert.Contains("alert.fired", codes);

        // My notifications page endpoints answer for both SA and CA.
        foreach (var token in new[] { sa, ca })
        {
            var (ls, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                "/api/v1/notifications?pageSize=5", null, token);
            Assert.Equal(200, ls);
            var (rc, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                "/api/v1/notifications/recent?limit=5", null, token);
            Assert.Equal(200, rc);
        }
        _output.WriteLine("PASS  notification endpoints reachable, registry seeded");
    }

    /// <summary>
    /// The UI's severity selector is a passthrough (?severity=N) — this locks in
    /// the contract the interaction depends on: picking High returns ONLY
    /// severity-3 rows (the restricted-zone alert.fired family), never the
    /// Medium corridor rows. Rows are seeded directly for the demo admin,
    /// shaped exactly like the trip pipeline emits them, so the test never
    /// depends on leftover drive data.
    /// </summary>
    [Fact]
    public async Task SeverityFilter_PickHigh_ShowsOnlyRestrictedZoneNotifications()
    {
        var ca = await LoginAsync("admin@demofleet.com");

        var uid = await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Users\" WHERE \"Email\"='admin@demofleet.com' AND \"IsDeleted\"=false LIMIT 1");
        var cid = await _db.ScalarAsync("SELECT \"CompanyId\"::text FROM \"Users\" WHERE \"Email\"='admin@demofleet.com' AND \"IsDeleted\"=false LIMIT 1");
        Assert.False(string.IsNullOrEmpty(uid), "demo admin user not found");
        Assert.False(string.IsNullOrEmpty(cid), "demo admin company not found");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        await _db.ExecuteAsync($@"
            INSERT INTO ""Notifications"" (""Id"",""Title"",""Message"",""IsRead"",""UserId"",""CompanyId"",""Status"",""TenantId"",""CreatedAt"",""UpdatedAt"",""IsDeleted"",""Version"",""EventType"",""Severity"",""DeliveryChannel"")
            VALUES
            ('{Guid.NewGuid()}'::uuid, 'E2E high restricted-zone {suffix}', 'Vehicle entered a do-not-enter geofence', false, '{uid}'::uuid, '{cid}'::uuid, 0, '{cid}'::uuid, now(), now(), false, 0, 'alert.fired', 3, 'in_app'),
            ('{Guid.NewGuid()}'::uuid, 'E2E medium corridor deviation {suffix}', 'Vehicle stayed outside the route buffer', false, '{uid}'::uuid, '{cid}'::uuid, 0, '{cid}'::uuid, now(), now(), false, 0, 'alert.fired', 2, 'in_app');
        ");

        // The UI's "High" selection sends ?severity=3 — only the restricted-zone
        // breach comes back, the Medium corridor row is excluded.
        var high = await ListBySeverityAsync(ca, 3);
        Assert.All(high, n => Assert.Equal(3, n.GetProperty("severity").GetInt32()));
        Assert.Contains(high, n => (n.GetProperty("title").GetString() ?? "").Contains($"restricted-zone {suffix}"));
        Assert.DoesNotContain(high, n => (n.GetProperty("title").GetString() ?? "").Contains("corridor deviation"));

        // The other end of the selector: ?severity=2 returns only the corridor row.
        var medium = await ListBySeverityAsync(ca, 2);
        Assert.All(medium, n => Assert.Equal(2, n.GetProperty("severity").GetInt32()));
        Assert.Contains(medium, n => (n.GetProperty("title").GetString() ?? "").Contains($"corridor deviation {suffix}"));
        Assert.DoesNotContain(medium, n => (n.GetProperty("title").GetString() ?? "").Contains("restricted-zone"));

        _output.WriteLine("PASS  severity filter — High shows only restricted-zone, Medium only corridor");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> LoginAsync(string email, string password = "Admin@123")
    {
        var key = $"{email}:{password}";
        if (_tokens.TryGetValue(key, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, password);
        Assert.NotNull(token);
        _tokens[key] = token!;
        return token!;
    }

    private async Task<int> UnreadCountAsync(string token)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/notifications/unread-count", null, token);
        Assert.Equal(200, s);
        return d!.Value.GetProperty("count").GetInt32();
    }

    private async Task<List<JsonElement>> ListMyNotificationsAsync(string token, string? eventType = null)
    {
        var url = eventType != null
            ? $"/api/v1/notifications?pageSize=50&eventType={Uri.EscapeDataString(eventType)}"
            : "/api/v1/notifications?pageSize=50";
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, url, null, token);
        Assert.Equal(200, s);
        return d!.Value.GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<List<JsonElement>> ListBySeverityAsync(string token, int severity)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/notifications?pageSize=50&severity={severity}", null, token);
        Assert.Equal(200, s);
        return d!.Value.GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>Count notifications addressed to THIS user with the "Your access changed" title.</summary>
    private async Task<int> CountTargetedUserNotificationsAsync(string token)
    {
        var items = await ListMyNotificationsAsync(token, eventType: "permission.role_updated");
        return items.Count(n => (n.GetProperty("title").GetString() ?? "").Contains("Your access changed"));
    }

    private async Task<HashSet<string>> EffectivePermissionsAsync(string token)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/auth/permissions", null, token);
        Assert.Equal(200, s);
        return d!.Value.GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString() ?? string.Empty).ToHashSet();
    }

    private async Task<string> GetDemoCompanyIdAsync(string saToken)
    {
        const string cacheKey = "demo_company_id";
        if (_tokens.TryGetValue(cacheKey, out var cached)) return cached;
        var (s, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get,
            "/api/v1/admin/companies?pageSize=10", null, saToken);
        Assert.Equal(200, s);
        var demo = root.GetProperty("data").GetProperty("items").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("name").GetString()?.Contains("Demo") == true);
        Assert.False(demo.ValueKind == JsonValueKind.Undefined, "Demo company not found");
        var id = demo.GetProperty("id").GetString()!;
        _tokens[cacheKey] = id;
        return id;
    }

    private async Task<Guid> GetAssignedPackageIdAsync(string saToken, string companyId)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/companies/{companyId}/subscription", null, saToken);
        if (s != 200 || d == null) return Guid.Empty;
        if (d.Value.TryGetProperty("packageId", out var p) && p.ValueKind == JsonValueKind.String
            && Guid.TryParse(p.GetString(), out var pid)) return pid;
        return Guid.Empty;
    }

    private async Task<Guid> GetCompanyAdminRoleIdAsync(string saToken, string companyId)
    {
        var (s, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/admin/companies/{companyId}/roles", null, saToken);
        Assert.Equal(200, s);
        var role = root.GetProperty("data").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("name").GetString() == "Company Admin");
        Assert.False(role.ValueKind == JsonValueKind.Undefined, "Company Admin role not found");
        return role.GetProperty("id").GetGuid();
    }

    private async Task<List<JsonElement>> GetItemsAsync(string token, string endpoint)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, endpoint, null, token);
        if (s != 200 || d == null) return new();
        return d.Value.GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<List<(Guid Id, string Code)>> GetPermissionsAsync(string token)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/permissions?pageSize=500", null, token);
        Assert.Equal(200, s);
        return d!.Value.GetProperty("items").EnumerateArray()
            .Select(p => (p.GetProperty("id").GetGuid(), p.GetProperty("code").GetString()!))
            .ToList();
    }
}