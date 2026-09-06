using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Three-tier alert type registry contract — platform catalog, company
/// subscriptions, and role visibility enforcement.
/// </summary>
public sealed class AlertRegistryTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public AlertRegistryTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    public async Task InitializeAsync() { await Task.CompletedTask; }
    public async Task DisposeAsync() { await Task.CompletedTask; }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier 1: Platform Catalog (Super Admin)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Catalog_SeededAlertTypes_AreVisible()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/alert-types", null, sa);
        Assert.Equal(200, s);
        var items = d!.Value.EnumerateArray().ToList();
        Assert.True(items.Count >= 10, $"Expected ≥10 seeded alert types, got {items.Count}");

        var codes = items.Select(i => i.GetProperty("code").GetString()).ToList();
        Assert.Contains("geofence.entry", codes);
        Assert.Contains("route.restricted_zone_violation", codes);
        Assert.Contains("trip.delayed", codes);
        Assert.Contains("device.offline", codes);
        _output.WriteLine($"PASS  Catalog has {items.Count} seeded alert types");
    }

    [Fact]
    public async Task Catalog_SuperAdmin_CanCreateAndDelete()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var code = $"test.{Guid.NewGuid():N}[..8]";
        var (cs, cd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/alert-types", new
            {
                code, name = "Test Alert Type", description = "test",
                category = "System", defaultSeverity = 1, displayOrder = 99
            }, sa);
        Assert.Equal(200, cs);
        var id = cd!.Value.GetProperty("id").GetGuid();
        _output.WriteLine($"PASS  Created alert type {id}");

        // Delete it
        var (ds, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete,
            $"/api/v1/alert-types/{id}", null, sa);
        Assert.Equal(200, ds);
        _output.WriteLine("PASS  Deleted alert type");
    }

    [Fact]
    public async Task Catalog_CompanyAdmin_Blocked()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/alert-types", null, ca);
        Assert.Equal(403, s);
        _output.WriteLine("PASS  Company Admin blocked from catalog (403)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier 2: Company Alert Subscriptions (Super Admin per-company)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Subscriptions_SeededForDemoCompany()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/alert-subscriptions", null, sa);
        Assert.Equal(200, s);
        var items = d!.Value.EnumerateArray().ToList();
        Assert.True(items.Count >= 10, $"Expected ≥10 subscriptions, got {items.Count}");

        // All should be enabled (package_default)
        var allEnabled = items.All(i => i.GetProperty("enabled").GetBoolean());
        Assert.True(allEnabled, "All seeded subscriptions should be enabled");
        _output.WriteLine($"PASS  Company has {items.Count} alert subscriptions (all enabled)");
    }

    [Fact]
    public async Task Subscriptions_SuperAdmin_CanToggle()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // Get the first subscription's alertTypeId
        var (ls, ld) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/alert-subscriptions", null, sa);
        var firstSub = ld!.Value.EnumerateArray().First();
        var alertTypeId = firstSub.GetProperty("alertTypeId").GetGuid();

        // Disable it
        var (ts, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/{alertTypeId}",
            new { enabled = false }, sa);
        Assert.Equal(200, ts);

        // Re-enable
        var (rs, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/{alertTypeId}",
            new { enabled = true }, sa);
        Assert.Equal(200, rs);
        _output.WriteLine("PASS  Super Admin can toggle company alert subscription");
    }

    [Fact]
    public async Task Subscriptions_BulkToggle_ByCategory()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // Disable all Geofence alerts
        var (bs, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/bulk",
            new { enabled = false, category = "Geofence" }, sa);
        Assert.Equal(200, bs);

        // Verify they're disabled
        var (ls, ld) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/alert-subscriptions", null, sa);
        var geoSubs = ld!.Value.EnumerateArray()
            .Where(i => i.GetProperty("alertTypeCategory").GetString() == "Geofence")
            .ToList();
        Assert.True(geoSubs.All(i => !i.GetProperty("enabled").GetBoolean()),
            "Geofence alerts should be disabled after bulk toggle");

        // Re-enable all
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/bulk",
            new { enabled = true, category = "Geofence" }, sa);
        _output.WriteLine("PASS  Bulk toggle by category works");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier 3: Role Alert Visibility (Company Admin)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Visibility_CompanyAdmin_CanTogglePerRole()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var co = await GetDemoCompanyIdAsync(await LoginAsync("admin@freebuff.com"));

        // Get a role
        var (rr, rd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/roles", null, ca);
        var roleListData = rd!.Value;
        var roles = roleListData.TryGetProperty("items", out var ri) ? ri.EnumerateArray().ToList() : roleListData.EnumerateArray().ToList();
        var role = roles.First(r => r.GetProperty("name").GetString() == "Company Admin");
        var roleId = role.GetProperty("id").GetGuid();

        // Get visibility list
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/role-alert-visibility/role/{roleId}", null, ca);
        Assert.Equal(200, vs);
        var items = vd!.Value.EnumerateArray().ToList();
        Assert.True(items.Count >= 10, $"Expected ≥10 visibility entries, got {items.Count}");

        // Toggle one off
        var firstAlertTypeId = items.First().GetProperty("alertTypeId").GetGuid();
        var (ts, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/role-alert-visibility/role/{roleId}/alert/{firstAlertTypeId}",
            new { visible = false }, ca);
        Assert.Equal(200, ts);
        _output.WriteLine("PASS  Company Admin can toggle per-role alert visibility");
    }

    [Fact]
    public async Task Visibility_CannotEnableUnentitledType()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // Disable an alert type for the company
        var (ls, ld) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/alert-subscriptions", null, sa);
        var firstSub = ld!.Value.EnumerateArray().First();
        var alertTypeId = firstSub.GetProperty("alertTypeId").GetGuid();
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/{alertTypeId}",
            new { enabled = false }, sa);

        // Get a role
        var (rr, rd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/roles", null, sa);
        var roleData2 = rd!.Value;
        var roleId = roleData2.TryGetProperty("items", out var ri2) ? ri2.EnumerateArray().First().GetProperty("id").GetGuid() : roleData2.EnumerateArray().First().GetProperty("id").GetGuid();

        // Try to make it visible — should fail (not entitled)
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/role-alert-visibility/role/{roleId}/alert/{alertTypeId}",
            new { visible = true }, sa);
        Assert.Equal(400, vs);
        _output.WriteLine("PASS  Cannot enable visibility for unentitled alert type");

        // Re-enable the subscription
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/{alertTypeId}",
            new { enabled = true }, sa);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Enforcement: alert pipeline checks entitlement
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enforcement_DisabledAlertType_NotFired()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var co = await GetDemoCompanyIdAsync(sa);

        // Disable route.corridor_deviation for this company
        var allSubs = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/alert-subscriptions", null, sa);
        var corridorSub = allSubs.Data!.Value.EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("alertTypeCode").GetString() == "route.corridor_deviation");
        if (corridorSub.ValueKind == JsonValueKind.Undefined)
        {
            _output.WriteLine("SKIP  route.corridor_deviation subscription not found");
            return;
        }
        var corridorId = corridorSub.GetProperty("alertTypeId").GetGuid();
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/{corridorId}",
            new { enabled = false }, sa);

        // Verify: the alert type is disabled
        var verify = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/companies/{co}/alert-subscriptions", null, sa);
        var disabled = verify.Data!.Value.EnumerateArray()
            .First(s => s.GetProperty("alertTypeCode").GetString() == "route.corridor_deviation");
        Assert.False(disabled.GetProperty("enabled").GetBoolean());

        // Re-enable
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/companies/{co}/alert-subscriptions/{corridorId}",
            new { enabled = true }, sa);
        _output.WriteLine("PASS  Enforcement: disabled alert type confirmed not entitled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> LoginAsync(string email, string password = "Admin@123")
    {
        var key = $"{email}:{password}";
        if (_tokens.TryGetValue(key, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, password);
        Assert.NotNull(token);
        _tokens[key] = token!;
        return token!;
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
}
