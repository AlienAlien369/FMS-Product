using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Driver Scorecards end-to-end:
///  1. A scorecard materialized from raw DriverBehaviorEvents + completed trips
///     matches the composite math exactly (rate-based safety, weighted blend).
///  2. A company weight override changes the composite; a company without an
///     override uses the platform default.
///  3. A driver below the minimum-activity threshold shows null scores
///     ("insufficient data"), never a misleading number.
///  4. Changing weights never silently rewrites already-computed periods —
///     the cached period stays until an explicit recompute, and historical
///     anchors survive recompute.
///  5. RBAC: driverscore.view gates reading, driverscore.configure gates
///     weight config + recompute.
///  6. A score drop fires a driver.score_drop alert + company-admin
///     notification ONLY when the company is entitled to that alert type.
/// </summary>
public sealed class DriverScorecardE2eTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private const string SaEmail = "admin@freebuff.com";

    public Task InitializeAsync() => RbacFixtures.SeedAsync(_db);
    public Task DisposeAsync() => Task.CompletedTask;
    private const string OpsEmail = RbacFixtures.OpsEmail;            // no driverscore perms
    private const string CompanyAdminEmail = "admin@demofleet.com";   // system role → auto-granted

    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    private static readonly SemaphoreSlim WorldLock = new(1, 1);
    private static bool s_worldReady;
    private static string? s_demoCompanyId;
    private static string? s_vehicleId;

    public DriverScorecardE2eTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    private string Uniq() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Each test gets its OWN driver so event/trip seeding stays deterministic.</summary>
    private async Task<string> CreateDriverAsync(string token, string companyId, string prefix)
    {
        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/drivers",
            new { employeeId = $"{prefix}-{Uniq()}", firstName = prefix, lastName = "Driver", companyId }, token);
        Assert.True(ds is 200 or 201, $"driver create {ds}");
        return dd!.Value.GetProperty("id").GetString()!;
    }

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    private Task<string> SaTokenAsync() => TokenAsync(SaEmail);

    private async Task<string> DemoFleetIdAsync()
        => await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet' AND \"IsDeleted\" = false")
           ?? throw new InvalidOperationException("demo-fleet company missing");

    /// <summary>Shared world: demo-fleet company + one vehicle (per class run).</summary>
    private async Task<(string CompanyId, string VehicleId)> EnsureWorldAsync()
    {
        if (s_worldReady) return (s_demoCompanyId!, s_vehicleId!);
        await WorldLock.WaitAsync();
        try
        {
            if (s_worldReady) return (s_demoCompanyId!, s_vehicleId!);
            var sa = await SaTokenAsync();
            var companyId = await DemoFleetIdAsync();
            var uniq = Uniq();
            var (vs, vd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
                new { registrationNumber = $"SC-{uniq}", name = "Scorecard Vehicle", companyId }, sa);
            Assert.True(vs is 200 or 201, $"vehicle create {vs}");
            s_demoCompanyId = companyId;
            s_vehicleId = vd!.Value.GetProperty("id").GetString()!;
            s_worldReady = true;
            return (companyId, s_vehicleId);
        }
        finally
        {
            WorldLock.Release();
        }
    }

    /// <summary>
    /// Creates a completed trip (via API create + SQL completion) whose waypoints
    /// carry the given expected/actual arrival pairs (null = not comparable).
    /// idleMinutes is applied to the trip's metrics for the Behavior category.
    /// </summary>
    private async Task<string> CreateCompletedTripAsync(string token, string companyId, string vehicleId, string driverId,
        (DateTime? Expected, DateTime? Actual)[] arrivals, int idleMinutes = 0, int durationMin = 300)
    {
        var uniq = Uniq();
        var waypoints = arrivals.Select((a, i) => new
        {
            sequenceOrder = i + 1,
            legType = 0,
            waypointType = 1,
            name = $"Stop {i + 1}",
            latitude = 23.02 + i * 0.01,
            longitude = 72.57 + i * 0.01
        }).ToArray();

        var (ts, td) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/trips",
            new { name = $"Scorecard Trip {uniq}", type = 0, companyId, vehicleId, driverId, waypoints }, token);
        Assert.True(ts is 200 or 201, $"trip create {ts}");
        var tripId = td!.Value.GetProperty("id").GetString()!;

        var end = DateTime.UtcNow.AddDays(-1);
        var start = end.AddMinutes(-durationMin);
        var wpSets = arrivals.Select((a, i) =>
            $"UPDATE \"TripWaypoints\" SET \"ExpectedArrival\" = {(a.Expected.HasValue ? $"'{a.Expected.Value:yyyy-MM-dd HH:mm:ss.fff}'" : "NULL")}, " +
            $"\"ActualArrival\" = {(a.Actual.HasValue ? $"'{a.Actual.Value:yyyy-MM-dd HH:mm:ss.fff}'" : "NULL")} " +
            $"WHERE \"TripId\" = '{tripId}' AND \"SequenceOrder\" = {i + 1};").ToList();
        await _db.ExecuteAsync($"""
            UPDATE "Trips" SET "Status" = 3, "ActualStartTime" = '{start:yyyy-MM-dd HH:mm:ss.fff}',
                "ActualEndTime" = '{end:yyyy-MM-dd HH:mm:ss.fff}', "ActualDuration" = INTERVAL '{durationMin} minutes',
                "IdleMinutes" = {idleMinutes}, "ActualDistance" = 100, "FuelUsedLiters" = 25
            WHERE "Id" = '{tripId}';
            {string.Join("\n", wpSets)}
            """);
        return tripId;
    }

    private Task InsertEventsAsync(string companyId, string vehicleId, string driverId,
        params (int EventType, double Confidence, DateTime Time)[] events)
    {
        var rows = events.Select(e =>
            $"('{Guid.NewGuid()}', '{companyId}', '{Guid.NewGuid()}', '{vehicleId}', '{driverId}', {e.EventType}, {e.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{e.Time:yyyy-MM-dd HH:mm:ss.fff}')");
        return _db.ExecuteAsync($"""
            INSERT INTO "DriverBehaviorEvents"
                ("Id", "TenantId", "DeviceId", "VehicleId", "DriverId", "EventType", "Confidence", "EventTimeUtc")
            VALUES {string.Join(",\n", rows)};
            """);
    }

    /// <summary>Inserts a pre-materialized period row (simulating a previous anchor's nightly run).</summary>
    private Task InsertPeriodAsync(string companyId, string driverId, string window, int anchorDaysAgo,
        decimal? composite, decimal? safety, decimal? compliance, decimal? punctuality, decimal? behavior,
        int tripCount, int eventCount)
    {
        var anchor = DateTime.UtcNow.AddDays(-anchorDaysAgo).Date;
        return _db.ExecuteAsync($"""
            INSERT INTO "DriverScorePeriods"
                ("Id", "CompanyId", "DriverId", "Window", "AnchorDate", "PeriodStart", "PeriodEnd",
                 "SafetyScore", "ComplianceScore", "PunctualityScore", "BehaviorScore", "CompositeScore",
                 "TripCount", "EventCount", "DistanceKm", "ComputedAt")
            VALUES ('{Guid.NewGuid()}', '{companyId}', '{driverId}', '{window}', '{anchor:yyyy-MM-dd}',
                    '{anchor.AddDays(-30):yyyy-MM-dd HH:mm:ss.fff}', '{anchor:yyyy-MM-dd HH:mm:ss.fff}',
                    {(safety.HasValue ? safety.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")},
                    {(compliance.HasValue ? compliance.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")},
                    {(punctuality.HasValue ? punctuality.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")},
                    {(behavior.HasValue ? behavior.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")},
                    {(composite.HasValue ? composite.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")},
                    {tripCount}, {eventCount}, 100, '{anchor.AddHours(-1):yyyy-MM-dd HH:mm:ss.fff}');
            """);
    }

    private async Task SetScoreDropEntitlementAsync(string companyId, bool enabled)
    {
        await _db.ExecuteAsync($"""
            UPDATE "CompanyAlertSubscriptions" SET "Enabled" = {enabled}
            WHERE "CompanyId" = '{companyId}' AND "IsDeleted" = false
              AND "AlertTypeId" = (SELECT "Id" FROM "AlertTypes" WHERE "Code" = 'driver.score_drop' AND "IsDeleted" = false);
            """);
    }

    private async Task<int> AlertCountAsync(string alertType)
        => int.Parse(await _db.ScalarAsync($"""
            SELECT COUNT(*) FROM "Alerts" WHERE "AlertType" = '{alertType}' AND "IsDeleted" = false
            """) ?? "0");

    // ── 1. Composite math from raw data ────────────────────────────────────
    [Fact]
    public async Task Scorecard_Returns_Exact_Composite_From_Raw_Data()
    {
        var (companyId, vehicleId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var driverId = await CreateDriverAsync(sa, companyId, "Exact");
        var now = DateTime.UtcNow;

        // 2 events (harsh_braking=5, drowsiness=10 @1.0) → deductions 15; over 3 trips → safety 50.
        await InsertEventsAsync(companyId, vehicleId, driverId,
            (0, 1.0, now.AddDays(-2)),   // HarshBraking (deduct 5)
            (4, 1.0, now.AddDays(-2)));  // Drowsiness (deduct 10)

        // 3 completed trips: idle 60min/300min → behavior 78; waypoints 3 on-time / 3 late → punctuality 65.
        for (var i = 0; i < 3; i++)
        {
            await CreateCompletedTripAsync(sa, companyId, vehicleId, driverId,
            [
                (now.AddDays(-1).AddHours(-2), now.AddDays(-1).AddHours(-2).AddMinutes(-5)), // on time
                (now.AddDays(-1).AddHours(-1), now.AddDays(-1).AddHours(-1).AddMinutes(30)), // late
            ], idleMinutes: 60, durationMin: 300);
        }

        var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, status);

        var scores = data!.Value.GetProperty("scores");
        Assert.Equal(50, scores.GetProperty("safety").GetInt32());        // 100 − round(15×10/3)
        Assert.Equal(100, scores.GetProperty("compliance").GetInt32());   // license null + no checkpoints
        Assert.Equal(65, scores.GetProperty("punctuality").GetInt32());   // (0.7×0.5 + 0.3×1.0)×100
        Assert.Equal(78, scores.GetProperty("behavior").GetInt32());      // 100 − min(30,(0.2−0.05)×150)
        Assert.Equal(70, scores.GetProperty("composite").GetInt32());     // 0.4×50+0.25×100+0.2×65+0.15×78 = 69.7 → 70
        Assert.False(data.Value.GetProperty("insufficientData").GetBoolean());
        Assert.Equal(3, data.Value.GetProperty("tripCount").GetInt32());
        Assert.Equal(2, data.Value.GetProperty("eventCount").GetInt32());

        // Materialized: a second read must reuse the cached period (same composite).
        var (status2, data2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, status2);
        Assert.Equal(70, data2!.Value.GetProperty("scores").GetProperty("composite").GetInt32());

        // Dashboard fleet overview reflects the materialized data.
        var (ds, dd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            "/api/v1/dashboard/drivers/scorecard-overview", null, sa);
        Assert.Equal(200, ds);
        Assert.True(dd!.Value.GetProperty("average").GetDecimal() > 0);
        Assert.True(dd.Value.GetProperty("distribution").GetArrayLength() > 0);
    }

    // ── 2. Weight override + insufficient data ─────────────────────────────
    [Fact]
    public async Task Weight_Override_And_Insufficient_Data()
    {
        var (companyId, vehicleId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var now = DateTime.UtcNow;

        // Only 2 completed trips → insufficient data, never a misleading number.
        var lowDriverId = await CreateDriverAsync(sa, companyId, "LowAct");
        for (var i = 0; i < 2; i++)
        {
            await CreateCompletedTripAsync(sa, companyId, vehicleId, lowDriverId,
                [(now.AddDays(-1), now.AddDays(-1).AddMinutes(-5)),
                 (now.AddDays(-1).AddHours(-1), now.AddDays(-1).AddHours(-1).AddMinutes(30))]);
        }

        var (ls, ld) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{lowDriverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, ls);
        Assert.True(ld!.Value.GetProperty("insufficientData").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ld.Value.GetProperty("scores").GetProperty("composite").ValueKind);
        Assert.Equal(JsonValueKind.Null, ld.Value.GetProperty("scores").GetProperty("safety").ValueKind);

        // Company override: hazardous-cargo weighting → different composite for the SAME raw data.
        var (us, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/drivers/scorecard-config",
            new { companyId, safetyWeight = 0.7, complianceWeight = 0.1, punctualityWeight = 0.1, behaviorWeight = 0.1 }, sa);
        Assert.Equal(200, us);

        // Company admin of demo-fleet sees the effective company override.
        var coToken = await TokenAsync(CompanyAdminEmail);
        var (cs, cd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/drivers/scorecard-config", null, coToken);
        Assert.Equal(200, cs);
        Assert.Equal("company", cd!.Value.GetProperty("source").GetString());
        Assert.Equal(0.7, cd.Value.GetProperty("safety").GetDouble());
    }

    // ── 3. Config change never silently rewrites history ───────────────────
    [Fact]
    public async Task Config_Change_Does_Not_Rewrite_Computed_Periods_Until_Recompute()
    {
        var (companyId, vehicleId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var driverId = await CreateDriverAsync(sa, companyId, "History");
        var now = DateTime.UtcNow;
        await InsertEventsAsync(companyId, vehicleId, driverId, (0, 1.0, now.AddDays(-2)), (4, 1.0, now.AddDays(-2)));
        for (var i = 0; i < 3; i++)
        {
            await CreateCompletedTripAsync(sa, companyId, vehicleId, driverId,
                [(now.AddDays(-1), now.AddDays(-1).AddMinutes(-5)), (now.AddDays(-1).AddHours(-1), now.AddDays(-1).AddHours(-1).AddMinutes(30))],
                idleMinutes: 60, durationMin: 300);
        }
        // Historical anchor from "yesterday" — must survive everything.
        await InsertPeriodAsync(companyId, driverId, "30d", anchorDaysAgo: 1, composite: 90m,
            safety: 90m, compliance: 90m, punctuality: 90m, behavior: 90m, tripCount: 3, eventCount: 0);

        // First materialization with platform defaults → composite 70.
        var (s1, d1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, s1);
        Assert.Equal(70, d1!.Value.GetProperty("scores").GetProperty("composite").GetInt32());

        // Change company weights (0.7/0.1/0.1/0.1 → composite 59 for the same data).
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/drivers/scorecard-config",
            new { companyId, safetyWeight = 0.7, complianceWeight = 0.1, punctualityWeight = 0.1, behaviorWeight = 0.1 }, sa);

        // Cached period is NOT rewritten by the config change alone.
        var (s2, d2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, s2);
        Assert.Equal(70, d2!.Value.GetProperty("scores").GetProperty("composite").GetInt32());

        // Explicit recompute applies the new weights to TODAY's anchor only.
        var (rs, rd) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/drivers/{driverId}/scorecard/recompute", null, sa);
        Assert.Equal(200, rs);
        Assert.Equal(59, rd!.Value.GetProperty("scores").GetProperty("composite").GetInt32());

        // Yesterday's historical anchor is untouched (still 90).
        var hist = await _db.ScalarAsync($"""
            SELECT "CompositeScore" FROM "DriverScorePeriods"
            WHERE "DriverId" = '{driverId}' AND "Window" = '30d'
              AND "AnchorDate" = ('{DateTime.UtcNow.AddDays(-1):yyyy-MM-dd}')
            """);
        Assert.Equal("90", hist);
    }

    // ── 4. RBAC gates ──────────────────────────────────────────────────────
    [Fact]
    public async Task Rbac_Gates_Scorecard_View_And_Config()
    {
        var (companyId, _) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var driverId = await CreateDriverAsync(sa, companyId, "Rbac");
        var ops = await TokenAsync(OpsEmail);

        // Ops Manager has fleet CRUD but not driverscore → 403 everywhere.
        var (r1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, ops);
        Assert.Equal(403, r1);
        var (r2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/drivers/scorecard-config", null, ops);
        Assert.Equal(403, r2);
        var (r3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/drivers/scorecard-config",
            new { companyId, safetyWeight = 0.5, complianceWeight = 0.25, punctualityWeight = 0.15, behaviorWeight = 0.1 }, ops);
        Assert.Equal(403, r3);
        var (r4, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
            $"/api/v1/drivers/{driverId}/scorecard/recompute", null, ops);
        Assert.Equal(403, r4);

        // Super Admin can read + configure.
        var (a1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, a1);
        var (a2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/drivers/scorecard-config", null, sa);
        Assert.Equal(200, a2);
    }

    // ── 5. Score-drop alert: entitled vs un-entitled ───────────────────────
    [Fact]
    public async Task Score_Drop_Fires_Alert_And_Notification_When_Entitled()
    {
        var (companyId, vehicleId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var driverId = await CreateDriverAsync(sa, companyId, "Drop");
        var now = DateTime.UtcNow;

        // Yesterday's anchor: composite 90 (safe driver).
        await InsertPeriodAsync(companyId, driverId, "30d", anchorDaysAgo: 1, composite: 90m,
            safety: 90m, compliance: 90m, punctuality: 90m, behavior: 90m, tripCount: 3, eventCount: 0);

        // Today: 2 drowsiness events + 3 completed trips → safety 33 → composite 73 → drop 17 ≥ 15.
        await InsertEventsAsync(companyId, vehicleId, driverId,
            (4, 1.0, now.AddDays(-2)), (4, 1.0, now.AddDays(-2)));
        for (var i = 0; i < 3; i++)
        {
            await CreateCompletedTripAsync(sa, companyId, vehicleId, driverId,
                [(now.AddDays(-1), now.AddDays(-1).AddMinutes(-5)), (now.AddDays(-1).AddHours(-1), now.AddDays(-1).AddHours(-1).AddMinutes(30))]);
        }

        // Materializing today's anchor detects the drop → alert + notification.
        var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
        Assert.Equal(200, s);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && await AlertCountAsync("DriverScoreDrop") == 0)
            await Task.Delay(200);
        Assert.True(await AlertCountAsync("DriverScoreDrop") > 0, "score-drop alert never fired");

        var notifCount = int.Parse(await _db.ScalarAsync($"""
            SELECT COUNT(*) FROM "Notifications"
            WHERE "CompanyId" = '{companyId}' AND "EventType" = 'alert.fired' AND "IsDeleted" = false
              AND "Message" LIKE '%score%drop%'
            """) ?? "0");
        Assert.True(notifCount > 0, "score-drop company-admin notification never fired");
    }

    [Fact]
    public async Task UnEntitled_Company_Never_Fires_Score_Drop_Alert()
    {
        var (companyId, vehicleId) = await EnsureWorldAsync();
        var sa = await SaTokenAsync();
        var driverId = await CreateDriverAsync(sa, companyId, "NoEnt");
        var now = DateTime.UtcNow;

        await SetScoreDropEntitlementAsync(companyId, enabled: false);
        try
        {
            await InsertPeriodAsync(companyId, driverId, "30d", anchorDaysAgo: 1, composite: 90m,
                safety: 90m, compliance: 90m, punctuality: 90m, behavior: 90m, tripCount: 3, eventCount: 0);
            await InsertEventsAsync(companyId, vehicleId, driverId, (4, 1.0, now.AddDays(-2)), (4, 1.0, now.AddDays(-2)));
            for (var i = 0; i < 3; i++)
            {
                await CreateCompletedTripAsync(sa, companyId, vehicleId, driverId,
                    [(now.AddDays(-1), now.AddDays(-1).AddMinutes(-5)), (now.AddDays(-1).AddHours(-1), now.AddDays(-1).AddHours(-1).AddMinutes(30))]);
            }

            var (s, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                $"/api/v1/drivers/{driverId}/scorecard?window=30d", null, sa);
            Assert.Equal(200, s);
            await Task.Delay(800); // give a (wrong) alert pipeline time to write

            Assert.Equal(0, await AlertCountAsync("DriverScoreDrop"));
            _output.WriteLine("PASS  un-entitled company: no score-drop alert despite a 17-point drop");
        }
        finally
        {
            await SetScoreDropEntitlementAsync(companyId, enabled: true);
        }
    }
}