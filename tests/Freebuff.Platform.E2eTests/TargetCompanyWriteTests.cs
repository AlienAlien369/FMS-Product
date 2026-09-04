using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Contract for how the single TARGET company of a create write is resolved
/// (TargetCompanyResolver) — independent of the view-scope header:
///
///  1. SuperAdmin scoped to a single company creates with that company as the
///     target → the record lands in that company.
///  2. SuperAdmin scoped to ALL/multiple creates WITHOUT an explicit companyId
///     → the write is rejected (400, clear validation error — no silent default).
///  3. SuperAdmin explicitly targets Company B while scoped to Company A →
///     the record lands in B: the target field, NOT the view scope, controls
///     the write. Also leaves an audit trail (cross-tenant write).
///  4. A non-cross-tenant user's forged companyId in the payload is ignored —
///     the server forces their own tenant regardless of what was sent.
///
/// Exercised table-driven across every tenant-scoped fleet resource
/// (vehicle, driver, geofence, route, device) so a future resource can't
/// silently regress the way the scope-filtering fix once did.
/// </summary>
public sealed class TargetCompanyWriteTests : IClassFixture<E2eFixture>
{
    public const string HeaderName = "X-Company-Scope";

    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public TargetCompanyWriteTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    private async Task<(int Status, JsonElement? Data)> PostAsync(string url, object? body, string? token, string? scope = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (scope != null) req.Headers.Add(HeaderName, scope);
        if (body != null) req.Content = JsonContent.Create(body);
        using var resp = await _db.Client.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        JsonElement? data = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null) data = d.Clone();
        }
        return ((int)resp.StatusCode, data);
    }

    // ── Resource table: create payload + where the created row lives ────────
    private sealed record Resource(
        string Key, string PostPath, string Table, string Column,
        Func<string, object> Payload, int EntityType);

    private static readonly Resource[] Resources =
    {
        new("vehicle",  "/api/v1/vehicles",  "Vehicles",  "RegistrationNumber",
            s => new { registrationNumber = s, name = "target-company vehicle" }, 4),
        new("driver",   "/api/v1/drivers",   "Drivers",   "EmployeeId",
            s => new { employeeId = s, firstName = "Target", lastName = "Company" }, 5),
        new("geofence", "/api/v1/geofences", "Geofences", "Name",
            s => new { name = s, type = 0, centerLatitude = 28.5, centerLongitude = 77.2, radius = 500 }, 7),
        new("route",    "/api/v1/routes",    "Routes",    "Name",
            s => new { name = s, type = 0, originName = "Origin", originLatitude = 0.0, originLongitude = 0.0 }, 21),
        new("device",   "/api/v1/devices",   "Devices",   "IdentityValue",
            s => new { vendorCode = "sample-json", deviceType = 0, identityType = 0, identityValue = s }, 20),
    };

    private async Task<string?> CompanyOfAsync(Resource r, string unique)
    {
        return await _db.ScalarAsync($"SELECT \"CompanyId\"::text FROM \"{r.Table}\" WHERE \"{r.Column}\" = '{unique}' AND \"IsDeleted\" = false");
    }

    /// <summary>Anonymous payload → dictionary so tests can add companyId.</summary>
    private static Dictionary<string, object> ToDict(object payload)
    {
        return new Dictionary<string, object>(payload.GetType().GetProperties()
            .ToDictionary(p => p.Name[..1].ToLowerInvariant() + p.Name[1..], p => p.GetValue(payload)!));
    }

    [Fact]
    public async Task Target_Company_Controls_The_Write_Everywhere()
    {
        await RbacFixtures.SeedAsync(_db);
        const string saEmail = "admin@freebuff.com";
        const string basicEmail = RbacFixtures.BasicAdminEmail;

        var saToken = await TokenAsync(saEmail);
        var basicToken = await TokenAsync(basicEmail);

        // Resolve the two company ids (demo + E2E Basic Co).
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/companies?pageSize=100"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", saToken);
            using var resp = await _db.Client.SendAsync(req);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("data").GetProperty("items");
            var demoId = items.EnumerateArray().First(c => c.GetProperty("name").GetString() == "Demo Fleet Company").GetProperty("id").GetString()!;
            var basicId = items.EnumerateArray().First(c => c.GetProperty("name").GetString() == "E2E Basic Co").GetProperty("id").GetString()!;
            Assert.NotEqual(demoId, basicId);

            foreach (var r in Resources)
            {
                var suffix = Guid.NewGuid().ToString("N")[..6];

                // ── 1. SA scoped to [demo], targeting demo → lands in demo ────
                var uniq1 = $"TGT-{r.Key.ToUpper()[..3]}-1-{suffix}";
                var p1 = ToDict(r.Payload(uniq1)); p1["companyId"] = demoId;
                var (s1, _) = await PostAsync(r.PostPath, p1, saToken, scope: demoId);
                Assert.Equal(201, s1);
                Assert.Equal(demoId, await CompanyOfAsync(r, uniq1));
                _output.WriteLine($"PASS  {r.Key,-9} SA scope=[demo] + target demo → lands in demo");

                // ── 3. SA scoped to [demo] but explicitly targeting basic → lands in basic ──
                var uniq3 = $"TGT-{r.Key.ToUpper()[..3]}-3-{suffix}";
                var p3 = ToDict(r.Payload(uniq3)); p3["companyId"] = basicId;
                var (s3, _) = await PostAsync(r.PostPath, p3, saToken, scope: demoId);
                Assert.Equal(201, s3);
                Assert.Equal(basicId, await CompanyOfAsync(r, uniq3));
                _output.WriteLine($"PASS  {r.Key,-9} SA scope=[demo] + target basic → lands in BASIC (target wins over scope)");

                // ── 2. SA with NO companyId (scope=ALL) → rejected 400, never a silent default ──
                var (s2, _) = await PostAsync(r.PostPath, r.Payload($"TGT-{r.Key.ToUpper()[..3]}-2-{suffix}"), saToken, scope: "ALL");
                Assert.Equal(400, s2);
                _output.WriteLine($"PASS  {r.Key,-9} SA no companyId (scope=ALL) → 400, no silent default");

                // ── 4. Non-SA forged companyId → ignored, forced to own tenant ──
                var uniq4 = $"TGT-{r.Key.ToUpper()[..3]}-4-{suffix}";
                var forged = ToDict(r.Payload(uniq4));
                forged["companyId"] = demoId; // Basic Admin tries to create into demo
                var (s4, _) = await PostAsync(r.PostPath, forged, basicToken);
                Assert.Equal(201, s4);
                Assert.Equal(basicId, await CompanyOfAsync(r, uniq4));
                _output.WriteLine($"PASS  {r.Key,-9} Basic Admin forged companyId → lands in OWN company (forced)");

                // ── Audit: the cross-tenant write (3) left a trail ─────────────
                var audit = await _db.ScalarAsync(
                    $"SELECT COUNT(*) FROM \"AuditLogs\" WHERE \"TenantId\" = '{basicId}' AND \"EntityType\" = {r.EntityType} " +
                    $"AND \"Source\" = 'SuperAdmin cross-tenant write'");
                Assert.True(int.TryParse(audit, out var n) && n >= 1,
                    $"{r.Key}: expected ≥1 cross-tenant audit row for target company basic, found {audit}");
                _output.WriteLine($"PASS  {r.Key,-9} audit trail written for the cross-tenant create");
            }
        }
    }
}