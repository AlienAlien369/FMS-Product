using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Centralized Audit Log contract:
///  1. Every privileged/sensitive action produces a correct, complete entry:
///     role permission changes (before/after diff), user lifecycle (created /
///     role changed / deactivated), SuperAdmin scope switches, share-link
///     generate/revoke, device-vendor registry changes, and SuperAdmin
///     cross-tenant writes (target company recorded).
///  2. Append-only: no application path can create/update/delete audit entries
///     directly — even SuperAdmin gets a clean non-2xx on every mutate verb.
///  3. Isolation: a Company Admin sees ONLY entries for their own company and
///     never SuperAdmin scope-switch / cross-tenant metadata; SuperAdmin sees
///     everything.
///  4. Filters: by actor, action code, target company, date range.
/// </summary>
public sealed class AuditLogE2eTests : IClassFixture<E2eFixture>
{
    private const string SaEmail = "admin@freebuff.com";
    private const string CoAdminEmail = "audit.coadmin@demo.test";

    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    // xUnit instantiates the test class per test method — world creation must be
    // shared across instances (one fresh DB per class run, so one world per run).
    private static readonly SemaphoreSlim WorldLock = new(1, 1);
    private static bool s_worldReady;
    private static string? s_worldRoleId;
    private static string? s_worldUserId;

    public AuditLogE2eTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    private string Uniq() => Guid.NewGuid().ToString("N")[..8];

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, Rbac.RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    private Task<string> SaTokenAsync() => TokenAsync(SaEmail);

    private async Task<string?> DemoFleetIdAsync()
        => await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet' AND \"IsDeleted\" = false");

    private async Task<string> PermIdAsync(string code)
        => await _db.ScalarAsync($"SELECT \"Id\"::text FROM \"Permissions\" WHERE \"Code\" = '{code}' AND \"IsDeleted\" = false")
           ?? throw new InvalidOperationException($"Permission '{code}' missing from seed");

    /// <summary>
    /// Shared world: demo-fleet company + an "Audit CoAdmin" role (dashboard.view)
    /// + a company-admin user holding it. Idempotent per class. audit.view is
    /// granted inside the isolation test (only possible once the feature exists).
    /// </summary>
    private async Task<(string RoleId, string UserId, string DemoFleetId)> EnsureWorldAsync()
    {
        var demoId = await DemoFleetIdAsync() ?? throw new InvalidOperationException("demo-fleet company missing");
        if (s_worldReady) return (s_worldRoleId!, s_worldUserId!, demoId);
        await WorldLock.WaitAsync();
        try
        {
            if (s_worldReady) return (s_worldRoleId!, s_worldUserId!, demoId);
            var sa = await SaTokenAsync();
            var dashId = await PermIdAsync("dashboard.view");
            var roleId = await CreateRoleAsync(sa, demoId, "Audit CoAdmin", new[] { "dashboard.view" });
            var userId = await CreateUserAsync(sa, demoId, CoAdminEmail, new[] { roleId }, dashId);
            s_worldRoleId = roleId;
            s_worldUserId = userId;
            s_worldReady = true;
            return (roleId, userId, demoId);
        }
        finally
        {
            WorldLock.Release();
        }
    }

    private async Task<string> CreateRoleAsync(string token, string companyId, string name, string[] permCodes)
    {
        var permIds = new List<string>();
        foreach (var c in permCodes) permIds.Add(await PermIdAsync(c));
        var (status, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/roles",
            new { name, description = "audit e2e role", companyId, permissionIds = permIds }, token);
        if (status != 201) throw new Xunit.Sdk.XunitException($"Role create failed {status}: {root}");
        return root.GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<string> CreateUserAsync(string token, string companyId, string email, string[] roleIds, string? dashPermId = null)
    {
        var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/users",
            new { email, password = Rbac.RbacFixtures.Password, firstName = "Audit", lastName = "CoAdmin", companyId, roleIds }, token);
        if (status != 201 || data == null) throw new Xunit.Sdk.XunitException($"User create failed {status}: {data}");
        return data.Value.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Polls the audit endpoint until ≥min entries with the action code appear.
    /// Fails fast on 404/403 — the endpoint/permission not existing is exactly
    /// the RED state, and waiting out a timeout there wastes minutes.
    /// </summary>
    private async Task<List<JsonElement>> PollAuditAsync(string token, string actionCode, int min = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        var lastStatus = 0;
        while (DateTime.UtcNow < deadline)
        {
            var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                $"/api/v1/audit?actionCode={actionCode}&pageSize=100", null, token);
            lastStatus = status;
            if (status is 404 or 403)
                throw new Xunit.Sdk.XunitException(
                    $"Audit endpoint returned {status} for actionCode={actionCode} — feature not implemented (expected RED)");
            if (status == 200 && data != null)
            {
                var items = data.Value.GetProperty("items").EnumerateArray().ToList();
                if (items.Count >= min) return items;
            }
            await Task.Delay(250);
        }
        throw new Xunit.Sdk.XunitException($"Timed out waiting for audit entries actionCode={actionCode} (last status {lastStatus})");
    }

