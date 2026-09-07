using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Speed Governor Monitoring + TPMS end-to-end, through the real ingestion
/// pipeline: a vehicle exceeding the POLICY limit alerts even though the
/// hardware governor allows it; a rapid tyre-pressure drop is flagged critical
/// (blowout-in-progress) via the temporal check; a vendor that doesn't report
/// TPMS/governor shows "not supported" — never a fabricated zero.
/// </summary>
public sealed class SensorPoliciesE2eTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new();

    public SensorPoliciesE2eTests(E2eFixture fixture, ITestOutputHelper output)
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

    /// <summary>Creates a vehicle + registered device (vendor code) and assigns the device to the vehicle.</summary>
    private async Task<(Guid Vehicle, string Imei)> SeedVehicleWithDeviceAsync(string token, string suffix, string vendorCode, int deviceType)
    {
        var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles", new
        {
            registrationNumber = $"SENS-{Unique()}", name = $"Sensor Vehicle {suffix}",
            vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1,
        }, token);
        Assert.True(vs is 200 or 201, $"vehicle create status={vs}");
        var vehicleId = vd!.Value.GetProperty("id").GetGuid();

        var imei = "860" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999);
        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/devices", new
        {
            vendorCode, deviceType, identityType = 0, identityValue = imei,
        }, token);
        Assert.True(ds is 200 or 201, $"device create status={ds}");
        var deviceId = dd!.Value.GetProperty("id").GetGuid();
        var (as1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/vehicles/{vehicleId}/devices", new { deviceId, role = 1 }, token);
        Assert.True(as1 is 200 or 201, $"device assign status={as1}");
        return (vehicleId, imei);
    }

    private async Task SetFleetPoliciesAsync(string token, double? speed = null, double? tyreMin = null, double? tyreMax = null)
    {
        var (st, sroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Put, "/api/v1/tenant/fleet-policies", new
        {
            speedPolicyMaxKmh = speed, tyrePressureMinBar = tyreMin, tyrePressureMaxBar = tyreMax,
        }, token);
        Assert.True(st == 200, $"fleet-policies PUT status={st} body={sroot.GetRawText()}");
        _output.WriteLine($"PASS  fleet policies set (speed={speed}, tyre {tyreMin}–{tyreMax} bar)");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Speed: the alert fires when the POLICY limit is crossed, even when the
    // hardware governor allows a higher speed (60 km/h policy, 80 km/h governor).
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SpeedOverPolicy_AlertsEvenWhenGovernorAllowsIt()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, imei) = await SeedVehicleWithDeviceAsync(token, suffix, "sample-json", deviceType: 0);
        await SetFleetPoliciesAsync(token, speed: 60);
        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");

        // 70 km/h: over the 60 policy, under the 80 governor → must alert on policy.
        var (st, sroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
        {
            imei, ts = DateTime.UtcNow.ToString("o"), lat = 23.1, lon = 72.6, speed = 70.0, governorLimit = 80.0,
        });
        Assert.True(st == 200, $"ingest status={st} body={sroot.GetRawText()}");

        var (a1, a1root) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/vehicles/{vehicleId}/sensors", null, token);
        Assert.True(a1 == 200, $"sensors GET status={a1}");
        Assert.Equal("over", a1root.GetProperty("data").GetProperty("speedStatus").GetString());
        Assert.Equal(70.0, a1root.GetProperty("data").GetProperty("speedKmh").GetDouble());
        Assert.Equal(60.0, a1root.GetProperty("data").GetProperty("policySpeedMaxKmh").GetDouble());
        Assert.Equal(80.0, a1root.GetProperty("data").GetProperty("speedGovernorLimitKmh").GetDouble());
        Assert.True(a1root.GetProperty("data").GetProperty("governorSupported").GetBoolean());

        var alert = await _db.ScalarAsync($@"
            SELECT ""Severity""::text || '|' || ""Message"" FROM ""Alerts""
            WHERE ""CompanyId"" = '{demoCompany}' AND ""VehicleId"" = '{vehicleId}'
              AND ""AlertType"" = 'vehicle.speed_limit_exceeded' AND ""IsDeleted"" = false");
        Assert.NotNull(alert);
        Assert.StartsWith("3|", alert); // High — policy breach
        Assert.Contains("70", alert);
        Assert.Contains("60 km/h policy", alert);
        _output.WriteLine("PASS  speed alert fired on POLICY (70 > 60) despite governor allowing 80 — High severity");

        // 55 km/h: under both → no second alert.
        var (st2, _) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
        {
            imei, ts = DateTime.UtcNow.ToString("o"), lat = 23.1, lon = 72.6, speed = 55.0, governorLimit = 80.0,
        });
        Assert.True(st2 == 200, $"ingest2 status={st2}");
        var count = await _db.ScalarAsync($@"
            SELECT COUNT(*)::text FROM ""Alerts""
            WHERE ""CompanyId"" = '{demoCompany}' AND ""VehicleId"" = '{vehicleId}'
              AND ""AlertType"" = 'vehicle.speed_limit_exceeded' AND ""IsDeleted"" = false");
        Assert.Equal("1", count);
        _output.WriteLine("PASS  below policy → no additional speed alert");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Tyre: a rapid pressure drop (blowout-in-progress) is critical even though
    // the static deviation alone would only warn (4.4 vs 5.0 min = 12% below,
    // but a 27% drop in 1 minute → rapid loss → High).
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TyreRapidDrop_FlagsCritical_FasterThanStaticCheck()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        var (vehicleId, imei) = await SeedVehicleWithDeviceAsync(token, suffix, "sample-json", deviceType: 0);
        await SetFleetPoliciesAsync(token, tyreMin: 5.0, tyreMax: 7.0);
        var demoCompany = await GuidScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");
        var t0 = DateTime.UtcNow;

        // Fix 1: within range (6.0 bar) → no alert.
        var (st1, sroot1) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
        {
            imei, ts = t0.ToString("o"), lat = 23.1, lon = 72.6, speed = 40.0,
            tyrePressures = new object[] { new { position = "front_left", pressureBar = 6.0, temperatureC = 35.0 } },
        });
        Assert.True(st1 == 200, $"ingest1 status={st1} body={sroot1.GetRawText()}");

        // Fix 2 (1 min later): 4.4 bar — 12% below min (would be warning), but
        // a 27% drop vs the 6.0 reading → rapid loss → critical.
        var (st2, sroot2) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/sample-json", new
        {
            imei, ts = t0.AddMinutes(1).ToString("o"), lat = 23.1, lon = 72.6, speed = 40.0,
            tyrePressures = new object[] { new { position = "front_left", pressureBar = 4.4 } },
        });
        Assert.True(st2 == 200, $"ingest2 status={st2} body={sroot2.GetRawText()}");

        var alert = await _db.ScalarAsync($@"
            SELECT ""Severity""::text || '|' || ""Message"" FROM ""Alerts""
            WHERE ""CompanyId"" = '{demoCompany}' AND ""VehicleId"" = '{vehicleId}'
              AND ""AlertType"" = 'vehicle.tyre_pressure_anomaly' AND ""IsDeleted"" = false");
        Assert.NotNull(alert);
        Assert.StartsWith("3|", alert); // Critical tier → High
        Assert.Contains("rapid pressure loss", alert);
        _output.WriteLine("PASS  rapid tyre drop flagged critical via temporal check (static check would only warn)");

        // The sensors surface reflects the warning/critical status live.
        var (se, sedata) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/vehicles/{vehicleId}/sensors", null, token);
        Assert.True(se == 200, $"sensors GET status={se}");
        var tyres = sedata.GetProperty("data").GetProperty("tyres").EnumerateArray().ToList();
        Assert.Single(tyres);
        Assert.Equal("critical", tyres[0].GetProperty("status").GetString());
        Assert.Equal(4.4, tyres[0].GetProperty("pressureBar").GetDouble());
        _output.WriteLine("PASS  live sensors surface shows the critical tyre");
    }

    // ───────────────────────────────────────────────────────────────────────
    // Not-supported: a Pictor camera (no TPMS, no governor) shows supported=false
    // and an empty tyre list — never a fabricated zero reading.
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task VendorWithoutTpms_ShowsNotSupported_NoFalseZero()
    {
        var token = await TokenAsync(DemoEmail);
        var suffix = Unique();
        // Pictor is a camera vendor: GPS + DMS events only — no governor, no TPMS.
        var (vehicleId, imei) = await SeedVehicleWithDeviceAsync(token, suffix, "pictor", deviceType: 6);

        var (st, sroot) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/ingest/pictor", new
        {
            imei, ts = DateTime.UtcNow.ToString("o"), lat = 23.1, lon = 72.6, speed = 55.0,
        });
        Assert.True(st == 200, $"ingest status={st} body={sroot.GetRawText()}");

        var (se, sedata) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Get, $"/api/v1/vehicles/{vehicleId}/sensors", null, token);
        Assert.True(se == 200, $"sensors GET status={se}");
        var data = sedata.GetProperty("data");

        Assert.Equal(55.0, data.GetProperty("speedKmh").GetDouble());       // speed is real
        Assert.False(data.GetProperty("governorSupported").GetBoolean());   // no governor on this device
        Assert.False(data.GetProperty("tyresSupported").GetBoolean());      // no TPMS on this device
        Assert.Empty(data.GetProperty("tyres").EnumerateArray());           // empty, not zeros
        Assert.Null(data.GetProperty("speedGovernorLimitKmh").GetDoubleOrNull());
        _output.WriteLine("PASS  pictor (no TPMS/governor) → supported=false, empty tyres, real speed only");
    }

    private async Task<Guid> GuidScalarAsync(string sql)
        => Guid.Parse(await _db.ScalarAsync(sql) ?? throw new Xunit.Sdk.XunitException($"Lookup returned no row: {sql}"));
}

internal static class SensorE2eJsonExtensions
{
    public static double? GetDoubleOrNull(this JsonElement el)
        => el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined ? null : el.GetDouble();
}