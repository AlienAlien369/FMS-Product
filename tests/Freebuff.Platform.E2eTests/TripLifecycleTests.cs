using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Trip module contract — vehicle+driver+route+geofence orchestration over the
/// real API + Postgres. Covers the definition-of-done bullets:
///   - Route-linked trip inherits the route's checkpoints / restricted zones
///   - Dynamic trip with directly-attached geofences behaves identically
///   - Round trip outbound/return sequencing is validated (turnaround rule)
///   - Scheduling with zero linked geofences is blocked (400, clear message)
///   - Double-booking a vehicle/driver on an in-progress trip is a hard block
///   - A missed expectedArrival flags the trip delayed without changing status
///   - Draft → Scheduled → InProgress → Completed lifecycle with history rows
///   - Cancel/abort requires a reason; terminal trips refuse further transitions
///   - Cross-tenant admins get 404 on other companies' trips (no leak/mutation)
///   - Live + replay endpoints read the telemetry stream (empty → 200, no crash)
/// </summary>
public sealed class TripLifecycleTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public TripLifecycleTests(E2eFixture fixture, ITestOutputHelper output)
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

    /// <summary>Creates a fresh vehicle + driver in the caller's company; returns their ids.</summary>
    private async Task<(Guid Vehicle, Guid Driver)> CreateFleetPairAsync(string token, string suffix)
    {
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles", new
        {
            registrationNumber = $"TRIP-{Unique()}", name = $"Trip Vehicle {suffix}",
            vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1,
        }, token);
        Assert.True(vs is 200 or 201, $"vehicle create status={vs}");
        var vehicleId = vd!.Value.GetProperty("id").GetGuid();

        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/drivers", new
        {
            employeeId = $"TRIP-{Unique()}", firstName = "Trip", lastName = $"Driver {suffix}",
            email = $"e2e.trip.{Unique()}@test.dev",
        }, token);
        Assert.True(ds is 200 or 201, $"driver create status={ds}");
        var driverId = dd!.Value.GetProperty("id").GetGuid();
        return (vehicleId, driverId);
    }

    private async Task<(Guid FenceA, Guid FenceB, Guid FenceC)> DemoGeofencesAsync()
    {
        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");
        var a = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = '{demoCompany}' ORDER BY \"Name\" LIMIT 1 OFFSET 0");
        var b = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = '{demoCompany}' ORDER BY \"Name\" LIMIT 1 OFFSET 1");
        var c = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"IsDeleted\" = false AND \"CompanyId\" = '{demoCompany}' ORDER BY \"Name\" LIMIT 1 OFFSET 2");
        return (a, b, c);
    }

    private static object Waypoint(int seq, string name, double lat, double lng, int legType = 0,
        int waypointType = 5, string? expectedArrival = null) =>
        new { sequenceOrder = seq, legType, waypointType, name, latitude = lat, longitude = lng, expectedArrival };

    private static object Link(Guid geofenceId, int role, int? sequenceOrder = null) =>
        new { geofenceId, role, sequenceOrder };

    private static async Task<(int Status, JsonElement Root)> CreateTripAsync(E2eDb db, string token, object payload)
        => await ApiJson.SendRawAsync(db.Client, HttpMethod.Post, "/api/v1/trips", payload, token);

    /// <summary>SendAsync wrapper that fails loudly with the raw body when a 2xx
    /// response carries no usable "data" — turns silent null-data into a real message.</summary>
    private static async Task<JsonElement> ExpectDataAsync(HttpClient client, HttpMethod method, string url, object? body, string token)
    {
        var (status, root) = await ApiJson.SendRawAsync(client, method, url, body, token);
        if (root.ValueKind == JsonValueKind.Undefined)
            throw new Xunit.Sdk.XunitException($"{method} {url} → {status} with EMPTY body");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            throw new Xunit.Sdk.XunitException($"{method} {url} → {status} with NO data: {root.GetRawText()}");
        return data;
    }

    private static async Task<Guid> CreatedIdAsync(E2eDb db, string token, object payload)
    {
        var (status, root) = await CreateTripAsync(db, token, payload);
        Assert.True(status == 201, $"trip create status={status} body={root.GetRawText()}");
        return root.GetProperty("data").GetProperty("id").GetGuid();
    }

    // ───────────────────────────────────────────────────────────────────────
    // Route-linked trip: inherits geofences + corridor; lifecycle completes.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RouteLinkedTrip_InheritsGeofences_AndCompletesLifecycle()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId) = await CreateFleetPairAsync(token, suffix);
        var (fenceA, fenceB, _) = await DemoGeofencesAsync();

        // Route with a checkpoint (seq 1) and a restricted zone.
        var (rs, rroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/routes", new
        {
            name = $"E2E Trip Route {suffix}",
            originName = "Depot", originLatitude = 23.0, originLongitude = 72.5,
            destinationName = "Customer", destinationLatitude = 23.2, destinationLongitude = 72.7,
            waypoints = JsonSerializer.Serialize(new[]
            {
                new { name = "Depot", lat = 23.0, lng = 72.5 },
                new { name = "Customer", lat = 23.2, lng = 72.7 },
            }),
            routeGeometry = JsonSerializer.Serialize(new
            {
                type = "LineString",
                coordinates = new[] { new[] { 72.5, 23.0 }, new[] { 72.7, 23.2 } },
            }),
            corridorEnabled = true, corridorBufferMeters = 500, deviationThresholdMinutes = 10,
        }, token);
        Assert.Equal(201, rs);
        var routeId = rroot.GetProperty("data").GetProperty("id").GetGuid();
        var (ls, _) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/routes/{routeId}/geofences",
            new object[] { Link(fenceA, 0, 1), Link(fenceB, 1) }, token);
        Assert.Equal(200, ls);
        _output.WriteLine("PASS  route created with 1 checkpoint + 1 restricted zone");

        // Trip references the route but supplies NO geofence links of its own —
        // the route's links must be inherited (mandatory-geofence satisfied).
        var tripId = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Route Trip {suffix}",
            type = 0,
            vehicleId,
            driverId,
            routeId,
            scheduledStartTime = DateTime.UtcNow.AddHours(1).ToString("o"),
        });
        _output.WriteLine($"PASS  route-linked trip created {tripId}");

        // Inherited links visible on detail; route + corridor config copied.
        var (gs, gdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, gs);
        var trip = gdata!.Value;
        Assert.Equal(2, trip.GetProperty("geofenceCount").GetInt32());
        Assert.Equal(1, trip.GetProperty("checkpointCount").GetInt32());
        Assert.Equal(1, trip.GetProperty("restrictedZoneCount").GetInt32());
        Assert.True(trip.GetProperty("corridorEnabled").GetBoolean());
        Assert.Equal(routeId, trip.GetProperty("routeId").GetGuid());
        Assert.True(trip.GetProperty("waypointCount").GetInt32() >= 2);
        _output.WriteLine("PASS  inherited 2 route geofences + corridor + route geometry onto the trip");

        // Lifecycle: draft → scheduled → in_progress → completed.
        var (s1, s1root) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 1 }, token);
        Assert.True(s1 == 200, $"schedule status={s1} {s1root?.GetRawText()}");
        var (s2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 2 }, token);
        Assert.Equal(200, s2);
        var (s3, s3root) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 3 }, token);
        Assert.True(s3 == 200, $"complete status={s3} {s3root?.GetRawText()}");
        Assert.Equal("Completed", s3root!.Value.GetProperty("statusName").GetString());

        // History audit trail on the record.
        var (hs, hdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, hs);
        var transitions = hdata!.Value.GetProperty("statusHistory").EnumerateArray()
            .Select(h => $"{h.GetProperty("fromStatus").GetInt32()}->{h.GetProperty("toStatus").GetInt32()}").ToList();
        Assert.Contains("1->2", transitions);
        Assert.Contains("2->3", transitions);
        Assert.Equal("Completed", hdata.Value.GetProperty("statusName").GetString());
        Assert.NotNull(hdata.Value.GetProperty("actualStartTime").GetString());
        Assert.NotNull(hdata.Value.GetProperty("actualEndTime").GetString());
        _output.WriteLine("PASS  draft→scheduled→in_progress→completed with history rows");

        // Replay + live endpoints read the telemetry stream (empty here → clean 200).
        var (rp, rpdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}/replay", null, token);
        Assert.Equal(200, rp);
        Assert.Equal(0, rpdata!.Value.GetArrayLength());
        var (lv, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}/live", null, token);
        Assert.Equal(200, lv);
        _output.WriteLine("PASS  replay (0 points) + live endpoints 200");

        // Cross-tenant: the Basic company admin must not see or mutate this trip.
        var basic = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var (x1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, basic);
        Assert.Equal(404, x1);
        var (x2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 4, reason = "hijack" }, basic);
        Assert.Equal(404, x2);
        _output.WriteLine("PASS  other-company admin GET/status on this trip → 404");

        // Terminal trip refuses further transitions.
        var (t1, t1root) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 4, reason = "late cancel" }, token);
        Assert.True(t1 == 400, $"terminal transition status={t1} body={t1root?.GetRawText()}");
        _output.WriteLine("PASS  completed trip refuses further transitions → 400");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Dynamic trip + direct geofences; validation blocks (no fences, dup seq);
    // in-progress trips cannot be deleted.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DynamicTrip_DirectGeofences_AndValidationBlocks()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId) = await CreateFleetPairAsync(token, suffix);
        var (fenceA, fenceB, fenceC) = await DemoGeofencesAsync();

        // Dynamic (route-less) trip with direct links: checkpoint A(1), B(2) + restricted C.
        var tripId = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Dynamic Trip {suffix}",
            type = 0,
            vehicleId,
            driverId,
            waypoints = new object[]
            {
                Waypoint(1, "Origin Gate", 23.0, 72.5, waypointType: 4),
                Waypoint(2, "Customer Site", 23.2, 72.7, waypointType: 1),
            },
            geofenceLinks = new object[] { Link(fenceA, 0, 1), Link(fenceB, 0, 2), Link(fenceC, 1) },
            corridorEnabled = false,
        });
        var (ds, ddata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, ds);
        Assert.Equal(3, ddata!.Value.GetProperty("geofenceCount").GetInt32());
        Assert.Equal(2, ddata.Value.GetProperty("checkpointCount").GetInt32());
        Assert.Equal(1, ddata.Value.GetProperty("restrictedZoneCount").GetInt32());
        _output.WriteLine("PASS  dynamic trip created with 2 direct checkpoints + 1 restricted zone");

        // Mandatory-geofence rule: a draft with NO links cannot be scheduled.
        var (cs2, _) = await CreateTripAsync(_db, token, new
        {
            name = $"E2E NoFence Trip {suffix}",
            type = 0,
            vehicleId,
            driverId,
            waypoints = new object[] { Waypoint(1, "A", 23.0, 72.5), Waypoint(2, "B", 23.2, 72.7) },
        });
        if (cs2 == 201)
        {
            // Re-fetch the created id via search.
            var ndata = await ExpectDataAsync(_db.Client, HttpMethod.Get,
                $"/api/v1/trips?search={Uri.EscapeDataString($"E2E NoFence Trip {suffix}")}", null, token);
            Assert.True(ndata.GetProperty("items").GetArrayLength() > 0,
                $"no-fence trip missing from list: {ndata.GetRawText()}");
            var nid = ndata.GetProperty("items")[0].GetProperty("id").GetGuid();
            var (st, stroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post,
                $"/api/v1/trips/{nid}/status", new { status = 1 }, token);
            Assert.True(st == 400, $"schedule without fences status={st} {stroot.GetRawText()}");
            Assert.Contains("at least one linked geofence", stroot.GetProperty("message").GetString());
            _output.WriteLine("PASS  zero-geofence schedule blocked → 400 with clear message");
        }
        else
        {
            Assert.True(cs2 == 400, $"unexpected no-fence create status={cs2}"); // e.g. assignment contention is also fine
        }

        // Duplicate checkpoint sequence / same-fence-two-roles on replace-all → 400.
        var (ds2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/trips/{tripId}/geofences",
            new object[] { Link(fenceA, 0, 1), Link(fenceB, 0, 1) }, token);
        Assert.Equal(400, ds2);
        var (dd2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put,
            $"/api/v1/trips/{tripId}/geofences",
            new object[] { Link(fenceA, 0, 1), Link(fenceA, 1) }, token);
        Assert.Equal(400, dd2);
        _output.WriteLine("PASS  duplicate sequence + same-fence-two-roles → 400");

        // Start the trip, then deletion is blocked while in-progress.
        var (st1, st1root) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 2 }, token);
        Assert.True(st1 == 200, $"start status={st1} {st1root?.GetRawText()}");
        var (del1, del1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Delete, $"/api/v1/trips/{tripId}", null, token);
        Assert.True(del1 == 400, $"delete blocked status={del1} {del1root.GetRawText()}");
        _output.WriteLine("PASS  in-progress trip delete blocked → 400");

        // Complete it; deletion of a completed trip is allowed (cleanup).
        var (st2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 3 }, token);
        Assert.Equal(200, st2);
        var (del2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/trips/{tripId}", null, token);
        Assert.True(del2 == 200, $"completed-trip delete status={del2}");
        _output.WriteLine("PASS  completed trip delete → 200");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Round-trip sequencing + delay flag + waypoint arrival marking.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RoundTrip_Sequencing_DelayFlag_AndArrivals()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId) = await CreateFleetPairAsync(token, suffix);
        var (fenceA, _, _) = await DemoGeofencesAsync();

        // Round trip without a return leg → rejected (no turnaround).
        var (badRound, badRoot) = await CreateTripAsync(_db, token, new
        {
            name = $"E2E Bad Round {suffix}",
            type = 1,
            vehicleId,
            driverId,
            waypoints = new object[]
            {
                Waypoint(1, "Depot", 23.0, 72.5, legType: 0),
                Waypoint(2, "Site A", 23.1, 72.6, legType: 0),
            },
            geofenceLinks = new object[] { Link(fenceA, 0, 1) },
        });
        Assert.True(badRound == 400, $"round-without-return status={badRound} {badRoot.GetRawText()}");
        _output.WriteLine("PASS  round trip without return leg → 400");

        // Return-then-outbound (non-contiguous) legs → rejected.
        var (badOrder, badOrderRoot) = await CreateTripAsync(_db, token, new
        {
            name = $"E2E Bad Order {suffix}",
            type = 1,
            vehicleId,
            driverId,
            waypoints = new object[]
            {
                Waypoint(1, "Back", 23.0, 72.5, legType: 1),
                Waypoint(2, "Depot", 23.0, 72.5, legType: 0),
            },
            geofenceLinks = new object[] { Link(fenceA, 0, 1) },
        });
        Assert.True(badOrder == 400, $"out-of-order legs status={badOrder} {badOrderRoot.GetRawText()}");
        _output.WriteLine("PASS  return-then-outbound legs → 400 (contiguity rule)");

        // Valid round trip: outbound → turnaround → return to origin.
        var roundTripId = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Round {suffix}",
            type = 1,
            vehicleId,
            driverId,
            waypoints = new object[]
            {
                Waypoint(1, "Depot", 23.0, 72.5, legType: 0),
                Waypoint(2, "Turnaround", 23.1, 72.6, legType: 0),
                Waypoint(3, "Back to Depot", 23.0, 72.5, legType: 1),
            },
            geofenceLinks = new object[] { Link(fenceA, 0, 1) },
        });
        _output.WriteLine($"PASS  round trip with outbound+return legs created {roundTripId}");

        // Schedule, then force a missed expected arrival on waypoint 1 and start —
        // IsDelayed fires while status stays InProgress.
        var (s1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{roundTripId}/status", new { status = 1 }, token);
        Assert.Equal(200, s1);
        var wp = await GuidScalarAsync(
            $"SELECT \"Id\"::text FROM \"TripWaypoints\" WHERE \"TripId\" = '{roundTripId}' AND \"SequenceOrder\" = 1 AND \"IsDeleted\" = false");
        await _db.ExecuteAsync($$"""
            UPDATE "TripWaypoints" SET "ExpectedArrival" = now() - interval '1 hour' WHERE "Id" = '{{wp}}'
            """);
        var (s2, s2root) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{roundTripId}/status", new { status = 2 }, token);
        Assert.True(s2 == 200, $"start status={s2} {s2root?.GetRawText()}");
        Assert.Equal("InProgress", s2root!.Value.GetProperty("statusName").GetString());
        Assert.True(s2root.Value.GetProperty("isDelayed").GetBoolean(), "trip should be flagged delayed");
        _output.WriteLine("PASS  missed expectedArrival → isDelayed=true, status stays InProgress");

        // Mark the waypoint arrived, complete the trip.
        var (ar, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{roundTripId}/waypoints/{wp}/arrive", new { }, token);
        Assert.Equal(200, ar);
        var (s3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{roundTripId}/status", new { status = 3 }, token);
        Assert.Equal(200, s3);
        _output.WriteLine("PASS  waypoint arrival recorded; round trip completed");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Zone-event endpoint: geofence events drive the lifecycle automatically.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ZoneEventEndpoint_AutoStartsAndAutoCompletes()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId) = await CreateFleetPairAsync(token, suffix);
        var (fenceA, fenceB, fenceC) = await DemoGeofencesAsync();

        // Dynamic trip: StartZone = A, EndZone = B, plus one checkpoint C.
        var tripId = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Zone Trip {suffix}",
            type = 0,
            vehicleId,
            driverId,
            waypoints = new object[] { Waypoint(1, "Depot", 23.0, 72.5), Waypoint(2, "Customer", 23.2, 72.7) },
            geofenceLinks = new object[] { Link(fenceA, 2), Link(fenceB, 3), Link(fenceC, 0, 1) },
        });
        var (sch, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 1 }, token);
        Assert.Equal(200, sch);
        _output.WriteLine("PASS  zone trip scheduled");

        // Exit the origin zone (A) → auto-starts.
        var (z1, z1data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/zone-events", new { geofenceId = fenceA, kind = 1 }, token);
        Assert.True(z1 == 200, $"exit-origin zone event status={z1}");
        Assert.True(z1data!.Value.GetProperty("statusChanged").GetBoolean());
        Assert.Equal(2, z1data.Value.GetProperty("newStatus").GetInt32());
        _output.WriteLine("PASS  exit origin → trip auto-started (InProgress)");

        // Enter the end zone (B) → auto-completes.
        var (z2, z2data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/zone-events", new { geofenceId = fenceB, kind = 0 }, token);
        Assert.True(z2 == 200, $"entry-end zone event status={z2}");
        Assert.True(z2data!.Value.GetProperty("statusChanged").GetBoolean());
        Assert.Equal(3, z2data.Value.GetProperty("newStatus").GetInt32());

        // The unvisited checkpoint (C) must be flagged as missed — the alert is
        // written to the Alerts table (no alerts API exists yet), so assert via
        // SQL scoped to THIS trip's vehicle (alerts carry no TripId; the test's
        // vehicle is freshly created and unique to this run).
        var missedCount = await _db.ScalarAsync($$"""
            SELECT COUNT(*)::text FROM "Alerts"
            WHERE "AlertType" = 'TripMissedCheckpoint' AND "VehicleId" = (
                SELECT "VehicleId" FROM "Trips" WHERE "Id" = '{{tripId}}')
            """);
        Assert.Equal("1", missedCount);

        // History rows carry the geofence_event source.
        var (hs, hdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, hs);
        var sources = hdata!.Value.GetProperty("statusHistory").EnumerateArray()
            .Select(h => h.GetProperty("source").GetString()).ToList();
        Assert.Contains("geofence_event", sources);
        Assert.Equal("Completed", hdata.Value.GetProperty("statusName").GetString());
        _output.WriteLine("PASS  entry end → auto-completed; missed-checkpoint alert raised; history source=geofence_event");

        // A zone event on the now-terminal trip is a clean no-op (200, no change).
        var (z3, z3data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/zone-events", new { geofenceId = fenceA, kind = 0 }, token);
        Assert.Equal(200, z3);
        Assert.False(z3data!.Value.GetProperty("statusChanged").GetBoolean());
    }

    // ───────────────────────────────────────────────────────────────────────
    // Real ingestion → trip automation: device fixes drive the lifecycle.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task IngestedTelemetry_AutoStartsAndAutoCompletesTrip()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId) = await CreateFleetPairAsync(token, suffix);
        var imei = "860" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999); // 15 digits, numeric only

        // Register a device and assign it to the vehicle (telemetry needs an
        // active assignment to resolve the vehicle from the IMEI).
        var (ds, ddata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/devices", new
        {
            vendorCode = "sample-json", deviceType = 0, identityType = 0, identityValue = imei,
        }, token);
        Assert.True(ds is 200 or 201, $"device create status={ds}");
        var deviceId = ddata!.Value.GetProperty("id").GetGuid();
        var (as_, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/vehicles/{vehicleId}/devices", new { deviceId, role = 0 }, token);
        Assert.True(as_ is 200 or 201, $"device assign status={as_}");
        _output.WriteLine("PASS  device registered + assigned");

        // Two known circles: origin (23.0,72.5) and end (23.2,72.7), 500 m.
        // (Geofence create returns 201 without a data payload — resolve by name.)
        var (o1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/geofences", new
        { name = $"E2E Origin {suffix}", type = 0, centerLatitude = 23.0, centerLongitude = 72.5, radius = 500 }, token);
        var (o2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/geofences", new
        { name = $"E2E End {suffix}", type = 0, centerLatitude = 23.2, centerLongitude = 72.7, radius = 500 }, token);
        Assert.True(o1 is 200 or 201 && o2 is 200 or 201, "geofence create failed");
        var originId = await GuidScalarAsync($"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"Name\" = 'E2E Origin {suffix}' AND \"IsDeleted\" = false");
        var endId = await GuidScalarAsync($"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"Name\" = 'E2E End {suffix}' AND \"IsDeleted\" = false");

        var tripId = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Ingest Trip {suffix}",
            type = 0,
            vehicleId,
            driverId,
            waypoints = new object[] { Waypoint(1, "Depot", 23.0, 72.5), Waypoint(2, "Customer", 23.2, 72.7) },
            geofenceLinks = new object[] { Link(originId, 2), Link(endId, 3) },
        });
        var (sch, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 1 }, token);
        Assert.Equal(200, sch);

        var fix = (double lat, double lng) => new { imei, ts = DateTime.UtcNow.ToString("o"), lat, lon = lng, speed = 40.0 };
        // Fix 1: inside the origin — first fix, establishes the baseline only.
        var (f1, f1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", fix(23.0, 72.5));
        Assert.True(f1 == 200, $"fix1 status={f1} body={f1root.GetRawText()}");
        // Fix 2: outside the origin → EXIT → auto-start.
        var (f2, f2root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", fix(23.01, 72.5));
        Assert.True(f2 == 200, $"fix2 status={f2} body={f2root.GetRawText()}");
        var (d1, d1data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, d1);
        Assert.Equal("InProgress", d1data!.Value.GetProperty("statusName").GetString());
        _output.WriteLine("PASS  vehicle left the origin circle → trip auto-started");

        // Fix 3: outside the end circle — no state change.
        var (f3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", fix(23.2, 72.68));
        Assert.Equal(200, f3);
        // Fix 4: inside the end circle → ENTRY → auto-complete.
        var (f4, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", fix(23.2, 72.7));
        Assert.Equal(200, f4);
        var (d2, d2data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}", null, token);
        Assert.Equal(200, d2);
        Assert.Equal("Completed", d2data!.Value.GetProperty("statusName").GetString());
        var sources = d2data.Value.GetProperty("statusHistory").EnumerateArray()
            .Select(h => h.GetProperty("source").GetString()).ToList();
        Assert.Contains("geofence_event", sources);
        _output.WriteLine("PASS  vehicle entered the end circle → trip auto-completed (source=geofence_event)");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Double-booking hard block + cancel requires reason.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task DoubleBooking_IsHardBlock_AndCancelNeedsReason()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (busyVehicle, busyDriver) = await CreateFleetPairAsync(token, suffix);
        var (freeVehicle, freeDriver) = await CreateFleetPairAsync(token, "free-" + suffix);
        var (fenceA, _, _) = await DemoGeofencesAsync();

        // First trip goes in-progress with this vehicle+driver.
        var trip1Id = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Busy Trip {suffix}",
            type = 0,
            vehicleId = busyVehicle,
            driverId = busyDriver,
            waypoints = new object[] { Waypoint(1, "A", 23.0, 72.5), Waypoint(2, "B", 23.2, 72.7) },
            geofenceLinks = new object[] { Link(fenceA, 0, 1) },
        });
        await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{trip1Id}/status", new { status = 1 }, token);
        var (st2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{trip1Id}/status", new { status = 2 }, token);
        Assert.Equal(200, st2);

        // A second trip for the same vehicle is a hard 400 at create.
        var (cs2, c2root) = await CreateTripAsync(_db, token, new
        {
            name = $"E2E Conflict Trip {suffix}",
            type = 0,
            vehicleId = busyVehicle,
            driverId = busyDriver,
            waypoints = new object[] { Waypoint(1, "A", 23.0, 72.5), Waypoint(2, "B", 23.2, 72.7) },
            geofenceLinks = new object[] { Link(fenceA, 0, 1) },
        });
        Assert.True(cs2 == 400, $"double-booking create status={cs2}");
        Assert.Contains("already assigned", c2root.GetProperty("message").GetString());
        _output.WriteLine("PASS  vehicle/driver already on in-progress trip → hard 400 at create");

        // A fresh pair schedules fine; cancelling without a reason is rejected.
        var trip2Id = await CreatedIdAsync(_db, token, new
        {
            name = $"E2E Free Trip {suffix}",
            type = 0,
            vehicleId = freeVehicle,
            driverId = freeDriver,
            waypoints = new object[] { Waypoint(1, "A", 23.0, 72.5), Waypoint(2, "B", 23.2, 72.7) },
            geofenceLinks = new object[] { Link(fenceA, 0, 1) },
        });
        await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/trips/{trip2Id}/status", new { status = 1 }, token);
        var (c1, c1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{trip2Id}/status", new { status = 4 }, token);
        Assert.True(c1 == 400, $"cancel without reason status={c1} {c1root.GetRawText()}");
        Assert.Contains("reason is required", c1root.GetProperty("message").GetString());
        var c2data = await ExpectDataAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{trip2Id}/status", new { status = 4, reason = "Customer cancelled the order" }, token);
        Assert.Equal("Cancelled", c2data.GetProperty("statusName").GetString());
        _output.WriteLine("PASS  cancel requires a reason; with reason → Cancelled");
    }
}