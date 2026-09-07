using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Driver Safety Monitoring end-to-end: DMS camera registration + assignment,
/// behavior-event ingestion (normalized by the adapter), driver-resolved event
/// persistence (scorecard groundwork), registry alerts + notifications, and the
/// two spec-locked behaviors — a company NOT entitled to driver.drowsiness never
/// gets that alert, and a panic button fires regardless of per-user preference.
/// </summary>
public sealed class DriverSafetyE2eTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public DriverSafetyE2eTests(E2eFixture fixture, ITestOutputHelper output)
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

    /// <summary>Creates a fresh vehicle + driver in demo-fleet; registers a GPS
    /// tracker + a DMS camera (the new device type) and assigns both.</summary>
    private async Task<(Guid Vehicle, Guid Driver, Guid DmsDevice, string Imei)> SeedDmsFleetAsync(string token, string suffix)
    {
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles", new
        {
            registrationNumber = $"SAFE-{Unique()}", name = $"Safety Vehicle {suffix}",
            vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1,
        }, token);
        Assert.True(vs is 200 or 201, $"vehicle create status={vs}");
        var vehicleId = vd!.Value.GetProperty("id").GetGuid();

        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/drivers", new
        {
            employeeId = $"SAFE-{Unique()}", firstName = "Safety", lastName = $"Driver {suffix}",
            email = $"e2e.safety.{Unique()}@test.dev",
        }, token);
        Assert.True(ds is 200 or 201, $"driver create status={ds}");
        var driverId = dd!.Value.GetProperty("id").GetGuid();

        // DMS camera is a first-class device type (6) with its own vehicle role (7).
        var imei = "860" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999);
        var (ds2, ddata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/devices", new
        {
            vendorCode = "sample-json", deviceType = 6, identityType = 0, identityValue = imei,
        }, token);
        Assert.True(ds2 is 200 or 201, $"dms device create status={ds2}");
        var dmsDeviceId = ddata!.Value.GetProperty("id").GetGuid();
        var (as1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/vehicles/{vehicleId}/devices", new { deviceId = dmsDeviceId, role = 7 }, token);
        Assert.True(as1 is 200 or 201, $"dms assign status={as1}");

        // Confirm the registration stuck with the DMS type + role.
        var dmsType = await _db.ScalarAsync(
            $"SELECT \"DeviceType\"::text FROM \"Devices\" WHERE \"Id\" = '{dmsDeviceId}'");
        Assert.Equal("6", dmsType);
        var dmsRole = await _db.ScalarAsync(
            $"SELECT \"Role\"::text FROM \"VehicleDevices\" WHERE \"DeviceId\" = '{dmsDeviceId}' AND \"AssignedTo\" IS NULL");
        Assert.Equal("7", dmsRole);
        _output.WriteLine("PASS  DMS camera registered (deviceType=6) + assigned (role=7)");

        return (vehicleId, driverId, dmsDeviceId, imei);
    }

    /// <summary>Schedules and starts an in-progress trip so ingest resolves the driver.</summary>
    private async Task<Guid> StartTripAsync(string token, Guid vehicleId, Guid driverId, string suffix)
    {
        var (o1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/geofences", new
        { name = $"E2E Origin {suffix}", type = 0, centerLatitude = 23.0, centerLongitude = 72.5, radius = 500 }, token);
        var (o2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/geofences", new
        { name = $"E2E End {suffix}", type = 0, centerLatitude = 23.2, centerLongitude = 72.7, radius = 500 }, token);
        Assert.True(o1 is 200 or 201 && o2 is 200 or 201, "geofence create failed");
        var originId = await GuidScalarAsync($"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"Name\" = 'E2E Origin {suffix}' AND \"IsDeleted\" = false");
        var endId = await GuidScalarAsync($"SELECT \"Id\"::text FROM \"Geofences\" WHERE \"Name\" = 'E2E End {suffix}' AND \"IsDeleted\" = false");

        var (ts, troot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/trips", new
        {
            name = $"E2E Safety Trip {suffix}", type = 0, vehicleId, driverId,
            waypoints = new object[]
            {
                new { sequenceOrder = 1, legType = 0, waypointType = 5, name = "Depot", latitude = 23.0, longitude = 72.5 },
                new { sequenceOrder = 2, legType = 0, waypointType = 5, name = "Customer", latitude = 23.2, longitude = 72.7 },
            },
            geofenceLinks = new object[] { new { geofenceId = originId, role = 2, sequenceOrder = (int?)null }, new { geofenceId = endId, role = 3, sequenceOrder = (int?)null } },
        }, token);
        Assert.Equal(201, ts);
        var tripId = troot.GetProperty("data").GetProperty("id").GetGuid();
        var (sch, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 1 }, token);
        Assert.Equal(200, sch);
        var (start, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/trips/{tripId}/status", new { status = 2 }, token);
        Assert.Equal(200, start);
        return tripId;
    }

    // ───────────────────────────────────────────────────────────────────────
    // Full chain: DMS device → normalized behavior events → driver-resolved
    // rows → registry alerts + notifications.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task IngestedBehaviorEvents_PersistDriverResolved_AndRaiseAlertsAndNotifications()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId, _, imei) = await SeedDmsFleetAsync(token, suffix);
        var tripId = await StartTripAsync(token, vehicleId, driverId, suffix);
        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");

        // Ingest one fix carrying three DMS events: harsh braking (Medium),
        // drowsiness (High) and the panic button (Critical).
        var (st, sroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
        {
            imei,
            ts = DateTime.UtcNow.ToString("o"),
            lat = 23.1, lon = 72.6, speed = 42.5,
            behaviorEvents = new object[]
            {
                new { type = "harsh_braking", confidence = 0.92, mediaUrl = "https://cdn.example.com/clips/ab1.mp4" },
                new { type = "drowsiness", confidence = 0.87 },
                new { type = "sos_triggered", confidence = 0.99 },
            },
        });
        Assert.True(st == 200, $"ingest status={st} body={sroot.GetRawText()}");

        // Driver-resolved event rows persisted (scorecard groundwork) — queryable
        // by driver via the API.
        var (se, sedata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/safety-events", null, token);
        Assert.Equal(200, se);
        var events = sedata!.Value.EnumerateArray().ToList();
        Assert.Equal(3, events.Count);
        var codes = events.Select(e => e.GetProperty("eventType").GetString()).OrderBy(c => c).ToArray();
        Assert.Equal(new[] { "drowsiness", "harsh_braking", "sos_triggered" }, codes);
        var braking = events.Single(e => e.GetProperty("eventType").GetString() == "harsh_braking");
        Assert.Equal("https://cdn.example.com/clips/ab1.mp4", braking.GetProperty("mediaUrl").GetString());
        Assert.Equal(2, braking.GetProperty("severity").GetInt32()); // Medium
        var drowsy = events.Single(e => e.GetProperty("eventType").GetString() == "drowsiness");
        Assert.Equal(3, drowsy.GetProperty("severity").GetInt32()); // High
        var panic = events.Single(e => e.GetProperty("eventType").GetString() == "sos_triggered");
        Assert.Equal(4, panic.GetProperty("severity").GetInt32()); // Critical
        _output.WriteLine("PASS  behavior events persisted driver-resolved with canonical codes + severities");

        // Live tracking surface: the trip's live endpoint exposes recent safety events.
        var (lt, ltdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/trips/{tripId}/live", null, token);
        Assert.Equal(200, lt);
        var recent = ltdata!.Value.GetProperty("recentSafetyEvents").EnumerateArray().ToList();
        Assert.True(recent.Count >= 1, "live view should surface recent safety events");
        var recentCodes = recent.Select(e => e.GetProperty("eventType").GetString()).ToHashSet();
        Assert.Contains("sos_triggered", recentCodes);
        Assert.Contains("drowsiness", recentCodes);
        _output.WriteLine("PASS  live trip view surfaces the safety events");

        // Alerts: three distinct registry alert rows with per-type severities.
        var alerts = await _db.ScalarAsync($@"
            SELECT string_agg(""AlertType"" || ':' || ""Severity""::text, ',' ORDER BY ""AlertType"")
            FROM ""Alerts"" WHERE ""CompanyId"" = '{demoCompany}' AND ""VehicleId"" = '{vehicleId}' AND ""IsDeleted"" = false");
        Assert.Equal("DriverDrowsiness:3,DriverHarshBraking:2,DriverPanicButton:4", alerts);
        _output.WriteLine("PASS  three registry alerts with correct severities (2/3/4)");

        // Notifications: alert.fired for the driver alerts AND the panic — the
        // panic notification is Critical. (Every admin in the company receives
        // each one, so assert the distinct severity set {2,3,4}.)
        var notif = await _db.ScalarAsync($@"
            SELECT string_agg(DISTINCT ""Severity""::text, ',' ORDER BY ""Severity""::text DESC)
            FROM ""Notifications""
            WHERE ""CompanyId"" = '{demoCompany}' AND ""RelatedEntityType"" = 'Driver' AND ""RelatedEntityId"" = '{driverId}' AND ""IsDeleted"" = false");
        Assert.Equal("4,3,2", notif);
        var panicCount = await _db.ScalarAsync($@"
            SELECT COUNT(*)::text FROM ""Notifications""
            WHERE ""CompanyId"" = '{demoCompany}' AND ""RelatedEntityType"" = 'Driver' AND ""RelatedEntityId"" = '{driverId}'
              AND ""Severity"" = 4 AND ""IsDeleted"" = false");
        Assert.True(int.Parse(panicCount!) >= 1, "panic notification delivered to at least one admin");
        _output.WriteLine("PASS  notifications delivered across severities (panic at Critical)");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Entitlement gate: a company NOT subscribed to driver.drowsiness never
    // generates that alert even when the device reports it.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Drowsiness_CompanyNotEntitled_NoAlertButEventPersisted()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId, _, imei) = await SeedDmsFleetAsync(token, suffix);
        await StartTripAsync(token, vehicleId, driverId, suffix);
        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");

        // Disable the company's driver.drowsiness entitlement (Super-Admin set).
        var drowsyType = await GuidScalarAsync("SELECT \"Id\"::text FROM \"AlertTypes\" WHERE \"Code\" = 'driver.drowsiness' AND \"IsDeleted\" = false");
        await _db.ExecuteAsync($@"
            UPDATE ""CompanyAlertSubscriptions"" SET ""Enabled"" = false
            WHERE ""CompanyId"" = '{demoCompany}' AND ""AlertTypeId"" = '{drowsyType}' AND ""IsDeleted"" = false");
        try
        {
            var (st, sroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
            {
                imei,
                ts = DateTime.UtcNow.ToString("o"),
                lat = 23.1, lon = 72.6, speed = 40.0,
                behaviorEvents = new object[] { new { type = "drowsiness", confidence = 0.9 } },
            });
            Assert.True(st == 200, $"ingest status={st} body={sroot.GetRawText()}");

            // No alert, no notification for the disabled type…
            var drowsyAlerts = await _db.ScalarAsync($@"
                SELECT COUNT(*)::text FROM ""Alerts""
                WHERE ""CompanyId"" = '{demoCompany}' AND ""VehicleId"" = '{vehicleId}' AND ""AlertType"" = 'DriverDrowsiness' AND ""IsDeleted"" = false");
            Assert.Equal("0", drowsyAlerts);
            // …but the raw event is still persisted (scorecard groundwork).
            var persisted = await _db.ScalarAsync($@"
                SELECT COUNT(*)::text FROM ""DriverBehaviorEvents""
                WHERE ""DriverId"" = '{driverId}' AND ""EventType"" = 4");
            Assert.Equal("1", persisted);
            _output.WriteLine("PASS  un-entitled company: no drowsiness alert, raw event still persisted");
        }
        finally
        {
            await _db.ExecuteAsync($@"
                UPDATE ""CompanyAlertSubscriptions"" SET ""Enabled"" = true
                WHERE ""CompanyId"" = '{demoCompany}' AND ""AlertTypeId"" = '{drowsyType}' AND ""IsDeleted"" = false");
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    // Panic preference immunity: a user who MUTED alert.fired still receives
    // the panic notification — an emergency signal is not mutable by personal
    // notification settings.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task PanicButton_Notification_DeliveredDespiteMutedPreference()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, driverId, _, imei) = await SeedDmsFleetAsync(token, suffix);
        await StartTripAsync(token, vehicleId, driverId, suffix);
        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");
        var adminUserId = await GuidScalarAsync(
            "SELECT \"Id\"::text FROM \"Users\" WHERE \"Email\" = 'admin@demofleet.com' AND \"IsDeleted\" = false");

        // Mute alert.fired for the demo admin — the panic must still arrive.
        var prefId = Guid.NewGuid();
        await _db.ExecuteAsync($@"
            INSERT INTO ""NotificationPreferences"" (""Id"", ""UserId"", ""CompanyId"", ""EventType"", ""Enabled"", ""IsDeleted"", ""CreatedAt"", ""UpdatedAt"", ""Version"")
            VALUES ('{prefId}', '{adminUserId}', '{demoCompany}', 'alert.fired', false, false, now(), now(), 0)");
        try
        {
            var (st, sroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
            {
                imei,
                ts = DateTime.UtcNow.ToString("o"),
                lat = 23.15, lon = 72.65, speed = 15.0,
                behaviorEvents = new object[] { new { type = "sos_triggered", confidence = 0.99 } },
            });
            Assert.True(st == 200, $"ingest status={st} body={sroot.GetRawText()}");

            // The panic notification was delivered to the muted user regardless.
            var delivered = await _db.ScalarAsync($@"
                SELECT COUNT(*)::text FROM ""Notifications""
                WHERE ""UserId"" = '{adminUserId}' AND ""EventType"" = 'alert.fired'
                  AND ""RelatedEntityType"" = 'Driver' AND ""RelatedEntityId"" = '{driverId}' AND ""IsDeleted"" = false");
            Assert.Equal("1", delivered);
            var severity = await _db.ScalarAsync($@"
                SELECT ""Severity""::text FROM ""Notifications""
                WHERE ""UserId"" = '{adminUserId}' AND ""EventType"" = 'alert.fired'
                  AND ""RelatedEntityType"" = 'Driver' AND ""RelatedEntityId"" = '{driverId}' AND ""IsDeleted"" = false LIMIT 1");
            Assert.Equal("4", severity); // Critical — the bell can distinguish an SOS
            _output.WriteLine("PASS  panic notification delivered to a user who muted alert.fired (severity 4)");
        }
        finally
        {
            await _db.ExecuteAsync($"DELETE FROM \"NotificationPreferences\" WHERE \"Id\" = '{prefId}'");
        }
    }
}