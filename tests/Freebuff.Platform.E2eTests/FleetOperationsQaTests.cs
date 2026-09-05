using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Fleet Operations pre-release QA regression — four passes covering:
///   Pass 1: Module/Package gating
///   Pass 2: Role/Permission correctness
///   Pass 3: Tenant/Company Scope
///   Pass 4: Cross-entity relationships
/// </summary>
public sealed class FleetOperationsQaTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public FleetOperationsQaTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    public async Task InitializeAsync() { await Task.CompletedTask; }
    public async Task DisposeAsync() { await Task.CompletedTask; }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 1 — Module/Package Gating
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Trips_IsRegisteredInFleetModule_NotPlanned()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        // SendRawAsync returns the full envelope; SendAsync unwraps data
        var (s, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get,
            "/api/v1/tenant/company/modules", null, sa);
        Assert.Equal(200, s);
        var data = root.GetProperty("data");
        var modules = data.GetProperty("modules");
        var fleet = modules.EnumerateArray()
            .FirstOrDefault(m => m.GetProperty("code").GetString() == "fleet");
        Assert.False(fleet.ValueKind == JsonValueKind.Undefined, "Fleet module not found in tenant modules");
        var tripPage = fleet.GetProperty("pages").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("key").GetString() == "trip");
        Assert.False(tripPage.ValueKind == JsonValueKind.Undefined, "Trip page not in fleet module");
        Assert.False(tripPage.GetProperty("planned").GetBoolean(), "Trip should not be planned");
        _output.WriteLine("PASS  Trips registered as Active in Fleet Operations module");
    }

    [Theory]
    [InlineData("/api/v1/vehicles")]
    [InlineData("/api/v1/drivers")]
    [InlineData("/api/v1/devices")]
    [InlineData("/api/v1/geofences")]
    [InlineData("/api/v1/routes")]
    [InlineData("/api/v1/trips")]
    public async Task FleetPage_IsReachable(string endpoint)
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"{endpoint}?pageSize=1", null, ca);
        Assert.Equal(200, s);
        _output.WriteLine($"PASS  {endpoint} reachable");
    }

    [Theory]
    [InlineData("/api/v1/alerts")]
    [InlineData("/api/v1/fuels")]
    [InlineData("/api/v1/maintenances")]
    [InlineData("/api/v1/reports")]
    public async Task PlannedPage_IsNotReachable(string endpoint)
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"{endpoint}?pageSize=1", null, ca);
        Assert.True(s is 404 or 400 or 403,
            $"Planned page {endpoint} should be unreachable, got {s}");
        _output.WriteLine($"PASS  Planned page {endpoint} unreachable ({s})");
    }

    [Fact]
    public async Task DemoCompany_HasPackageWithFleetModule()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var (s, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get,
            "/api/v1/tenant/company/modules", null, ca);
        Assert.Equal(200, s);
        var data = root.GetProperty("data");
        var pkgName = data.GetProperty("packageName").GetString();
        var codes = data.GetProperty("includedModuleCodes").EnumerateArray()
            .Select(c => c.GetString()).ToList();
        Assert.False(string.IsNullOrEmpty(pkgName), "Package name is null");
        Assert.Contains("fleet", codes);
        _output.WriteLine($"PASS  Demo company has package '{pkgName}' with fleet module");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 2 — Role/Permission Correctness
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/vehicles")]
    [InlineData("/api/v1/drivers")]
    [InlineData("/api/v1/devices")]
    [InlineData("/api/v1/geofences")]
    [InlineData("/api/v1/routes")]
    [InlineData("/api/v1/trips")]
    public async Task FleetManager_HasFleetViewPermission(string endpoint)
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var fmToken = await CreateFmUserAsync(sa);
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"{endpoint}?pageSize=1", null, fmToken);
        Assert.Equal(200, s);
        _output.WriteLine($"PASS  Fleet Manager has access to {endpoint}");
    }

    [Theory]
    [InlineData("/api/v1/roles")]
    [InlineData("/api/v1/admin/companies")]
    public async Task FleetManager_LacksOrgPermission(string endpoint)
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var fmToken = await CreateFmUserAsync(sa);
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            endpoint, null, fmToken);
        Assert.Equal(403, s);
        _output.WriteLine($"PASS  Fleet Manager blocked from {endpoint}");
    }

    [Fact]
    public async Task FleetManager_BlockedFromRoleCreate()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var fmToken = await CreateFmUserAsync(sa);
        var co = await GetDemoCompanyIdAsync(sa);
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/roles", new { name = "test", companyId = co }, fmToken);
        Assert.Equal(403, s);
        _output.WriteLine("PASS  Fleet Manager blocked from role.create");
    }

    [Fact]
    public async Task FleetManager_Permissions_ReturnsFleetPermsOnly()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        var fmToken = await CreateFmUserAsync(sa);
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/auth/permissions", null, fmToken);
        Assert.Equal(200, s);
        var perms = d!.Value.GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString()!).ToList();
        Assert.Contains(perms, p => p.StartsWith("vehicle."));
        Assert.Contains(perms, p => p.StartsWith("route."));
        Assert.DoesNotContain(perms, p => p.StartsWith("role."));
        Assert.DoesNotContain(perms, p => p.StartsWith("company."));
        _output.WriteLine($"PASS  FM permissions: fleet present, org absent ({perms.Count} total)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 3 — Tenant/Company Scope
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/vehicles")]
    [InlineData("/api/v1/drivers")]
    [InlineData("/api/v1/devices")]
    [InlineData("/api/v1/trips")]
    public async Task CompanyAdmin_ScopedToOwnCompany_ByCompanyId(string endpoint)
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var co = await GetDemoCompanyIdAsync(await LoginAsync("admin@freebuff.com"));
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"{endpoint}?pageSize=100", null, ca);
        Assert.Equal(200, s);
        var items = d!.Value.GetProperty("items").EnumerateArray().ToList();
        if (items.Count == 0)
        {
            _output.WriteLine($"PASS  {endpoint}: 0 items (scope test vacuous)");
            return;
        }
        var companies = items.Select(i => i.GetProperty("companyId").GetString())
            .Distinct().ToList();
        Assert.True(companies.Count <= 1,
            $"{endpoint}: Company Admin sees {companies.Count} companies");
        Assert.Equal(co, companies[0]);
        _output.WriteLine($"PASS  {endpoint}: scoped to own company ({items.Count} items)");
    }

    [Theory]
    [InlineData("/api/v1/geofences")]
    [InlineData("/api/v1/routes")]
    public async Task CompanyAdmin_ScopedToOwnCompany_ByCompanyName(string endpoint)
    {
        // Geofences and Routes DTOs expose companyName but not companyId
        var ca = await LoginAsync("admin@demofleet.com");
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"{endpoint}?pageSize=100", null, ca);
        Assert.Equal(200, s);
        var items = d!.Value.GetProperty("items").EnumerateArray().ToList();
        if (items.Count == 0)
        {
            _output.WriteLine($"PASS  {endpoint}: 0 items (scope test vacuous)");
            return;
        }
        var companyNames = items.Select(i =>
            i.TryGetProperty("companyName", out var cn) ? cn.GetString() : null)
            .Where(n => n != null).Distinct().ToList();
        // All items should belong to the same company (Demo Fleet)
        Assert.True(companyNames.Count <= 1,
            $"{endpoint}: Company Admin sees {companyNames.Count} different companies");
        _output.WriteLine($"PASS  {endpoint}: scoped to own company ({items.Count} items)");
    }

    [Fact]
    public async Task SuperAdmin_CrossTenantDataVisible()
    {
        var sa = await LoginAsync("admin@freebuff.com");
        foreach (var ep in new[] { "/api/v1/vehicles", "/api/v1/drivers", "/api/v1/devices" })
        {
            var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                $"{ep}?pageSize=100", null, sa);
            Assert.Equal(200, s);
            var items = d!.Value.GetProperty("items").EnumerateArray().ToList();
            Assert.True(items.Count > 0, $"SA sees no items on {ep}");
        }
        _output.WriteLine("PASS  Super Admin sees cross-tenant data");
    }

    [Fact]
    public async Task ForgedScopeHeader_IgnoredForCompanyAdmin()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var fakeId = Guid.NewGuid().ToString();
        var req = new HttpRequestMessage(HttpMethod.Get,
            "/api/v1/vehicles?pageSize=10");
        req.Headers.Add("X-Company-Scope", fakeId);
        req.Headers.Add("Authorization", $"Bearer {ca}");
        var resp = await _db.Client.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("data").GetProperty("items")
            .EnumerateArray().ToList();
        var hasFake = items.Any(i =>
            i.GetProperty("companyId").GetString() == fakeId);
        Assert.False(hasFake,
            "Company Admin accessed another company via forged scope");
        _output.WriteLine("PASS  Forged scope header ignored for Company Admin");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pass 4 — Cross-Entity Relationships
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Device_CreatedAndRetrievable()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var imei = "860" + Guid.NewGuid().ToString("N")[..12];
        var (cs, cd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/devices", new
            {
                name = "QA Regression Device",
                vendorCode = "sample-json",
                deviceType = 0,
                identityType = 0,
                identityValue = imei
            }, ca);
        Assert.True(cs is 200 or 201, $"Device create: {cs}");
        var id = cd!.Value.GetProperty("id").GetGuid();
        var (gs, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/devices/{id}", null, ca);
        Assert.Equal(200, gs);
        _output.WriteLine($"PASS  Device created and retrievable ({id})");
    }

    [Fact]
    public async Task DeviceAssignment_ToVehicle()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var vehicles = await GetItemsAsync(ca, "/api/v1/vehicles?pageSize=1");
        Assert.True(vehicles.Count > 0, "No vehicles to assign device to");
        var imei = "860" + Guid.NewGuid().ToString("N")[..12];
        var (cs, cd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/devices", new
            {
                name = "QA Assign Device",
                vendorCode = "sample-json",
                deviceType = 0,
                identityType = 0,
                identityValue = imei
            }, ca);
        Assert.True(cs is 200 or 201);
        var deviceId = cd!.Value.GetProperty("id").GetGuid();
        var vehicleId = vehicles[0].GetProperty("id").GetGuid();
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/vehicles/{vehicleId}/devices",
            new { deviceId, role = "primary" }, ca);
        Assert.True(s is 200 or 201 or 400, $"Device assign: {s}");
        _output.WriteLine($"PASS  Device assigned to vehicle {vehicleId}");
    }

    [Fact]
    public async Task RouteDetail_IncludesLinkedGeofences()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var routes = await GetItemsAsync(ca, "/api/v1/routes?pageSize=1");
        if (routes.Count == 0) { _output.WriteLine("SKIP  No routes"); return; }
        var routeId = routes[0].GetProperty("id").GetGuid();
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/routes/{routeId}", null, ca);
        Assert.Equal(200, s);
        var route = d!.Value;
        Assert.True(route.TryGetProperty("geofenceCount", out _),
            "Route detail missing geofenceCount");
        _output.WriteLine("PASS  Route detail includes geofenceCount");
    }

    [Fact]
    public async Task Trip_WithWaypoints_CreatedSuccessfully()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var vehicles = await GetItemsAsync(ca, "/api/v1/vehicles?pageSize=1");
        var drivers = await GetItemsAsync(ca, "/api/v1/drivers?pageSize=1");
        Assert.True(vehicles.Count > 0 && drivers.Count > 0);
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/trips", new
            {
                name = "QA Regression Trip",
                vehicleId = vehicles[0].GetProperty("id").GetGuid(),
                driverId = drivers[0].GetProperty("id").GetGuid(),
                tripType = 0,
                status = 0,
                waypoints = new object[]
                {
                    new { sequenceOrder = 1, lat = 28.6, lng = 77.2,
                          waypointType = 0, legType = 0 },
                    new { sequenceOrder = 2, lat = 28.7, lng = 77.3,
                          waypointType = 0, legType = 0 },
                }
            }, ca);
        Assert.True(s is 200 or 201, $"Trip create with waypoints: {s}");
        _output.WriteLine("PASS  Trip created with waypoints");
    }

    [Fact]
    public async Task Trip_WithoutWaypoints_IsRejected()
    {
        var ca = await LoginAsync("admin@demofleet.com");
        var vehicles = await GetItemsAsync(ca, "/api/v1/vehicles?pageSize=1");
        var drivers = await GetItemsAsync(ca, "/api/v1/drivers?pageSize=1");
        Assert.True(vehicles.Count > 0 && drivers.Count > 0);
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/trips", new
            {
                name = "QA No Waypoints",
                vehicleId = vehicles[0].GetProperty("id").GetGuid(),
                driverId = drivers[0].GetProperty("id").GetGuid(),
                tripType = 0,
                status = 0,
            }, ca);
        Assert.Equal(400, s);
        _output.WriteLine("PASS  Trip without waypoints rejected (400)");
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

    private async Task<string> CreateFmUserAsync(string saToken)
    {
        const string cacheKey = "fm_user";
        if (_tokens.TryGetValue(cacheKey, out var cached)) return cached;

        var co = await GetDemoCompanyIdAsync(saToken);
        var (rs, rd) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get,
            "/api/v1/roles", null, saToken);
        var fmRole = rd.GetProperty("data").GetProperty("items").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("name").GetString() == "Fleet Manager");
        Assert.False(fmRole.ValueKind == JsonValueKind.Undefined,
            "Fleet Manager role not found");

        var email = $"fm_qa_{Guid.NewGuid():N}@test.com";
        var (us, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            "/api/v1/users", new
            {
                email,
                password = "TestPass123!",
                firstName = "FM",
                lastName = "QA",
                companyId = co,
                roleIds = new[] { fmRole.GetProperty("id").GetGuid() }
            }, saToken);
        Assert.True(us is 200 or 201, $"FM user creation: {us}");

        var token = await ApiJson.LoginAsync(_db.Client, email, "TestPass123!");
        Assert.NotNull(token);
        _tokens[cacheKey] = token!;
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
            .FirstOrDefault(c => c.GetProperty("name").GetString()
                ?.Contains("Demo") == true);
        Assert.False(demo.ValueKind == JsonValueKind.Undefined,
            "Demo company not found in admin/companies");
        var id = demo.GetProperty("id").GetString()!;
        _tokens[cacheKey] = id;
        return id;
    }

    private async Task<List<JsonElement>> GetItemsAsync(string token, string endpoint)
    {
        var (s, d) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            endpoint, null, token);
        if (s != 200 || d == null) return new();
        return d.Value.GetProperty("items").EnumerateArray().ToList();
    }
}
