using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Customer Tracking Link — the first surface exposed to someone who is NOT a
/// platform user: possession of an unguessable share token alone grants read-only
/// access to ONE trip. Covered here:
///   - Zero-auth access via the token (magic-link pattern)
///   - Payload allowlist: the API response never carries internal fields
///     (driver/vehicle identity, contact info, geofences, alerts, pricing)
///   - Expired / revoked links return a clean "no longer available" state
///   - POD evidence (signature + photo) surfaces through the public view, with
///     the photo served through a public, token-scoped URL
///   - Per-IP/per-token rate limiting blocks rapid token-guessing bursts
/// </summary>
public sealed class TripShareLinkE2eTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public TripShareLinkE2eTests(E2eFixture fixture, ITestOutputHelper output)
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

    private async Task<(Guid TripId, Guid DeliveryWaypointId, Guid VehicleId, Guid CompanyId)> CreateInProgressTripAsync(string token, string suffix)
    {
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles", new
        {
            registrationNumber = $"TRK-{Unique()}", name = $"Track Vehicle {suffix}",
            vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1,
        }, token);
        Assert.True(vs is 200 or 201, $"vehicle create status={vs}");
        var vehicleId = vd!.Value.GetProperty("id").GetGuid();

        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/drivers", new
        {
            employeeId = $"TRK-{Unique()}", firstName = "Track", lastName = $"Driver {suffix}",
            email = $"e2e.trk.{Unique()}@test.dev",
        }, token);
        Assert.True(ds is 200 or 201, $"driver create status={ds}");
        var driverId = dd!.Value.GetProperty("id").GetGuid();

        var fence = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = (SELECT \"Id\" FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet') ORDER BY \"Name\" LIMIT 1");

        var (status, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/trips", new
        {
            name = $"Track Trip {suffix}", type = 0, vehicleId, driverId,
            waypoints = new object[]
            {
                new { sequenceOrder = 1, legType = 0, waypointType = 0, name = "Depot", latitude = 23.0225, longitude = 72.5714 },
                new { sequenceOrder = 2, legType = 0, waypointType = 1, name = "Customer T", latitude = 23.05, longitude = 72.62 },
            },
            geofenceLinks = new object[] { new { geofenceId = fence, role = 0, sequenceOrder = 1 } },
        }, token);
        Assert.True(status == 201, $"trip create status={status} body={root.GetRawText()}");
        var tripId = root.GetProperty("data").GetProperty("id").GetGuid();

        var (s1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{tripId}/status", new { status = 1 }, token);
        Assert.Equal(200, s1);
        var (s2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{tripId}/status", new { status = 2 }, token);
        Assert.Equal(200, s2);

        var (ds2, droot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.True(ds2 == 200, $"trip get status={ds2} body={droot.GetRawText()}");
        var ddata = droot.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object
            ? dataEl
            : throw new Xunit.Sdk.XunitException($"no data object: {droot.GetRawText()}");
        var deliveryWp = ddata.GetProperty("waypoints").EnumerateArray()
            .First(w => w.GetProperty("waypointType").GetInt32() == 1);
        var companyId = await GuidScalarAsync($"SELECT \"CompanyId\"::text FROM \"Trips\" WHERE \"Id\" = '{tripId}'");
        return (tripId, deliveryWp.GetProperty("id").GetGuid(), vehicleId, companyId);
    }

    private async Task<Guid> GuidScalarAsync(string sql)
        => Guid.Parse(await _db.ScalarAsync(sql) ?? throw new Xunit.Sdk.XunitException($"Lookup returned no row: {sql}"));

    private async Task<string> CreateShareLinkAsync(string token, Guid tripId, int? expiresInDays = null)
    {
        var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/share-links",
            expiresInDays.HasValue ? (object)new { expiresInDays = expiresInDays.Value } : new { }, token);
        Assert.True(status is 200 or 201, $"share-link create status={status} body={data?.GetRawText()}");
        return data!.Value.GetProperty("token").GetString()!;
    }

    /// <summary>Recursively collects every object key in the payload — the allowlist gate.</summary>
    private static HashSet<string> CollectKeys(JsonElement el, HashSet<string> keys)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    keys.Add(prop.Name.ToLowerInvariant());
                    CollectKeys(prop.Value, keys);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) CollectKeys(item, keys);
                break;
        }
        return keys;
    }

    private static readonly string[] PublicAllowlist =
    {
        "status", "statuslabel", "isdelayed", "tripname", "companyname",
        "vehicleposition", "latitude", "longitude", "speedkmh", "headingdeg", "updatedat",
        "waypoints", "name", "sequenceorder", "arrived", "expectedarrivalutc", "podevidence",
        "type", "typename", "verified", "otpverifiedat", "capturedat", "locationmismatch", "signaturesvg", "imageurl",
        "etaminutestonextstop", "expectedfinalarrivalutc", "routepath",
    };

    private static readonly string[] ForbiddenKeys =
    {
        "vehiclename", "registrationnumber", "vehicleid", "driverid", "drivername", "driverfirstname",
        "driverlastname", "phone", "customerphone", "customeremail", "email",
        "geofence", "geofenceid", "geofencename", "corridor", "routeid", "routegeometry",
        "alert", "safety", "delayreason", "cancelreason", "tripgeofences", "linkedgeofenceid",
        "price", "cost", "amount", "fuelusedliters", "capturedby", "notes", "waypointid", "tripid",
        "companyid", "tenantid", "createdby", "id",
    };

    // ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShareLink_ZeroAuth_ReturnsMinimalPublicView()
    {
        var token = await TokenAsync(DemoEmail);
        var (tripId, _, vehicleId, companyId) = await CreateInProgressTripAsync(token, Unique());

        // Seed live position so the public view carries the vehicle marker.
        await _db.ExecuteAsync(
            $"INSERT INTO \"TelemetryStates\" (\"Id\", \"TenantId\", \"VehicleId\", \"DeviceId\", \"CreatedAt\", \"UpdatedAt\", \"EventTimeUtc\", \"Latitude\", \"Longitude\", \"SpeedKmh\", \"HeadingDeg\") " +
            $"VALUES ('{Guid.NewGuid()}', '{companyId}', '{vehicleId}', '{Guid.NewGuid()}', now(), now(), now(), 23.045, 72.605, 42.5, 90)");

        var linkToken = await CreateShareLinkAsync(token, tripId);

        // NO Authorization header — possession of the token alone.
        var (status, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/public/trips/{linkToken}", null, null);
        Assert.True(status == 200, $"public view status={status} body={root.GetRawText()}");

        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("status").GetInt32());          // InProgress
        Assert.Equal("In Transit", data.GetProperty("statusLabel").GetString());
        Assert.StartsWith("Track Trip", data.GetProperty("tripName").GetString()); // name prefix sanity
        Assert.Equal(2, data.GetProperty("waypoints").GetArrayLength());
        Assert.True(data.GetProperty("routePath").GetArrayLength() >= 2, "route path derived from waypoints");
        var pos = data.GetProperty("vehiclePosition");
        Assert.Equal(23.045, pos.GetProperty("latitude").GetDouble(), 3);
        Assert.Equal(42.5, pos.GetProperty("speedKmh").GetDouble(), 3);

        // Payload allowlist: no internal key may appear anywhere in the response.
        var keys = CollectKeys(data, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var forbiddenHit = keys.Where(k => ForbiddenKeys.Contains(k.ToLowerInvariant())).ToList();
        Assert.True(forbiddenHit.Count == 0, $"public payload leaks internal keys: {string.Join(", ", forbiddenHit)}");
        var unknown = keys.Where(k => !PublicAllowlist.Contains(k.ToLowerInvariant())).ToList();
        Assert.True(unknown.Count == 0, $"public payload carries keys outside the allowlist: {string.Join(", ", unknown)}");
        _output.WriteLine("PASS  zero-auth public view, live position, and payload allowlist hold");
    }

    [Fact]
    public async Task ExpiredAndRevokedLinks_ReturnCleanNoLongerAvailable()
    {
        var token = await TokenAsync(DemoEmail);
        var (tripId, _, _, _) = await CreateInProgressTripAsync(token, Unique());

        // Expired: create then age the row past expiry.
        var expiredToken = await CreateShareLinkAsync(token, tripId, expiresInDays: 30);
        await _db.ExecuteAsync($"UPDATE \"TripShareLinks\" SET \"ExpiresAt\" = now() - interval '1 day' WHERE \"Token\" = '{expiredToken}'");
        var (e1, e1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/public/trips/{expiredToken}", null, null);
        Assert.Equal(404, e1);
        Assert.Contains("no longer available", e1root.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(e1root.TryGetProperty("data", out _) && e1root.GetProperty("data").ValueKind != JsonValueKind.Null,
            "expired link must not leak any data");

        // Revoked: revoke via the management endpoint, then the SAME token is dead.
        var revokedToken = await CreateShareLinkAsync(token, tripId);
        var linkId = await GuidScalarAsync($"SELECT \"Id\"::text FROM \"TripShareLinks\" WHERE \"Token\" = '{revokedToken}'");
        var (rv, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{tripId}/share-links/{linkId}/revoke", null, token);
        Assert.True(rv is 200 or 201, $"revoke status={rv}");
        var (r1, r1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/public/trips/{revokedToken}", null, null);
        Assert.Equal(404, r1);
        Assert.Contains("no longer available", r1root.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        // Unknown token: identical clean state (no user enumeration, no data).
        var (u1, u1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/public/trips/{new string('x', 43)}", null, null);
        Assert.Equal(404, u1);
        Assert.Contains("no longer available", u1root.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("PASS  expired / revoked / unknown tokens all yield the same clean dead state");
    }

    [Fact]
    public async Task PublicView_SurfacesPodEvidence_WithTokenScopedPhotoUrl()
    {
        var token = await TokenAsync(DemoEmail);
        var (tripId, wpId, _, _) = await CreateInProgressTripAsync(token, Unique());

        // Signature evidence.
        var (sigS, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/signature",
            new { signatureSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 40\"><path d=\"M 5 35 L 25 15 L 45 30\" stroke=\"#000\" fill=\"none\"/></svg>", latitude = 23.05, longitude = 72.62 }, token);
        Assert.True(sigS == 200, $"signature status={sigS}");

        // Photo evidence (multipart upload).
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "delivery.png");
            form.Add(new StringContent("23.05"), "latitude");
            form.Add(new StringContent("72.62"), "longitude");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/trips/{tripId}/waypoints/{wpId}/pod/photo");
            req.Headers.Authorization = new("Bearer", token);
            req.Content = form;
            using var resp = await _db.Client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"photo upload status={(int)resp.StatusCode} body={body}");
        }

        var linkToken = await CreateShareLinkAsync(token, tripId);
        var (status, root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/public/trips/{linkToken}", null, null);
        Assert.True(status == 200, $"public view status={status}");

        var deliveryWp = root.GetProperty("data").GetProperty("waypoints").EnumerateArray()
            .First(w => w.GetProperty("name").GetString() == "Customer T");
        Assert.True(deliveryWp.GetProperty("podEvidence").GetArrayLength() >= 2, "signature + photo evidence surfaced");

        var photo = deliveryWp.GetProperty("podEvidence").EnumerateArray()
            .First(p => p.TryGetProperty("imageUrl", out var iu) && iu.GetString() != null);
        var photoUrl = photo.GetProperty("imageUrl").GetString()!;
        Assert.StartsWith($"/api/v1/public/trips/{linkToken}/photos/", photoUrl);

        // The photo is fetchable WITHOUT auth through the token-scoped public URL.
        using (var imgResp = await _db.Client.GetAsync(photoUrl))
        {
            Assert.True(imgResp.StatusCode == HttpStatusCode.OK, $"public photo status={(int)imgResp.StatusCode}");
            Assert.Equal(png, await imgResp.Content.ReadAsByteArrayAsync());
        }
        _output.WriteLine("PASS  public view carries POD evidence; photo served via token-scoped public URL");
    }

}

/// <summary>
/// Rate limiting gets its OWN fixture class (its own server + limiter state):
/// the guessing burst exhausts the shared per-IP budget, which would otherwise
/// starve sibling tests that legitimately need a few public requests.
/// </summary>
public sealed class TripShareLinkRateLimitE2eTests : IClassFixture<E2eFixture>
{
    private readonly E2eDb _db;

    public TripShareLinkRateLimitE2eTests(E2eFixture fixture)
    {
        _db = fixture.Db;
    }

    [Fact]
    public async Task RateLimiting_BlocksRapidTokenGuessing()
    {
        // An unauthenticated surface must not allow brute-force enumeration:
        // each guess carries a DIFFERENT token (so per-token limits never
        // engage), but the per-IP limiter throttles the burst with 429.
        var hits = new List<int>();
        for (var i = 0; i < 90; i++)
        {
            using var resp = await _db.Client.GetAsync($"/api/v1/public/trips/{Guid.NewGuid():N}{Guid.NewGuid():N}");
            hits.Add((int)resp.StatusCode);
        }
        Assert.Contains(429, hits);
        Assert.DoesNotContain(200, hits); // no guess may ever succeed
        Assert.True(hits.Count(x => x == 429) >= 10, "a meaningful share of the burst must be throttled");
    }
}