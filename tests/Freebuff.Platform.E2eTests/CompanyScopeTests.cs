using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Contract for the stateless header-based Company Scope model:
///   frontend sends X-Company-Scope on every call → backend resolves
///   effective scope = requested ∩ permitted-company set.
///
/// Behaviors pinned here:
///  1. A non-cross-tenant user can never widen scope via the header — a forged
///     header naming another company is silently dropped (200, own company only).
///  2. SuperAdmin default (no header) spans companies; an explicit header subset
///     narrows every list surface to exactly those companies.
///  3. A bogus/deactivated id in the header is silently dropped (200, no error).
///  4. View scope never gates command-side writes — SuperAdmin edits a vehicle
///     of company A while viewing scope=[company B]; the write still succeeds
///     (single-target-company semantics). A normal user editing another
///     company's vehicle is rejected.
/// </summary>
public sealed class CompanyScopeTests : IClassFixture<E2eFixture>
{
    public const string HeaderName = "X-Company-Scope";

    private readonly E2eDb _db;
    private readonly ITestOutputHelper _output;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public CompanyScopeTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _output = output;
    }

    private async Task SeedAsync()
    {
        // Program seed creates the platform company + Demo Fleet Company (with
        // roles/users); fixtures add Company B (E2E Basic Co) with Basic Admin.
        await RbacFixtures.SeedAsync(_db);
    }

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    /// <summary>GET with optional scope header → (status, data).</summary>
    private async Task<(int Status, JsonElement? Data)> GetAsync(string url, string? token = null, string? scope = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (scope != null) req.Headers.Add(HeaderName, scope);
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

    private async Task<(int Status, JsonElement? Data)> SendAsync(HttpMethod method, string url,
        object? body = null, string? token = null, string? scope = null)
    {
        using var req = new HttpRequestMessage(method, url);
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

    private static List<string> Regs(JsonElement? data)
    {
        var regs = new List<string>();
        if (data != null && data.Value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
                if (item.TryGetProperty("registrationNumber", out var r)) regs.Add(r.GetString()!);
        return regs;
    }

    private static string? CompanyOf(JsonElement? data, string reg)
    {
        if (data == null || !data.Value.TryGetProperty("items", out var items)) return null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("registrationNumber", out var r) && r.GetString() == reg
                && item.TryGetProperty("companyId", out var c)) return c.GetString();
        }
        return null;
    }

    [Fact]
    public async Task Scope_Contract_Full_Flow()
    {
        await SeedAsync();
        const string saEmail = "admin@freebuff.com";
        const string demoEmail = "admin@demofleet.com";
        const string basicEmail = RbacFixtures.BasicAdminEmail;

        var saToken = await TokenAsync(saEmail);
        var demoToken = await TokenAsync(demoEmail);
        var basicToken = await TokenAsync(basicEmail);

        // ── Create deterministic cross-company data through the real API ──────
        var demoReg = "SCOPE-DEMO-" + Guid.NewGuid().ToString("N")[..6];
        var basicReg = "SCOPE-BASIC-" + Guid.NewGuid().ToString("N")[..6];
        var createDemo = await SendAsync(HttpMethod.Post, "/api/v1/vehicles",
            new { registrationNumber = demoReg, name = "Scope demo vehicle" }, demoToken);
        Assert.Equal(201, createDemo.Status);
        var createBasic = await SendAsync(HttpMethod.Post, "/api/v1/vehicles",
            new { registrationNumber = basicReg, name = "Scope basic vehicle" }, basicToken);
        Assert.Equal(201, createBasic.Status);

        // Company ids from the created rows (SA sees everything).
        var saAll = await GetAsync("/api/v1/vehicles?pageSize=100", saToken);
        var demoId = CompanyOf(saAll.Data, demoReg);
        var basicId = CompanyOf(saAll.Data, basicReg);
        Assert.NotNull(demoId);
        Assert.NotNull(basicId);
        Assert.NotEqual(demoId, basicId);

        _output.WriteLine($"demo company={demoId} basic company={basicId}");

        // ── 1. Normal user: forged header naming another company is dropped ────
        var caDefault = await GetAsync("/api/v1/vehicles?pageSize=100", demoToken);
        var caForged = await GetAsync("/api/v1/vehicles?pageSize=100", demoToken, scope: basicId);
        Assert.Equal(200, caForged.Status); // dropped silently, not 403
        var defaultRegs = Regs(caDefault.Data).OrderBy(r => r).ToList();
        var forgedRegs = Regs(caForged.Data).OrderBy(r => r).ToList();
        Assert.Contains(demoReg, forgedRegs);
        Assert.DoesNotContain(basicReg, forgedRegs);
        Assert.Equal(defaultRegs, forgedRegs); // identical to no-header result
        _output.WriteLine("PASS  CA forged header → identical to default (own company only)");

        // ── 2. SuperAdmin default spans companies; header narrows ─────────────
        Assert.Contains(demoReg, Regs(saAll.Data));
        Assert.Contains(basicReg, Regs(saAll.Data)); // SA sees both companies with no header

        var saDemo = await GetAsync("/api/v1/vehicles?pageSize=100", saToken, scope: demoId);
        Assert.Equal(200, saDemo.Status);
        Assert.Contains(demoReg, Regs(saDemo.Data));
        Assert.DoesNotContain(basicReg, Regs(saDemo.Data));
        foreach (var item in saDemo.Data!.Value.GetProperty("items").EnumerateArray())
            Assert.Equal(demoId, item.GetProperty("companyId").GetString()); // zero leakage
        _output.WriteLine("PASS  SA scope=[demo] → only demo rows");

        // Same narrowing on /users and /drivers list surfaces.
        var saUsers = await GetAsync("/api/v1/users?pageSize=100", saToken, scope: demoId);
        Assert.Equal(200, saUsers.Status);
        if (saUsers.Data!.Value.TryGetProperty("items", out var uItems) && uItems.ValueKind == JsonValueKind.Array)
            foreach (var u in uItems.EnumerateArray())
                Assert.Equal(demoId, u.GetProperty("companyId").GetString());
        _output.WriteLine("PASS  SA scope=[demo] narrows /users too");

        // ── 3. Bogus/deactivated id in the header is silently dropped ─────────
        var bogus = "00000000-0000-0000-0000-0000000000ab";
        var saBogus = await GetAsync("/api/v1/vehicles?pageSize=100", saToken, scope: $"{demoId},{bogus}");
        Assert.Equal(200, saBogus.Status);
        var bogusRegs = Regs(saBogus.Data);
        Assert.Contains(demoReg, bogusRegs);
        Assert.DoesNotContain(basicReg, bogusRegs);
        _output.WriteLine("PASS  SA header with bogus id → dropped, 200, demo only");

        // ── 4. Narrowing to the other company hides demo rows ─────────────────
        var saBasic = await GetAsync("/api/v1/vehicles?pageSize=100", saToken, scope: basicId);
        Assert.Contains(basicReg, Regs(saBasic.Data));
        Assert.DoesNotContain(demoReg, Regs(saBasic.Data));
        _output.WriteLine("PASS  SA scope=[basic] → basic rows only");

        // ── 5. View scope does NOT gate command-side writes ───────────────────
        // SA is viewing scope=[basic] yet edits the demo vehicle: single-target
        // write still succeeds (SA permitted set includes both companies).
        var demoVehicleId = DemoVehicleId(saAll.Data, demoReg);
        var editWhileScoped = await SendAsync(HttpMethod.Put, $"/api/v1/vehicles/{demoVehicleId}",
            new { name = "Scope demo vehicle edited" }, saToken, scope: basicId);
        Assert.Equal(200, editWhileScoped.Status);
        _output.WriteLine("PASS  SA write to demo vehicle while scope=[basic] → 200");

        // A normal user editing another company's vehicle is rejected.
        var editCross = await SendAsync(HttpMethod.Put, $"/api/v1/vehicles/{demoVehicleId}",
            new { name = "hacked" }, basicToken);
        Assert.NotEqual(200, editCross.Status);
        _output.WriteLine($"PASS  Basic Admin editing demo vehicle → rejected ({editCross.Status})");
    }

    private static string DemoVehicleId(JsonElement? data, string reg)
    {
        foreach (var item in data!.Value.GetProperty("items").EnumerateArray())
        {
            if (item.TryGetProperty("registrationNumber", out var r) && r.GetString() == reg)
                return item.GetProperty("id").GetString()!;
        }
        throw new Xunit.Sdk.XunitException($"created vehicle {reg} missing from SA list");
    }

    /// <summary>
    /// Generic scope contract for EVERY tenant-scoped list endpoint (driven by
    /// the page registry): an endpoint that "ignores scope entirely" fails here
    /// automatically, so a future page can't silently regress the way Devices /
    /// Geofences / Routes did. Per endpoint:
    ///   - SA no-header must span ≥2 companies (skipped when the fixture has no
    ///     cross-company data for that resource, e.g. devices live in demo only);
    ///   - SA scope=[demo] must return strictly fewer rows, all demo-owned;
    ///   - CA with a forged header naming another company must get exactly the
    ///     same rows as with no header (own company only, never widened).
    /// </summary>
    [Fact]
    public async Task Scope_Every_Tenant_List_Endpoint_Responds_To_Scope()
    {
        await SeedAsync();
        const string saEmail = "admin@freebuff.com";
        const string demoEmail = "admin@demofleet.com";
        var saToken = await TokenAsync(saEmail);
        var caToken = await TokenAsync(demoEmail);

        // Resolve the demo company (id + name) and any other company id.
        var comps = await GetAsync("/api/v1/admin/companies?pageSize=100", saToken);
        Assert.Equal(200, comps.Status);
        var compItems = comps.Data!.Value.GetProperty("items");
        var demo = compItems.EnumerateArray().First(c => c.GetProperty("name").GetString() == "Demo Fleet Company");
        var demoId = demo.GetProperty("id").GetString()!;
        var demoName = demo.GetProperty("name").GetString()!;
        var otherId = compItems.EnumerateArray().First(c => c.GetProperty("id").GetString() != demoId).GetProperty("id").GetString()!;

        // (page key, list path, row field carrying the row's company identity, admin-only?)
        var endpoints = new (string Page, string Path, string Field, bool AdminOnly)[]
        {
            ("vehicle",  "/api/v1/vehicles?pageSize=100",          "companyId",   false),
            ("driver",   "/api/v1/drivers?pageSize=100",           "companyId",   false),
            ("device",   "/api/v1/devices?pageSize=100",           "companyId",   false),
            ("geofence", "/api/v1/geofences?pageSize=100",         "companyName", false),
            ("route",    "/api/v1/routes?pageSize=100",            "companyName", false),
            ("user",     "/api/v1/users?pageSize=100",             "companyId",   false),
            ("role",     "/api/v1/roles?pageSize=100",             "companyId",   false),
            ("company",  "/api/v1/admin/companies?pageSize=100",   "id",          true),
        };

        foreach (var (page, path, field, adminOnly) in endpoints)
        {
            var all = await GetAsync(path, saToken);
            Assert.Equal(200, all.Status);
            var items = all.Data!.Value.GetProperty("items");
            var distinct = items.EnumerateArray()
                .Select(i => i.GetProperty(field).GetString())
                .Where(v => v != null).Distinct().ToList();
            if (distinct.Count < 2)
            {
                _output.WriteLine($"SKIP  {page,-9} ({path}) — no cross-company fixture data (distinct companies = {distinct.Count})");
                continue;
            }

            // SA scope=[demo] must narrow and leak nothing.
            var dem = await GetAsync(path, saToken, scope: demoId);
            Assert.Equal(200, dem.Status);
            var demItems = dem.Data!.Value.GetProperty("items");
            var demCount = demItems.GetArrayLength();
            Assert.True(demCount < items.GetArrayLength(),
                $"{page}: scope=[demo] returned {demCount} rows — not fewer than ALL ({items.GetArrayLength()}); endpoint ignores scope");
            var expected = field == "companyName" ? demoName : demoId;
            foreach (var row in demItems.EnumerateArray())
                Assert.Equal(expected, row.GetProperty(field).GetString()); // zero leakage
            _output.WriteLine($"PASS  {page,-9} ({path}) narrows {items.GetArrayLength()} → {demCount}, all demo");

            // CA forged header naming another company → identical to no-header.
            // (Admin-only endpoints are correctly 403 for CA outright — that is the
            // strongest possible denial, so no forged-header comparison applies.)
            var caDefault = await GetAsync(path, caToken);
            var caForged = await GetAsync(path, caToken, scope: otherId);
            if (adminOnly)
            {
                Assert.Equal(403, caDefault.Status);
                Assert.Equal(403, caForged.Status);
                _output.WriteLine($"PASS  {page,-9} CA blocked outright → 403 (admin-only endpoint)");
                continue;
            }
            Assert.Equal(200, caDefault.Status);
            Assert.Equal(200, caForged.Status);
            var defItems = caDefault.Data!.Value.GetProperty("items");
            var forgItems = caForged.Data!.Value.GetProperty("items");
            var defComps = defItems.EnumerateArray().Select(i => i.GetProperty(field).GetString()).Distinct().OrderBy(v => v).ToList();
            var forgComps = forgItems.EnumerateArray().Select(i => i.GetProperty(field).GetString()).Distinct().OrderBy(v => v).ToList();
            Assert.Equal(defItems.GetArrayLength(), forgItems.GetArrayLength());
            Assert.Equal(defComps, forgComps);
            _output.WriteLine($"PASS  {page,-9} CA forged header → identical to default ({defItems.GetArrayLength()} rows)");
        }
    }
}