    /// <summary>
    /// Poll until an entry with the action code MATCHES the predicate — a plain
    /// count poll can return early on an unrelated entry of the same code (e.g.
    /// the shared world's user.created before this test's user entry lands).
    /// </summary>
    private async Task<JsonElement> PollAuditEntryAsync(string token, string actionCode, Func<JsonElement, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                $"/api/v1/audit?actionCode={actionCode}&pageSize=100", null, token);
            if (status is 404 or 403)
                throw new Xunit.Sdk.XunitException($"Audit endpoint returned {status} for actionCode={actionCode}");
            if (status == 200 && data != null)
            {
                var hit = data.Value.GetProperty("items").EnumerateArray().FirstOrDefault(match);
                if (hit.ValueKind != JsonValueKind.Undefined) return hit;
            }
            await Task.Delay(250);
        }
        throw new Xunit.Sdk.XunitException($"Timed out waiting for audit entry actionCode={actionCode} matching predicate");
    }

    private static IEnumerable<string?> StrArray(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(x => x.GetString())
            : Enumerable.Empty<string?>();

    // ── 1. Scope switch ─────────────────────────────────────────────────────
    [Fact]
    public async Task Scope_Switch_Logs_Entry_With_Target_Companies()
    {
        var (_, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var (status, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/audit/scope-switch",
            new { companyIds = new[] { demoId } }, sa);
        Assert.Equal(200, status);

        var e = await PollAuditEntryAsync(sa, "scope.switched",
            x => StrArray(x.GetProperty("afterState"), "companyIds").Contains(demoId));
        Assert.Equal("scope.switched", e.GetProperty("actionCode").GetString());
        Assert.Equal("company", e.GetProperty("targetEntityType").GetString());
        Assert.Contains("SuperAdmin", e.GetProperty("actorRole").GetString()!);
        Assert.NotEmpty(e.GetProperty("actorUserId").GetString()!);
    }

    // ── 2. Role permission change with before/after diff ────────────────────
    [Fact]
    public async Task Role_Permission_Change_Logs_Added_And_Removed()
    {
        var (_, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var dashId = await PermIdAsync("dashboard.view");
        var vehId = await PermIdAsync("vehicle.view");
        var roleId = await CreateRoleAsync(sa, demoId, "Perm Diff " + Uniq(), new[] { "dashboard.view" });

        var (uStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/roles/{roleId}",
            new { permissionIds = new[] { dashId, vehId } }, sa);
        Assert.Equal(200, uStatus);

        var e = await PollAuditEntryAsync(sa, "role.permission_updated",
            x => x.GetProperty("targetEntityId").GetString() == roleId);
        Assert.Equal(roleId, e.GetProperty("targetEntityId").GetString());
        Assert.Equal(demoId, e.GetProperty("targetCompanyId").GetString());
        Assert.Equal("role", e.GetProperty("targetEntityType").GetString());
        // vehicle.view added
        Assert.Contains("vehicle.view", StrArray(e.GetProperty("afterState"), "permissionIds"));
        Assert.DoesNotContain("vehicle.view", StrArray(e.GetProperty("beforeState"), "permissionIds"));
        // dashboard.view retained in both
        Assert.Contains("dashboard.view", StrArray(e.GetProperty("beforeState"), "permissionIds"));
        Assert.Contains("dashboard.view", StrArray(e.GetProperty("afterState"), "permissionIds"));
    }

    // ── 3. User lifecycle: created / role changed / deactivated ─────────────
    [Fact]
    public async Task User_Lifecycle_Logs_Created_Role_Change_Deactivated()
    {
        var (roleId, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var email = $"audit.u{Uniq()}@demo.test";
        var userId = await CreateUserAsync(sa, demoId, email, new[] { roleId });

        var c = await PollAuditEntryAsync(sa, "user.created",
            x => x.GetProperty("targetEntityId").GetString() == userId);
        Assert.Equal(demoId, c.GetProperty("targetCompanyId").GetString());
        Assert.Equal("user", c.GetProperty("targetEntityType").GetString());
        Assert.Equal(SaEmail, c.GetProperty("actorEmail").GetString()); // actor = SuperAdmin who created the account

        // Role change: move the user to a second role
        var role2 = await CreateRoleAsync(sa, demoId, "Second Role " + Uniq(), new[] { "dashboard.view" });
        var (rStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/users/{userId}",
            new { roleIds = new[] { role2 } }, sa);
        Assert.Equal(200, rStatus);
        var rc = await PollAuditEntryAsync(sa, "user.role_changed",
            x => x.GetProperty("targetEntityId").GetString() == userId);
        Assert.Contains(role2, StrArray(rc.GetProperty("afterState"), "roleIds"));
        Assert.Contains(roleId, StrArray(rc.GetProperty("beforeState"), "roleIds"));

        // Deactivate
        var (dStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/users/{userId}",
            new { status = 1 }, sa);
        Assert.Equal(200, dStatus);
        var deactivated = await PollAuditAsync(sa, "user.deactivated");
        Assert.Contains(deactivated, x => x.GetProperty("targetEntityId").GetString() == userId);
    }

    // ── 4. Share link generate + revoke ─────────────────────────────────────
    [Fact]
    public async Task Share_Link_Logs_Generate_And_Revoke()
    {
        var (_, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var tripId = await CreateTripAsync(sa, demoId);

        var (lStatus, lData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/share-links", new { expiresInDays = 7 }, sa);
        Assert.Equal(200, lStatus);
        var linkId = lData!.Value.GetProperty("id").GetString()!;

        var g = await PollAuditEntryAsync(sa, "share_link.generated",
            x => x.GetProperty("targetEntityId").GetString() == linkId);
        Assert.Equal("sharelink", g.GetProperty("targetEntityType").GetString());
        Assert.Equal(demoId, g.GetProperty("targetCompanyId").GetString());

        var (vStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/share-links/{linkId}/revoke", null, sa);
        Assert.Equal(200, vStatus);
        var rev = await PollAuditAsync(sa, "share_link.revoked");
        Assert.Contains(rev, x => x.GetProperty("targetEntityId").GetString() == linkId);
    }

    private async Task<string> CreateTripAsync(string token, string companyId)
    {
        var uniq = Uniq();
        var (vStatus, vData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            new { registrationNumber = $"AUD-{uniq}", name = "audit vehicle", companyId }, token);
        Assert.Equal(201, vStatus);
        var vehicleId = vData!.Value.GetProperty("id").GetString()!;

        var (dStatus, dData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/drivers",
            new { employeeId = $"AUD-{uniq}", firstName = "Audit", lastName = "Driver", companyId }, token);
        Assert.Equal(201, dStatus);
        var driverId = dData!.Value.GetProperty("id").GetString()!;

        var (tStatus, tData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/trips",
            new
            {
                name = $"Audit Trip {uniq}",
                type = 0,
                companyId,
                vehicleId,
                driverId,
                waypoints = new[]
                {
                    new { sequenceOrder = 1, legType = 0, waypointType = 0, name = "Depot", latitude = 23.0225, longitude = 72.5714 },
                    new { sequenceOrder = 2, legType = 0, waypointType = 1, name = "Customer", latitude = 23.05, longitude = 72.62 }
                }
            }, token);
        Assert.Equal(201, tStatus);
        return tData!.Value.GetProperty("id").GetString()!;
    }

    // ── 5. Device vendor registry change ────────────────────────────────────
    [Fact]
    public async Task Device_Vendor_Change_Logs_Create_And_Status()
    {
        await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var code = "audit-vendor-" + Uniq();

        var (cStatus, cData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/admin/device-vendors",
            new { code, name = "Audit Vendor " + Uniq() }, sa);
        Assert.Equal(201, cStatus);
        var vendorId = cData!.Value.GetProperty("id").GetString()!;

        var e = await PollAuditEntryAsync(sa, "devicevendor.changed",
            x => x.GetProperty("targetEntityId").GetString() == vendorId);
        Assert.Equal("devicevendor", e.GetProperty("targetEntityType").GetString());
        Assert.Equal("create", e.GetProperty("action").GetString());

        // Deactivate the vendor (status 1 = Inactive) → status_changed entry
        var (uStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/admin/device-vendors/{vendorId}",
            new { status = 1 }, sa);
        Assert.Equal(200, uStatus);
        var statusChanged = await PollAuditAsync(sa, "devicevendor.status_changed");
        Assert.Contains(statusChanged, x => x.GetProperty("targetEntityId").GetString() == vendorId);
    }

    // ── 6. SuperAdmin cross-tenant write records the target company ─────────
    [Fact]
    public async Task Cross_Tenant_Write_Logs_Target_Company()
    {
        var (_, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var (status, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            new { registrationNumber = $"X-{Uniq()}", name = "cross-tenant vehicle", companyId = demoId }, sa);
        Assert.Equal(201, status);

        var e = await PollAuditEntryAsync(sa, "vehicle.created",
            x => x.GetProperty("targetCompanyId").GetString() == demoId);
        Assert.Equal("vehicle", e.GetProperty("targetEntityType").GetString());
        Assert.Contains("cross-tenant", e.GetProperty("source").GetString()!.ToLowerInvariant());
    }

    // ── 7. Append-only: no mutate verb exists on audit entries ──────────────
    [Fact]
    public async Task Append_Only_SuperAdmin_Cannot_Mutate_Entries()
    {
        var (_, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        // Seed one real entry so we have a stable id to attack.
        await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/audit/scope-switch",
            new { companyIds = new[] { demoId } }, sa);
        var items = await PollAuditAsync(sa, "scope.switched");
        var id = items.First().GetProperty("id").GetString()!;

        var attacks = new (HttpMethod M, string Url)[]
        {
            (HttpMethod.Put,    $"/api/v1/audit/{id}"),
            (HttpMethod.Delete, $"/api/v1/audit/{id}"),
            (HttpMethod.Patch,  $"/api/v1/audit/{id}"),
            (HttpMethod.Post,   "/api/v1/audit"),            // direct entry create
            (HttpMethod.Post,   $"/api/v1/audit/{id}/update"), // sneaky sub-path
        };
        foreach (var (m, url) in attacks)
        {
            var (status, _) = await ApiJson.SendAsync(_db.Client, m, url, new { actionCode = "tamper" }, sa);
            Assert.True(status is >= 400 and <= 599,
                $"{m} {url} must be rejected (got {status}) — audit entries are append-only");
            _output.WriteLine($"PASS  {m,-7} {url} → {status}");
        }
    }

    // ── 8. Company Admin isolation ──────────────────────────────────────────
    [Fact]
    public async Task Company_Admin_Sees_Only_Own_Company_And_No_Cross_Tenant_Metadata()
    {
        var (roleId, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var dashId = await PermIdAsync("dashboard.view");
        var auditPermId = await PermIdAsync("audit.view");

        // Grant audit.view to the company-admin role (possible only once the
        // permission + page exist — RED fails here first).
        var (rStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/roles/{roleId}",
            new { permissionIds = new[] { dashId, auditPermId } }, sa);
        Assert.Equal(200, rStatus);

        // Seed cross-tenant + scope metadata as SuperAdmin (demo-fleet targeted)
        await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/audit/scope-switch",
            new { companyIds = new[] { demoId } }, sa);
        await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            new { registrationNumber = $"ISO-{Uniq()}", name = "isolation vehicle", companyId = demoId }, sa);
        // Ensure both landed for SA
        await PollAuditAsync(sa, "scope.switched");
        await PollAuditAsync(sa, "vehicle.created");

        var coToken = await TokenAsync(CoAdminEmail);
        var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/audit?pageSize=100", null, coToken);
        Assert.Equal(200, status);

        var items = data!.Value.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        foreach (var e in items)
        {
            Assert.Equal(demoId, e.GetProperty("targetCompanyId").GetString());
            Assert.NotEqual("scope.switched", e.GetProperty("actionCode").GetString());
            var source = e.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
            Assert.DoesNotContain("cross-tenant", source.ToLowerInvariant());
        }
        // SA (same company targeted) sees the metadata the company admin cannot
        var saItems = await PollAuditAsync(sa, "scope.switched");
        Assert.NotEmpty(saItems);
    }

    // ── 9. Filters: action code / target company / date range ───────────────
    [Fact]
    public async Task Filters_By_Action_Company_And_Date_Range()
    {
        var (_, _, demoId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/audit/scope-switch",
            new { companyIds = new[] { demoId } }, sa);
        await PollAuditAsync(sa, "scope.switched");

        // actionCode filter
        var (s1, d1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/audit?actionCode=scope.switched&pageSize=100", null, sa);
        Assert.Equal(200, s1);
        var codeItems = d1!.Value.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(codeItems);
        Assert.All(codeItems, e => Assert.Equal("scope.switched", e.GetProperty("actionCode").GetString()));

        // targetCompanyId filter
        var (s2, d2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/audit?targetCompanyId={demoId}&pageSize=100", null, sa);
        Assert.Equal(200, s2);
        var companyItems = d2!.Value.GetProperty("items").EnumerateArray().ToList();
        Assert.All(companyItems, e => Assert.Equal(demoId, e.GetProperty("targetCompanyId").GetString()));

        // date range (yesterday → tomorrow covers everything written just now)
        var from = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var to = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var (s3, d3) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/audit?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&pageSize=100", null, sa);
        Assert.Equal(200, s3);
        var rangeItems = d3!.Value.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(rangeItems);
        Assert.All(rangeItems, e =>
        {
            var ts = DateTime.Parse(e.GetProperty("timestamp").GetString()!);
            Assert.InRange(ts, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(2));
        });

        // actor filter
        var (s4, d4) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/audit?actor={Uri.EscapeDataString(SaEmail)}&pageSize=100", null, sa);
        Assert.Equal(200, s4);
        var actorItems = d4!.Value.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(actorItems);
        Assert.All(actorItems, e => Assert.Equal(SaEmail, e.GetProperty("actorEmail").GetString()));
    }
}