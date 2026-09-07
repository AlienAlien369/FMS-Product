using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Proof-of-delivery contract over the real API + Postgres:
///   - Capture signature / photo / OTP code end-to-end (real evidence records)
///   - Geolocation mismatch flagging (data-quality signal, never a hard block)
///   - Delivery waypoint configured to require POD blocks completion until captured
///   - POD records are tenant-isolated (other company → 404) and permission-gated
///     (roles without pod.view/pod.create → 403)
/// </summary>
public sealed class ProofOfDeliveryE2eTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public ProofOfDeliveryE2eTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    public Task InitializeAsync() => RbacFixtures.SeedAsync(_db);
    public Task DisposeAsync() => Task.CompletedTask;

    private const string DemoEmail = "admin@demofleet.com";

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> GuidScalarAsync(string sql)
        => Guid.Parse(await _db.ScalarAsync(sql) ?? throw new Xunit.Sdk.XunitException($"Lookup returned no row: {sql}"));

    private async Task<(Guid Vehicle, Guid Driver)> CreateFleetPairAsync(string token, string suffix)
    {
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles", new
        {
            registrationNumber = $"POD-{Unique()}", name = $"POD Vehicle {suffix}",
            vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1,
        }, token);
        Assert.True(vs is 200 or 201, $"vehicle create status={vs}");
        var vehicleId = vd!.Value.GetProperty("id").GetGuid();

        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/drivers", new
        {
            employeeId = $"POD-{Unique()}", firstName = "POD", lastName = $"Driver {suffix}",
            email = $"e2e.pod.{Unique()}@test.dev",
        }, token);
        Assert.True(ds is 200 or 201, $"driver create status={ds}");
        var driverId = dd!.Value.GetProperty("id").GetGuid();
        return (vehicleId, driverId);
    }

    /// <summary>Creates an in-progress trip with a delivery waypoint (and one linked geofence to satisfy scheduling).</summary>
    private async Task<(Guid TripId, Guid DeliveryWaypointId)> NewInProgressDeliveryTripAsync(string token, string suffix, bool requirePod = false)
    {
        var (vehicleId, driverId) = await CreateFleetPairAsync(token, suffix);
        var fence = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = (SELECT \"Id\" FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet') ORDER BY \"Name\" LIMIT 1");

        var (status, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/trips", new
        {
            name = $"POD Trip {suffix}",
            type = 0,
            vehicleId,
            driverId,
            requirePodForDelivery = requirePod,
            waypoints = new object[]
            {
                new { sequenceOrder = 1, legType = 0, waypointType = 0, name = "Depot", latitude = 23.0225, longitude = 72.5714 },
                new { sequenceOrder = 2, legType = 0, waypointType = 1, name = "Customer A", latitude = 23.05, longitude = 72.62, customerPhone = "+919876543210", customerEmail = "cust@example.com" },
            },
            geofenceLinks = new object[] { new { geofenceId = fence, role = 0, sequenceOrder = 1 } },
        }, token);
        Assert.True(status == 201, $"trip create status={status} body={root.GetRawText()}");
        var tripId = root.GetProperty("data").GetProperty("id").GetGuid();

        var (s1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{tripId}/status", new { status = 1 }, token);
        Assert.Equal(200, s1);
        var (s2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{tripId}/status", new { status = 2 }, token);
        Assert.Equal(200, s2);

        var (ds, ddata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, ds);
        var deliveryWp = ddata!.Value.GetProperty("waypoints").EnumerateArray()
            .First(w => w.GetProperty("waypointType").GetInt32() == 1);
        return (tripId, deliveryWp.GetProperty("id").GetGuid());
    }

    // ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CaptureAllThreePodTypes_WithMismatchFlag_EndToEnd()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (tripId, wpId) = await NewInProgressDeliveryTripAsync(token, suffix);
        _output.WriteLine($"PASS  in-progress delivery trip {tripId} / waypoint {wpId}");

        // 1. Signature — captured ~50 km from the waypoint → flagged as mismatch.
        var (sigS, sigData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/signature",
            new { signatureSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 40\"><path d=\"M 10 30 L 30 10 L 50 25\" stroke=\"#000\" fill=\"none\"/></svg>", latitude = 22.5, longitude = 72.57, notes = "signed at gate" }, token);
        Assert.True(sigS == 200, $"signature status={sigS} body={sigData?.GetRawText()}");
        Assert.Equal(0, sigData!.Value.GetProperty("type").GetInt32());
        Assert.True(sigData.Value.GetProperty("verified").GetBoolean());
        Assert.True(sigData.Value.GetProperty("locationMismatch").GetBoolean());
        Assert.Contains("km from the waypoint", sigData.Value.GetProperty("locationMismatchDetail").GetString());
        Assert.Equal("signed at gate", sigData.Value.GetProperty("notes").GetString());
        _output.WriteLine("PASS  signature captured + geolocation mismatch flagged (~50 km away)");

        // 2. Photo — captured at the waypoint → no mismatch.
        var (phS, phData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/photo",
            new { imageUrl = "https://cdn.example/pod/photo-" + suffix + ".jpg", latitude = 23.05, longitude = 72.62 }, token);
        Assert.True(phS == 200, $"photo status={phS} body={phData?.GetRawText()}");
        Assert.Equal(1, phData!.Value.GetProperty("type").GetInt32());
        Assert.False(phData.Value.GetProperty("locationMismatch").GetBoolean());
        Assert.Contains("https://cdn.example/pod/", phData.Value.GetProperty("imageUrl").GetString());
        _output.WriteLine("PASS  photo captured, stored image reference, no mismatch at waypoint");

        // 3. OTP — issue, then verify with the returned code.
        var (otpS, otpData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/otp", null, token);
        Assert.True(otpS == 200, $"otp issue status={otpS} body={otpData?.GetRawText()}");
        var otpCode = otpData!.Value.GetProperty("otp").GetString()!;
        Assert.Equal(6, otpCode.Length);
        Assert.Contains("+919876543210", otpData.Value.GetProperty("customerChannel").GetString());

        var (verifyS, verifyData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/otp/verify",
            new { code = otpCode, latitude = 23.05, longitude = 72.62 }, token);
        Assert.True(verifyS == 200, $"otp verify status={verifyS} body={verifyData?.GetRawText()}");
        Assert.True(verifyData!.Value.GetProperty("otpVerified").GetBoolean());
        Assert.Equal(2, verifyData.Value.GetProperty("type").GetInt32());
        _output.WriteLine("PASS  OTP issued (code relayed in dev) + verified → evidence recorded");

        // Wrong code is rejected and does not stamp anything new.
        var wrong = otpCode == "000000" ? "111111" : "000000";
        var (badS, badRoot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/otp/verify", new { code = wrong }, token);
        Assert.Equal(400, badS);
        Assert.Contains("Incorrect", badRoot.GetProperty("message").GetString());

        // Trip-level evidence panel lists all three.
        var (listS, listData) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}/pod", null, token);
        Assert.Equal(200, listS);
        var records = listData!.Value.EnumerateArray().ToList();
        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { 0, 1, 2 }, records.Select(r => r.GetProperty("type").GetInt32()).OrderBy(t => t).ToArray());
        Assert.All(records, r => Assert.Equal("Customer A", r.GetProperty("waypointName").GetString()));
        Assert.All(records, r => Assert.False(string.IsNullOrEmpty(r.GetProperty("capturedBy").GetString())));
        _output.WriteLine("PASS  trip evidence panel lists signature + photo + verified OTP for the delivery waypoint");
    }

    [Fact]
    public async Task RequirePod_BlocksArrival_UntilEvidenceCaptured()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (tripId, wpId) = await NewInProgressDeliveryTripAsync(token, suffix, requirePod: true);

        // Delivery waypoint shows the resolved policy on the trip detail.
        var (ds, ddata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.True(ddata!.Value.GetProperty("podRequired").GetBoolean());
        Assert.True(ddata.Value.GetProperty("requirePodForDelivery").GetBoolean());

        // Arrival without evidence → blocked (400, clear message), waypoint unchanged.
        var (a1, a1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/arrive", new { }, token);
        Assert.Equal(400, a1);
        Assert.Contains("requires proof of delivery", a1root.GetProperty("message").GetString());
        _output.WriteLine("PASS  arrival blocked while POD missing → 400 with clear message");

        // Capture evidence, then arrival succeeds.
        var (sigS, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/signature",
            new { signatureSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 40\"><path d=\"M 5 35 L 25 15 L 45 30\" stroke=\"#000\" fill=\"none\"/></svg>", latitude = 23.05, longitude = 72.62 }, token);
        Assert.True(sigS == 200, $"signature status={sigS}");

        var (a2, a2root) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/arrive", new { }, token);
        Assert.True(a2 == 200, $"arrival after capture status={a2} body={a2root?.GetRawText()}");

        var (ds2, ddata2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        var wp = ddata2!.Value.GetProperty("waypoints").EnumerateArray().First(w => w.GetProperty("id").GetGuid() == wpId);
        Assert.NotNull(wp.GetProperty("actualArrival").GetString());
        _output.WriteLine("PASS  arrival allowed once evidence captured; actualArrival stamped");
    }

    [Fact]
    public async Task PodRecords_AreTenantIsolated()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (tripId, wpId) = await NewInProgressDeliveryTripAsync(token, suffix);
        var (sigS, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/signature",
            new { signatureSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M 0 0 L 10 10\"/></svg>" }, token);
        Assert.True(sigS == 200, $"signature status={sigS}");

        // Other company's admin (Basic package still includes the fleet module, so
        // pod.* is granted) must NOT see the demo company's trip or its evidence.
        var basic = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var (x1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}/pod", null, basic);
        Assert.Equal(404, x1);
        var (x2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/signature",
            new { signatureSvg = "<svg/>" }, basic);
        Assert.Equal(404, x2);
        _output.WriteLine("PASS  other-company admin → 404 on pod evidence + capture (no tenant leak)");
    }

    [Fact]
    public async Task PodEndpoints_ArePermissionGated()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (tripId, wpId) = await NewInProgressDeliveryTripAsync(token, suffix);

        // Fleet Manager has trip.view but NOT pod.view/pod.create (pod is a
        // distinct page in the registry) → the evidence/capture endpoints 403.
        var fm = await TokenAsync(RbacFixtures.FleetManagerEmail);
        var (ok, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, fm);
        Assert.Equal(200, ok);

        var (g1, g1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}/pod", null, fm);
        Assert.Equal(403, g1);
        Assert.Contains("pod.view", g1root.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        var (g2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/signature",
            new { signatureSvg = "<svg/>" }, fm);
        Assert.Equal(403, g2);
        _output.WriteLine("PASS  fleet manager (no pod.* grants) → 403 on evidence view + capture");
    }
}