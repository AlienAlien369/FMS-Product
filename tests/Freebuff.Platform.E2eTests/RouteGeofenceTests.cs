using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Route to Geofence linking contract — semantic roles (Checkpoint with
/// sequence order, RestrictedZone, Start/EndZone), tenant isolation, and the
/// validation rules from the Route-linking spec:
///   - linking requires the route to define origin + destination first
///   - one geofence per route (checkpoint AND restricted for the same fence is
///     contradictory → rejected)
///   - checkpoint sequence numbers must not collide
///   - every linked geofence belongs to the route's company
///   - replace-all PUT keeps list count indicators in sync
///   - other-company admins cannot touch a route's links (404)
/// </summary>
public sealed class RouteGeofenceTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public RouteGeofenceTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    public Task InitializeAsync() => RbacFixtures.SeedAsync(_db);
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private async Task<Guid> GuidScalarAsync(string sql)
    {
        var raw = await _db.ScalarAsync(sql)
            ?? throw new Xunit.Sdk.XunitException($"Lookup returned no row: {sql}");
        return Guid.Parse(raw);
    }

    private static object Link(Guid geofenceId, int role, int? sequenceOrder = null) =>
        new { geofenceId, role, sequenceOrder };

    [Fact]
    public async Task Link_ReplaceValidate_AndListIndicators_WorkEndToEnd()
    {
        const string demoEmail = "admin@demofleet.com"; // Demo Fleet Company Admin
        var token = await TokenAsync(demoEmail);
        var suffix = Unique();

        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");
        var fenceA = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = '{demoCompany}' ORDER BY \"Name\" LIMIT 1 OFFSET 0");
        var fenceB = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = '{demoCompany}' ORDER BY \"Name\" LIMIT 1 OFFSET 1");
        var fenceC = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = '{demoCompany}' ORDER BY \"Name\" LIMIT 1 OFFSET 2");
        // Other-company fence: seed companies have no geofences, so insert one
        // directly for E2E Basic Co (same pattern other suites use for fixtures).
        var basicCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'e2e-basic'");
        var foreignFence = Guid.NewGuid();
        await _db.ExecuteAsync($$"""
            INSERT INTO "Geofences"
                ("Id", "Name", "Type", "Status", "Coordinates", "CenterLatitude", "CenterLongitude", "Radius",
                 "ViolationCount", "CompanyId", "TenantId", "CreatedAt", "UpdatedAt", "IsDeleted", "Version")
            VALUES
                ('{{foreignFence}}', 'E2E Foreign Fence {{suffix}}', 0, 0, '[]', 10.0, 77.0, 500,
                 0, '{{basicCompany}}', '{{basicCompany}}', now(), now(), false, 0)
            """);
        _output.WriteLine($"fences A={fenceA} B={fenceB} C={fenceC} foreign={foreignFence} (company {basicCompany})");

        // ── 1. Create a route with origin + destination ──
        var (createStatus, createRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/routes", new
        {
            name = $"E2E RouteGeofence {suffix}",
            type = 0,
            originName = "Depot A",
            originLatitude = 23.0,
            originLongitude = 72.5,
            destinationName = "Warehouse B",
            destinationLatitude = 23.3,
            destinationLongitude = 72.9,
        }, token);
        Assert.True(createStatus == 201,
            $"route create status={createStatus} body={createRoot.GetRawText()}");
        var createData = createRoot.GetProperty("data");
        var routeId = createData.GetProperty("id").GetGuid();

        // ── 2. Linking is gated on origin + destination being defined ──
        // (An origin-less route can't even be created — OriginName is [Required].
        //  An origin-ONLY route can, and must refuse links until a destination
        //  exists: checkpoints have no meaning without a defined start→end path.)
        var (partialStatus, partialData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/routes",
            new { name = $"E2E OriginOnly {suffix}", originName = "Depot A", originLatitude = 23.0, originLongitude = 72.5 }, token);
        Assert.Equal(201, partialStatus);
        var partialId = partialData!.Value.GetProperty("id").GetGuid();
        var (gateStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{partialId}/geofences", new object[] { Link(fenceA, 0, 1) }, token);
        Assert.Equal(400, gateStatus);
        _output.WriteLine("PASS  link rejected on a route without a destination → 400");

        // ── 3. Fresh route has no links ──
        var (emptyStatus, emptyData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/routes/{routeId}/geofences", null, token);
        Assert.Equal(200, emptyStatus);
        Assert.Equal(0, emptyData!.Value.GetArrayLength());

        // ── 4. Happy path: A+B ordered checkpoints, C restricted zone ──
        var (linkStatus, _) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences",
            new object[] { Link(fenceA, 0, 1), Link(fenceB, 0, 2), Link(fenceC, 1) }, token);
        Assert.Equal(200, linkStatus);
        _output.WriteLine("PASS  link 3 fences (2 checkpoints + 1 restricted) → 200");

        // ── 5. Read back: roles resolved, checkpoints ordered by sequence ──
        var (readStatus, readData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/routes/{routeId}/geofences", null, token);
        Assert.Equal(200, readStatus);
        var rows = readData!.Value.EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Checkpoint", rows[0].GetProperty("roleName").GetString());
        Assert.Equal(1, rows[0].GetProperty("sequenceOrder").GetInt32());
        Assert.Equal("Checkpoint", rows[1].GetProperty("roleName").GetString());
        Assert.Equal(2, rows[1].GetProperty("sequenceOrder").GetInt32());
        Assert.Equal("RestrictedZone", rows[2].GetProperty("roleName").GetString());
        Assert.True(rows[0].GetProperty("geofenceName").GetString()!.Length > 0);
        Assert.True(rows[0].GetProperty("geofenceType").GetInt32() is 0 or 2);
        _output.WriteLine("PASS  GET returns 3 links with resolved role/name/sequence");

        // ── 6. Route list rows carry the count indicator ──
        var searchTerm = $"E2E RouteGeofence {suffix}";
        var (pageStatus, pageData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/routes?search={Uri.EscapeDataString(searchTerm)}", null, token);
        Assert.Equal(200, pageStatus);
        var row = pageData!.Value.GetProperty("items").EnumerateArray().First();
        Assert.Equal(3, row.GetProperty("geofenceCount").GetInt32());
        Assert.Equal(2, row.GetProperty("checkpointCount").GetInt32());
        Assert.Equal(1, row.GetProperty("restrictedZoneCount").GetInt32());
        _output.WriteLine("PASS  list indicator: 3 linked · 2 checkpoints · 1 restricted");

        // ── 7. Duplicate sequence numbers rejected ──
        var (dupSeqStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences",
            new object[] { Link(fenceA, 0, 1), Link(fenceB, 0, 1) }, token);
        Assert.Equal(400, dupSeqStatus);
        _output.WriteLine("PASS  duplicate checkpoint sequence → 400");

        // ── 8. Same geofence twice with contradictory roles rejected ──
        var (dupFenceStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences",
            new object[] { Link(fenceA, 0, 1), Link(fenceA, 1) }, token);
        Assert.Equal(400, dupFenceStatus);
        _output.WriteLine("PASS  same fence as checkpoint + restricted → 400");

        // ── 9. Cross-company geofence rejected ──
        var (foreignStatus, foreignRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences", new object[] { Link(foreignFence, 0, 1) }, token);
        Assert.Equal(400, foreignStatus);
        var foreignError = foreignRoot.GetProperty("message").GetString();
        _output.WriteLine($"PASS  other-company geofence → 400 ({foreignError})");

        // ── 10. Replace-all with [] clears links ──
        var (clearStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences", new object[0], token);
        Assert.Equal(200, clearStatus);
        var (afterClear, afterData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/routes/{routeId}/geofences", null, token);
        Assert.Equal(200, afterClear);
        Assert.Equal(0, afterData!.Value.GetArrayLength());
        _output.WriteLine("PASS  replace-all with [] clears links");

        // ── 11. Other-company admin cannot modify this route's links ──
        var basicToken = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var (crossTenantStatus, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences", new object[] { Link(fenceA, 0) }, basicToken);
        Assert.Equal(404, crossTenantStatus);
        _output.WriteLine("PASS  other-company admin PUT on this route's geofences → 404");
    }
}
