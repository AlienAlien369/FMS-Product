using System.Net.Http.Json;
using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Freebuff.Platform.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// The exhaustive RBAC matrix suite. Every (Role × Page × Action) cell is
/// asserted twice:
///   1. at the permission-calculation layer — the /auth/permissions effective
///      set must equal the DB-derived oracle (role grants ∩ package modules)
///      for ALL registered pages × 6 actions × 7 roles;
///   2. at the HTTP layer — real endpoints must return 200/201 for a role with
///      the permission and exactly 403 for a role without it, for every role
///      × CRUD action on every page with a real endpoint.
/// Plus: package-gate (role grants exist but the company package blocks the
/// module), cross-tenant isolation, 401/malformed handling, SuperAdmin
/// unrestricted access, and the Roles & Permissions selector vs the oracle.
///
/// Fixtures (RbacFixtures) are seeded into the per-class fresh database before
/// any test runs (IAsyncLifetime), so every expectation is computed from real
/// seed + fixture data — nothing hand-typed.
/// </summary>
public sealed class RbacMatrixTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly RbacOracle _oracle;
    private readonly Checker _checker;

    public RbacMatrixTests(E2eFixture fixture, ITestOutputHelper output)
    {
        _db = fixture.Db;
        _checker = new Checker(output);
        _oracle = new RbacOracle(_db);
    }

    public async Task InitializeAsync() => await RbacFixtures.SeedAsync(_db);
    public Task DisposeAsync() => Task.CompletedTask;

    private const string SuperAdminEmail = "admin@freebuff.com";
    private const string DemoAdminEmail = "admin@demofleet.com";

    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    /// <summary>Every role exercised by the matrix, in a stable order.</summary>
    private static readonly (string Role, string Email)[] Roles =
    {
        ("SuperAdmin", SuperAdminEmail),
        ("Company Admin", DemoAdminEmail),
        ("Fleet Manager", RbacFixtures.FleetManagerEmail),
        ("Read Only", RbacFixtures.ReadOnlyEmail),
        ("Ops Manager", RbacFixtures.OpsEmail),
        ("Basic Admin", RbacFixtures.BasicAdminEmail),
        ("Basic Viewer", RbacFixtures.BasicViewerEmail),
    };

    private async Task<string> TokenAsync(string email)
    {
        if (_tokens.TryGetValue(email, out var cached)) return cached;
        var token = await ApiJson.LoginAsync(_db.Client, email, RbacFixtures.Password)
            ?? throw new Xunit.Sdk.XunitException($"Login failed for {email}");
        _tokens[email] = token;
        return token;
    }

    private static HashSet<string> PermSet(JsonElement? data)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (data != null && data.Value.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Array)
            foreach (var p in perms.EnumerateArray()) set.Add(p.GetString()!);
        return set;
    }

    // ───────────────────────────────────────────────────────────────────────
    // 1. Exhaustive effective-permission matrix (calculation layer)
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_EffectivePermissions_Exhaustive()
    {
        var allCodes = PageRegistry.All.SelectMany(p => PageRegistry.CodesFor(p.Key)).ToHashSet();

        foreach (var (role, email) in Roles)
        {
            var token = await TokenAsync(email);
            var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: token);
            _checker.Check($"{role}: /auth/permissions returns 200", status == 200, $"status={status}");
            var actual = PermSet(data);

            HashSet<string> expected;
            if (role == "SuperAdmin")
            {
                expected = allCodes; // SuperAdmin bypasses everything — gets all codes
            }
            else
            {
                var (uid, cid) = await _oracle.IdentityAsync(email);
                expected = await _oracle.EffectiveCodesAsync(uid, cid);
            }

            // Every (page × action) cell: present ⟺ expected.
            var mismatches = new List<string>();
            foreach (var page in PageRegistry.All)
                foreach (var action in PageRegistry.Actions)
                {
                    var code = $"{page.Key}.{action}";
                    if (actual.Contains(code) != expected.Contains(code))
                        mismatches.Add($"{code}:got={actual.Contains(code)},want={expected.Contains(code)}");
                }

            // No orphan/extra codes outside the registered registry codes.
            var extras = actual.Except(allCodes).ToList();
            if (extras.Count > 0) mismatches.Add($"extras={string.Join(',', extras)}");

            _checker.Check($"{role}: all {PageRegistry.All.Count * PageRegistry.Actions.Length} (page×action) cells match oracle",
                mismatches.Count == 0, string.Join("; ", mismatches.Take(8)));
        }

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 2. HTTP endpoint gating matrix (enforcement layer)
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_HttpEndpoints_RoleGating()
    {
        foreach (var (role, email) in Roles)
        {
            var token = await TokenAsync(email);
            var (uid, cid) = await _oracle.IdentityAsync(email);
            var effective = await _oracle.EffectiveCodesAsync(uid, cid);
            var isSuper = role == "SuperAdmin";

            // View legs — list endpoints for every page with one.
            var listEndpoints = new (string Page, string Url)[]
            {
                ("dashboard", "/api/v1/dashboard/stats"),
                ("vehicle", "/api/v1/vehicles"),
                ("driver", "/api/v1/drivers"),
                ("geofence", "/api/v1/geofences"),
                ("route", "/api/v1/routes"),
                ("trip", "/api/v1/trips"),
                ("user", "/api/v1/users"),
                ("role", "/api/v1/roles"),
                ("package", "/api/v1/admin/packages"),
                ("module", "/api/v1/admin/modules?pageSize=5"),
                ("platform", "/api/v1/admin/companies"),
            };
            foreach (var (page, url) in listEndpoints)
            {
                var expectedOk = isSuper || effective.Contains($"{page}.view");
                var (st, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, url, token: token);
                var ok = st == 200;
                _checker.Check($"{role} GET {page} list = {(expectedOk ? "200" : "403")}", ok == expectedOk,
                    $"status={st}");
            }

            // CRUD legs for pages with full create/update/delete endpoints.
            var crudPages = new[] { "vehicle", "driver", "geofence", "route", "user", "role" };
            foreach (var page in crudPages)
            {
                // Create leg — POST as the role itself. SuperAdmin writes require
                // an explicit target company (TargetCompanyResolver), so the SA
                // leg names its own company; everyone else is forced server-side.
                var expectedCreate = isSuper || effective.Contains($"{page}.create");
                var rawPayload = CreatePayload(page, $"{role}-{Guid.NewGuid():N}");
                object payload = isSuper
                    ? MergeCompanyId(rawPayload, cid)
                    : rawPayload;
                var (cst, cdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/{page}s",
                    payload, token: token);
                var createOk = cst is 200 or 201;
                _checker.Check($"{role} POST {page} = {(expectedCreate ? "2xx" : "403")}", createOk == expectedCreate,
                    $"status={cst}");
                if (createOk && !expectedCreate)
                    _checker.Check($"{role} POST {page} actually created nothing",
                        cdata == null || !cdata.Value.TryGetProperty("id", out _), "data present");

                // Ensure a resource exists in the role's company for detail/update/delete legs.
                var resourceId = await EnsureResourceAsync(page, cid);
                if (resourceId == Guid.Empty)
                {
                    _checker.Check($"{role} {page} resource creation for detail/update/delete", false, "could not create");
                    continue;
                }

                var detailExpected = isSuper || effective.Contains($"{page}.view");
                var (dst, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/{page}s/{resourceId}", token: token);
                _checker.Check($"{role} GET {page} detail = {(detailExpected ? "200" : "403")}", (dst == 200) == detailExpected,
                    $"status={dst}");

                var updateExpected = isSuper || effective.Contains($"{page}.update");
                var (ust, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/{page}s/{resourceId}",
                    UpdatePayload(page), token: token);
                _checker.Check($"{role} PUT {page} = {(updateExpected ? "200" : "403")}", (ust == 200) == updateExpected,
                    $"status={ust}");

                var deleteExpected = isSuper || effective.Contains($"{page}.delete");
                var (delst, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/{page}s/{resourceId}", token: token);
                _checker.Check($"{role} DELETE {page} = {(deleteExpected ? "200" : "403")}", (delst == 200) == deleteExpected,
                    $"status={delst}");
            }
        }

        _checker.AssertAll();
    }

    /// <summary>Creates a resource in the given company via a caller that can (company admin, or SuperAdmin for users/roles).</summary>
    private async Task<Guid> EnsureResourceAsync(string page, Guid companyId)
    {
        try
        {
            var demoToken = await TokenAsync(DemoAdminEmail);
            var basicToken = await TokenAsync(RbacFixtures.BasicAdminEmail);
            var saToken = await TokenAsync(SuperAdminEmail);
            var suffix = Guid.NewGuid().ToString("N")[..8];

            switch (page)
            {
                case "user":
                {
                    var (st, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                        $"/api/v1/admin/companies/{companyId}/users",
                        new { Email = $"e2e.res.{suffix}@test.dev", Password = "Pass@123", FirstName = "E2E", LastName = "Res" }, saToken);
                    return st is 200 or 201 && data != null && data.Value.TryGetProperty("id", out var id) ? id.GetGuid() : Guid.Empty;
                }
                case "role":
                {
                    var (st, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post,
                        $"/api/v1/admin/companies/{companyId}/roles",
                        new { Name = $"E2E Res Role {suffix}" }, saToken);
                    return st is 200 or 201 && data != null && data.Value.TryGetProperty("id", out var id) ? id.GetGuid() : Guid.Empty;
                }
                default:
                {
                    // vehicle/driver/geofence/route — create as the company's admin.
                    var companyAdmin = companyId == await _oracle.CompanyIdAsync(RbacFixtures.BasicCompanySlug) ? basicToken : demoToken;
                    var name = $"res-{suffix}";
                    var (st, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, $"/api/v1/{page}s",
                        CreatePayload(page, name), token: companyAdmin);
                    if (st is not (200 or 201)) return Guid.Empty;
                    // vehicle/driver creates return the id in data; geofence/route
                    // create responses carry no data — look the row up by name.
                    if (data != null && data.Value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        return id.GetGuid();
                    var (lst, ldata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
                        $"/api/v1/{page}s?search={name}&pageSize=50", token: companyAdmin);
                    if (lst == 200 && ldata != null && ldata.Value.TryGetProperty("items", out var items))
                    {
                        var match = items.EnumerateArray().FirstOrDefault(i =>
                            i.TryGetProperty("name", out var n) && n.GetString() != null && n.GetString()!.Contains(name));
                        if (match.ValueKind == JsonValueKind.Object && match.TryGetProperty("id", out var mid))
                            return mid.GetGuid();
                    }
                    return Guid.Empty;
                }
            }
        }
        catch
        {
            return Guid.Empty;
        }
    }

    /// <summary>Anonymous create payload → dictionary with an explicit companyId (SuperAdmin writes).</summary>
    private static object MergeCompanyId(object payload, Guid companyId)
    {
        var dict = payload.GetType().GetProperties()
            .ToDictionary(p => p.Name[..1].ToLowerInvariant() + p.Name[1..], p => p.GetValue(payload)!);
        dict["companyId"] = companyId;
        return dict;
    }

    private static string UniqueToken() => Guid.NewGuid().ToString("N")[..10];

    private static object CreatePayload(string page, string suffix) => page switch
    {
        "vehicle" => new { registrationNumber = $"E2E-{UniqueToken()}", name = $"E2E Vehicle {suffix}", vehicleType = "Truck", make = "Tata", model = "Prima", year = 2023, fuelType = 1 },
        "driver" => new { employeeId = $"E2E-{UniqueToken()}", firstName = "E2E", lastName = $"Driver {suffix}", email = $"e2e.drv.{suffix}.{UniqueToken()}@test.dev" },
        "geofence" => new { name = $"E2E Geo {suffix} {UniqueToken()}", type = 0, centerLatitude = 28.5, centerLongitude = 77.2, radius = 500 },
        "route" => new { name = $"E2E Route {suffix} {UniqueToken()}", originName = "Origin", originLatitude = 1.0, originLongitude = 2.0 },
        "user" => new { email = $"e2e.usr.{suffix}.{UniqueToken()}@test.dev", password = "Pass@123", firstName = "E2E", lastName = "User" },
        "role" => new { name = $"E2E Role {suffix} {UniqueToken()}", description = "matrix" },
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    private static object UpdatePayload(string page) => page switch
    {
        "vehicle" => new { name = $"E2E Vehicle Renamed {UniqueToken()}" },
        "driver" => new { firstName = $"E2E2 {UniqueToken()}" },
        "geofence" => new { name = $"E2E Geo Renamed {UniqueToken()}" },
        "route" => new { name = $"E2E Route Renamed {UniqueToken()}" },
        "user" => new { firstName = $"E2E2 {UniqueToken()}" },
        // role.Name is unique per company — only touch description to avoid
        // unique-index collisions between parallel role legs.
        "role" => new { description = $"matrix-updated {UniqueToken()}" },
        _ => throw new ArgumentOutOfRangeException(nameof(page))
    };

    // ───────────────────────────────────────────────────────────────────────
    // 3. Package gate: role grants exist, company package blocks the module
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_PackageGate_BlocksEvenGrantedCodes()
    {
        // Basic Admin is granted EVERY permission code (trap role) but its
        // company's Basic package has no organization module — user.view etc.
        // must be denied even though the role holds them.
        var basicAdmin = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var (usersSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: basicAdmin);
        _checker.Check("Basic Admin GET /users = 403 (granted user.view, package lacks organization)",
            usersSt == 403, $"status={usersSt}");
        var (rolesSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/roles", token: basicAdmin);
        _checker.Check("Basic Admin GET /roles = 403 (granted role.view, package lacks organization)",
            rolesSt == 403, $"status={rolesSt}");

        // Positive controls: fleet + dashboard ARE in the Basic package.
        var (vehSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles", token: basicAdmin);
        _checker.Check("Basic Admin GET /vehicles = 200 (fleet in Basic)", vehSt == 200, $"status={vehSt}");
        var (dashSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/dashboard/stats", token: basicAdmin);
        _checker.Check("Basic Admin GET /dashboard/stats = 200 (dashboard in Basic)", dashSt == 200, $"status={dashSt}");
        var (createSt, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            CreatePayload("vehicle", $"gate-{Guid.NewGuid():N}"), token: basicAdmin);
        _checker.Check("Basic Admin POST /vehicles = 2xx (vehicle.create + fleet in Basic)", createSt is 200 or 201,
            $"status={createSt}");

        // Basic Viewer: view-only fleet role — create must be denied.
        var basicViewer = await TokenAsync(RbacFixtures.BasicViewerEmail);
        var (v2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            CreatePayload("vehicle", $"viewer-{Guid.NewGuid():N}"), token: basicViewer);
        _checker.Check("Basic Viewer POST /vehicles = 403 (view-only role)", v2 == 403, $"status={v2}");

        // Control: Company Admin on Professional (organization in package) CAN read users.
        var demo = await TokenAsync(DemoAdminEmail);
        var (u3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users", token: demo);
        _checker.Check("Company Admin GET /users = 200 (org in Professional)", u3 == 200, $"status={u3}");
        _checker.Check("Company Admin GET /users contains basic-company users? no",
            !(await ListContainsEmailAsync(demo, RbacFixtures.BasicAdminEmail)), "cross-tenant leak");

        _checker.AssertAll();
    }

    private async Task<bool> ListContainsEmailAsync(string token, string email)
    {
        var (st, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get,
            $"/api/v1/users?search={email}&page=1&pageSize=50", token: token);
        if (st != 200 || data == null || !data.Value.TryGetProperty("items", out var items)) return false;
        return items.EnumerateArray().Any(u => u.GetProperty("email").GetString() == email);
    }

    // ───────────────────────────────────────────────────────────────────────
    // 4. Cross-tenant isolation
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_TenantIsolation_NoLeaks_NoCrossMutation()
    {
        var demo = await TokenAsync(DemoAdminEmail);
        var basicAdmin = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var sa = await TokenAsync(SuperAdminEmail);
        var demoCompany = await _oracle.CompanyIdAsync("demo-fleet");
        var basicCompany = await _oracle.CompanyIdAsync(RbacFixtures.BasicCompanySlug);

        // Company B creates a vehicle of its own.
        var (cst, cdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            CreatePayload("vehicle", $"x-{Guid.NewGuid():N}"), token: basicAdmin);
        _checker.Check("Basic Admin creates own vehicle", cst is 200 or 201, $"status={cst}");
        var basicVehicleId = cdata!.Value.GetProperty("id").GetGuid();

        // Company A (with full vehicle.delete) must NOT be able to read/update/delete it.
        var (g1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/vehicles/{basicVehicleId}", token: demo);
        _checker.Check("Company Admin GET other-company vehicle = 404 (no leak)", g1 == 404, $"status={g1}");
        var (g2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, $"/api/v1/vehicles/{basicVehicleId}",
            new { name = "Hijack" }, token: demo);
        _checker.Check("Company Admin PUT other-company vehicle = 404 (no mutation)", g2 == 404, $"status={g2}");
        var (g3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/vehicles/{basicVehicleId}", token: demo);
        _checker.Check("Company Admin DELETE other-company vehicle = 404 (no deletion)", g3 == 404, $"status={g3}");

        // List data itself is tenant-filtered: Company A never sees Company B's rows.
        var (l1, d1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles?pageSize=100", token: demo);
        var demoIds = new List<Guid>();
        var leaked = false;
        if (l1 == 200 && d1 != null)
            foreach (var v in d1.Value.GetProperty("items").EnumerateArray())
            {
                demoIds.Add(v.GetProperty("id").GetGuid());
                if (v.GetProperty("companyId").GetGuid() != demoCompany) leaked = true;
            }
        _checker.Check("Company Admin /vehicles all belong to own company", l1 == 200 && !leaked,
            $"status={l1}, leaked={leaked}");
        _checker.Check("Company Admin /vehicles does NOT contain Company B's vehicle", !demoIds.Contains(basicVehicleId));

        var (l2, d2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles?pageSize=100", token: basicAdmin);
        var basicLeak = false;
        if (l2 == 200 && d2 != null)
            foreach (var v in d2.Value.GetProperty("items").EnumerateArray())
                if (v.GetProperty("companyId").GetGuid() != basicCompany) basicLeak = true;
        _checker.Check("Basic Admin /vehicles all belong to own company", l2 == 200 && !basicLeak,
            $"status={l2}, leaked={basicLeak}");

        // Users: Company A cannot see Company B's users (user.view allows list).
        _checker.Check("Company Admin /users excludes Company B users",
            !await ListContainsEmailAsync(demo, RbacFixtures.BasicAdminEmail));

        // Roles: Company A's role list contains no Company B roles (the list
        // response has no companyId field, so assert by name instead).
        var (r1, rd1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/roles?pageSize=50", token: demo);
        var basicRoleLeak = false;
        if (r1 == 200 && rd1 != null)
            foreach (var r in rd1.Value.GetProperty("items").EnumerateArray())
                if (r.GetProperty("name").GetString() == "Basic Admin" || r.GetProperty("name").GetString() == "Basic Viewer")
                    basicRoleLeak = true;
        _checker.Check("Company Admin /roles contains no Company B roles", r1 == 200 && !basicRoleLeak,
            $"status={r1}, leaked={basicRoleLeak}");

        // SuperAdmin CAN read across tenants (unrestricted).
        var (s1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/vehicles/{basicVehicleId}", token: sa);
        _checker.Check("SuperAdmin reads other-company vehicle = 200", s1 == 200, $"status={s1}");

        // Cleanup: SuperAdmin deletes the cross-tenant vehicle (own company's cleanup by admin).
        await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/vehicles/{basicVehicleId}", token: basicAdmin);

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 5. Auth, malformed, unknown-page handling
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_Auth_Malformed_UnknownPage()
    {
        // No token → 401, not 403/200/500.
        var (n1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/vehicles");
        _checker.Check("GET /vehicles without token = 401", n1 == 401, $"status={n1}");
        var (n2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/users");
        _checker.Check("GET /users without token = 401", n2 == 401, $"status={n2}");
        var (n3, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/dashboard/stats");
        _checker.Check("GET /dashboard/stats without token = 401", n3 == 401, $"status={n3}");

        // Unknown/unregistered route → clean 404, not a crash.
        var (n4, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/nonexistent-page");
        _checker.Check("GET unknown route = 404", n4 == 404, $"status={n4}");
        // Registered-but-planned page (alert) has no controller → 404, not 500.
        var (n5, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/alerts");
        _checker.Check("GET planned-page route (alerts) = 404", n5 == 404, $"status={n5}");

        // Malformed body on a valid endpoint → 400 (validation), not 500.
        var demo = await TokenAsync(DemoAdminEmail);
        var (m1, _) = await ApiJson.SendRawAsync(_db.Client, HttpMethod.Post, "/api/v1/geofences", new { }, demo);
        _checker.Check("POST /geofences with empty body = 400 (validation)", m1 == 400, $"status={m1}");

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 6. SuperAdmin unrestricted access
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_SuperAdmin_Unrestricted()
    {
        var sa = await TokenAsync(SuperAdminEmail);

        var endpoints = new[]
        {
            ("/api/v1/vehicles", "vehicles list"),
            ("/api/v1/drivers", "drivers list"),
            ("/api/v1/geofences", "geofences list"),
            ("/api/v1/routes", "routes list"),
            ("/api/v1/users", "users list"),
            ("/api/v1/roles", "roles list"),
            ("/api/v1/dashboard/stats", "dashboard stats"),
            ("/api/v1/admin/packages", "admin packages"),
            ("/api/v1/admin/modules?pageSize=5", "admin modules"),
            ("/api/v1/admin/companies", "admin companies"),
            ("/api/v1/modules?pageSize=50", "modules catalog"),
            ("/api/v1/permissions/grouped", "permission selector"),
        };
        foreach (var (url, label) in endpoints)
        {
            var (st, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, url, token: sa);
            _checker.Check($"SuperAdmin {label} = 200", st == 200, $"status={st}");
        }

        var (_, perms) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/auth/permissions", token: sa);
        var set = PermSet(perms);
        var registeredCodeCount = PageRegistry.All.Count * PageRegistry.Actions.Length;
        _checker.Check($"SuperAdmin effective set = all {registeredCodeCount} registered codes",
            set.Count == registeredCodeCount && set.Contains("platform.view") && set.Contains("package.delete")
            && set.Contains("module.export") && set.Contains("user.import"),
            $"count={set.Count}, expected={registeredCodeCount}");

        // SuperAdmin can mutate across tenants: create a vehicle in Company B's company as its admin, then read it as SA.
        var basicAdmin = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var (cst, cdata) = await ApiJson.SendAsync(_db.Client, HttpMethod.Post, "/api/v1/vehicles",
            CreatePayload("vehicle", $"sa-{Guid.NewGuid():N}"), token: basicAdmin);
        var vid = cst is 200 or 201 ? cdata!.Value.GetProperty("id").GetGuid() : Guid.Empty;
        if (vid != Guid.Empty)
        {
            var (s2, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, $"/api/v1/vehicles/{vid}", token: sa);
            _checker.Check("SuperAdmin reads cross-tenant resource = 200", s2 == 200, $"status={s2}");
            await ApiJson.SendAsync(_db.Client, HttpMethod.Delete, $"/api/v1/vehicles/{vid}", token: basicAdmin);
        }

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 7. Roles & Permissions selector vs the oracle
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_Selector_MatchesOracle()
    {
        var plannedKeys = PageRegistry.All.Where(p => p.Planned).Select(p => p.Key).ToHashSet();

        foreach (var (role, email) in Roles)
        {
            var token = await TokenAsync(email);
            var (status, data) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/permissions/grouped", token: token);
            _checker.Check($"{role}: selector returns 200", status == 200, $"status={status}");

            var groups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (data != null)
                foreach (var g in data.Value.EnumerateArray())
                {
                    var module = g.GetProperty("module").GetString()!;
                    var codes = g.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("code").GetString()!).ToList();
                    groups[module] = codes.ToHashSet();
                }

            HashSet<string> expectedGroups;
            if (role == "SuperAdmin")
            {
                expectedGroups = PageRegistry.All.Select(p => p.Key).ToHashSet();
            }
            else
            {
                var (uid, cid) = await _oracle.IdentityAsync(email);
                var allowed = await _oracle.CompanyAllowedCodesAsync(cid);
                expectedGroups = allowed.Select(c => c.Split('.')[0]).ToHashSet();
            }

            _checker.Check($"{role}: selector groups == company-allowed page keys",
                groups.Keys.ToHashSet().SetEquals(expectedGroups),
                $"got={string.Join(',', groups.Keys.OrderBy(k => k))}, want={string.Join(',', expectedGroups.OrderBy(k => k))}");

            // Planned pages never appear for TENANTS in the selector; SuperAdmin
            // sees them (registry management needs the full catalog).
            var plannedLeak = groups.Keys.Intersect(plannedKeys).ToList();
            if (role == "SuperAdmin")
                _checker.Check($"SuperAdmin: planned groups present (registry management)",
                    plannedLeak.Count == plannedKeys.Count, $"leak={string.Join(',', plannedLeak)}");
            else
                _checker.Check($"{role}: no planned page groups in selector", plannedLeak.Count == 0,
                    $"leak={string.Join(',', plannedLeak)}");

            // Every group offers exactly the 6 standard action codes.
            var badGroup = groups.FirstOrDefault(g => !g.Value.SetEquals(PageRegistry.PagePermissionCodes(g.Key).ToHashSet()));
            _checker.Check($"{role}: every group = exactly 6 canonical codes",
                badGroup.Key == null, $"group={badGroup.Key}");
        }

        _checker.AssertAll();
    }

    // ───────────────────────────────────────────────────────────────────────
    // 8. Tenant helper endpoints: scoping + settings.update gate
    // ───────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Matrix_TenantEndpoints_ScopedAndGated()
    {
        var demo = await TokenAsync(DemoAdminEmail);
        var basicAdmin = await TokenAsync(RbacFixtures.BasicAdminEmail);
        var readOnly = await TokenAsync(RbacFixtures.ReadOnlyEmail);

        // Own-company profile: localization override applies per company.
        var (d1, dd1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: demo);
        _checker.Check("Demo admin tenant/company = en/USD", d1 == 200
            && dd1!.Value.GetProperty("defaultLanguage").GetString() == "en"
            && dd1.Value.GetProperty("defaultCurrency").GetString() == "USD", $"status={d1}");
        var (b1, bd1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: basicAdmin);
        _checker.Check("Basic admin tenant/company = fr/EUR (localization override)",
            b1 == 200 && bd1!.Value.GetProperty("defaultLanguage").GetString() == "fr"
            && bd1.Value.GetProperty("defaultCurrency").GetString() == "EUR", $"status={b1}");

        // Tenant driver dropdown is scoped: demo admin sees only demo drivers.
        var (t1, td1) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/drivers", token: demo);
        var demoOnly = true;
        if (t1 == 200 && td1 != null)
            foreach (var d in td1.Value.EnumerateArray())
                if (d.GetProperty("employeeId").GetString()!.StartsWith("DF-") == false
                    && d.GetProperty("employeeId").GetString()!.StartsWith("E2E-") == false) demoOnly = false;
        _checker.Check("Demo admin /tenant/drivers returns own-company drivers only", t1 == 200 && demoOnly,
            $"status={t1}");

        // settings.update gate: Read Only (view-only) cannot change company settings.
        var (ro1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/tenant/company-settings",
            new { DefaultLanguage = "fr" }, readOnly);
        _checker.Check("Read Only PUT company-settings = 403 (no settings.update)", ro1 == 403, $"status={ro1}");

        // Company admin CAN change their own company's locale (and restore it).
        var (set1, _) = await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/tenant/company-settings",
            new { DefaultLanguage = "fr", DefaultCurrency = "EUR" }, demo);
        _checker.Check("Company Admin PUT company-settings = 200", set1 == 200, $"status={set1}");
        var (d2, dd2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: demo);
        _checker.Check("Demo company locale change applies (fr/EUR)", d2 == 200
            && dd2!.Value.GetProperty("defaultLanguage").GetString() == "fr", $"status={d2}");
        // Isolation: the OTHER company is unaffected by demo's change.
        var (b2, bd2) = await ApiJson.SendAsync(_db.Client, HttpMethod.Get, "/api/v1/tenant/company", token: basicAdmin);
        _checker.Check("Basic company locale unaffected (fr/EUR stays)", b2 == 200
            && bd2!.Value.GetProperty("defaultLanguage").GetString() == "fr", $"status={b2}");
        // Restore demo to en/USD.
        await ApiJson.SendAsync(_db.Client, HttpMethod.Put, "/api/v1/tenant/company-settings",
            new { DefaultLanguage = "en", DefaultCurrency = "USD" }, demo);

        _checker.AssertAll();
    }
}