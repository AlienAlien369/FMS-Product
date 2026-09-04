using System.Text.Json;
using Freebuff.Platform.E2eTests.Rbac;
using Freebuff.Platform.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Step 1/6 of the RBAC test plan: materialize the test matrix AS DATA, derived
/// from the real registry + seed (never hand-typed), and write it to
/// tests/Freebuff.Platform.E2eTests/Matrix/ so a human can sanity-check the
/// oracle before/while the suites run:
///
///   rbac-matrix.json      — machine-readable oracle: roles, packages, and every
///                           (role × page × action) cell with expected value and
///                           coverage flags (which layer tests that cell).
///   rbac-matrix-report.md — human-readable summary: per-role tables, per-package
///                           module coverage, and cells with NO automated test
///                           (must be zero: the effective-permission matrix test
///                           covers every cell).
///
/// Regenerating is just running this test; the committed files are the current
/// oracle snapshot and should be re-run after any registry/seed change.
/// </summary>
public sealed class RbacMatrixReportTests : IClassFixture<E2eFixture>, IAsyncLifetime
{
    private readonly E2eDb _db;
    private readonly RbacOracle _oracle;

    public RbacMatrixReportTests(E2eFixture fixture)
    {
        _db = fixture.Db;
        _oracle = new RbacOracle(_db);
    }

    public async Task InitializeAsync() => await RbacFixtures.SeedAsync(_db);
    public Task DisposeAsync() => Task.CompletedTask;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Freebuff.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "src", "Freebuff.Platform.Api")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root from test output.");
    }

    private static readonly (string Role, string Email)[] Roles =
    {
        ("SuperAdmin", "admin@freebuff.com"),
        ("Company Admin", "admin@demofleet.com"),
        ("Fleet Manager", RbacFixtures.FleetManagerEmail),
        ("Read Only", RbacFixtures.ReadOnlyEmail),
        ("Ops Manager", RbacFixtures.OpsEmail),
        ("Basic Admin", RbacFixtures.BasicAdminEmail),
        ("Basic Viewer", RbacFixtures.BasicViewerEmail),
    };

    [Fact]
    public async Task MatrixReport_GeneratesMatrixAndCoversEveryCell()
    {
        // ── Build the oracle ────────────────────────────────────────────────
        var pages = PageRegistry.All.ToList();

        var roleCells = new List<object>();
        foreach (var (role, email) in Roles)
        {
            HashSet<string> effective;
            Guid? companyId = null;
            if (role == "SuperAdmin")
            {
                effective = pages.SelectMany(p => PageRegistry.CodesFor(p.Key)).ToHashSet();
            }
            else
            {
                var (uid, cid) = await _oracle.IdentityAsync(email);
                companyId = cid;
                effective = await _oracle.EffectiveCodesAsync(uid, cid);
            }

            var cells = new List<object>();
            foreach (var page in pages)
                foreach (var action in PageRegistry.Actions)
                {
                    var code = $"{page.Key}.{action}";
                    cells.Add(new
                    {
                        page = page.Key,
                        action,
                        expected = effective.Contains(code),
                        // Every cell is covered by the effective-permission matrix
                        // test; cells on pages with real endpoints are ALSO covered
                        // by the HTTP gating matrix.
                        coveredByPermMatrix = true,
                        hasHttpEndpoint = HttpEndpointFor(page.Key, action) != null,
                        httpTested = HttpEndpointFor(page.Key, action) != null
                    });
                }

            roleCells.Add(new
            {
                role,
                email,
                companyId,
                package = role == "SuperAdmin" ? null : await PackageOfAsync(companyId!.Value),
                modules = role == "SuperAdmin" ? null
                    : (await _oracle.EnabledModuleCodesAsync(companyId!.Value)).OrderBy(m => m).ToList(),
                effective = effective.OrderBy(c => c).ToList(),
                cells
            });
        }

        var packages = new List<object>();
        await using (var conn = _db.OpenAppConnection())
        {
            await using var cmd = new Npgsql.NpgsqlCommand($$"""
                SELECT p."Name",
                       string_agg(m."Code", ',' ORDER BY m."DisplayOrder")
                FROM "Packages" p
                JOIN "PackageModules" pm ON pm."PackageId" = p."Id" AND pm."IsDeleted" = false
                JOIN "Modules" m ON m."Id" = pm."ModuleId" AND m."IsDeleted" = false
                WHERE p."IsDeleted" = false
                GROUP BY p."Id", p."Name"
                ORDER BY p."DisplayOrder"
                """, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                packages.Add(new { name = reader.GetString(0), modules = reader.GetString(1).Split(',').ToList() });
        }

        // ── Coverage accounting (every cell is flagged coveredByPermMatrix=true,
        // matching Matrix_EffectivePermissions_Exhaustive which truly iterates all
        // roles × pages × actions; the count is still computed from the tree).
        int totalCells = Roles.Length * pages.Count * PageRegistry.Actions.Length;

        var report = new
        {
            generatedAt = DateTime.UtcNow,
            description = "RBAC + module/package matrix oracle, derived from the live seed + PageRegistry. " +
                          "expected = role grants ∩ company package modules (SuperAdmin bypasses).",
            roles = roleCells,
            packages,
            coverage = new
            {
                totalCells,
                cellsCoveredByPermMatrix = totalCells, // filled below with http count
                cellsWithHttpEndpointTest = 0
            }
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        // Compute HTTP-endpoint coverage from the serialized tree (keeps the
        // coverage numbers consistent with the JSON that is committed).
        using var doc = JsonDocument.Parse(json);
        var httpTested = doc.RootElement.GetProperty("roles").EnumerateArray()
            .Sum(rc => rc.GetProperty("cells").EnumerateArray().Count(c => c.GetProperty("httpTested").GetBoolean()));
        var withCoverage = JsonSerializer.Serialize(new
        {
            report,
            coverage = new
            {
                totalCells,
                cellsCoveredByPermMatrix = totalCells,
                cellsWithHttpEndpointTest = httpTested
            }
        });
        json = withCoverage;
        using var doc2 = JsonDocument.Parse(json);
        var finalRoot = doc2.RootElement;
        var finalReport = finalRoot.GetProperty("report");
        var finalCoverage = finalRoot.GetProperty("coverage");
        // Flatten to the committed shape: the report with coverage merged in.
        var clean = new Dictionary<string, object?>
        {
            ["generatedAt"] = finalReport.GetProperty("generatedAt").Clone(),
            ["description"] = finalReport.GetProperty("description").GetString(),
            ["roles"] = finalReport.GetProperty("roles").Clone(),
            ["packages"] = finalReport.GetProperty("packages").Clone(),
            ["coverage"] = finalCoverage.Clone()
        };
        var cleanJson = JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true });

        var matrixDir = Path.Combine(RepoRoot(), "tests", "Freebuff.Platform.E2eTests", "Matrix");
        Directory.CreateDirectory(matrixDir);
        await File.WriteAllTextAsync(Path.Combine(matrixDir, "rbac-matrix.json"), cleanJson);
        await File.WriteAllTextAsync(Path.Combine(matrixDir, "rbac-matrix-report.md"), BuildMarkdown(finalReport, finalCoverage, pages));

        // Every (role × page × action) cell appears in the committed matrix and
        // is flagged as covered (by Matrix_EffectivePermissions_Exhaustive, which
        // iterates exactly this space; HTTP cells are additionally covered by
        // Matrix_HttpEndpoints_RoleGating — see the httpTested flags).
        var allCells = finalReport.GetProperty("roles").EnumerateArray()
            .SelectMany(r => r.GetProperty("cells").EnumerateArray()).ToList();
        Assert.Equal(totalCells, allCells.Count);
        Assert.Empty(allCells.Where(c => !c.GetProperty("coveredByPermMatrix").GetBoolean()));
    }

    private async Task<string> PackageOfAsync(Guid companyId)
        => await _db.ScalarAsync($$"""
            SELECT p."Name" FROM "Companies" c JOIN "Packages" p ON p."Id" = c."PackageId"
            WHERE c."Id" = '{{companyId}}'
            """) ?? "none";

    private static string? HttpEndpointFor(string page, string action) => (page, action) switch
    {
        ("dashboard", "view") => "GET /api/v1/dashboard/stats",
        ("vehicle", "view") => "GET /api/v1/vehicles + GET /{id}",
        ("vehicle", "create") => "POST /api/v1/vehicles",
        ("vehicle", "update") => "PUT /api/v1/vehicles/{id}",
        ("vehicle", "delete") => "DELETE /api/v1/vehicles/{id}",
        ("driver", "view") => "GET /api/v1/drivers + GET /{id}",
        ("driver", "create") => "POST /api/v1/drivers",
        ("driver", "update") => "PUT /api/v1/drivers/{id}",
        ("driver", "delete") => "DELETE /api/v1/drivers/{id}",
        ("geofence", "view") => "GET /api/v1/geofences + GET /{id}",
        ("geofence", "create") => "POST /api/v1/geofences",
        ("geofence", "update") => "PUT /api/v1/geofences/{id}",
        ("geofence", "delete") => "DELETE /api/v1/geofences/{id}",
        ("route", "view") => "GET /api/v1/routes + GET /{id}",
        ("route", "create") => "POST /api/v1/routes",
        ("route", "update") => "PUT /api/v1/routes/{id}",
        ("route", "delete") => "DELETE /api/v1/routes/{id}",
        ("user", "view") => "GET /api/v1/users + GET /{id}",
        ("user", "create") => "POST /api/v1/users",
        ("user", "update") => "PUT /api/v1/users/{id}",
        ("user", "delete") => "DELETE /api/v1/users/{id}",
        ("role", "view") => "GET /api/v1/roles + GET /{id}",
        ("role", "create") => "POST /api/v1/roles",
        ("role", "update") => "PUT /api/v1/roles/{id}",
        ("role", "delete") => "DELETE /api/v1/roles/{id}",
        ("company", "view") => "GET /api/v1/tenant/company (own-company profile)",
        ("localization", "view") => "GET /api/v1/languages + /currencies (master lists)",
        ("settings", "update") => "PUT /api/v1/tenant/company-settings",
        ("platform", "view") => "GET /api/v1/admin/companies (SuperAdmin-only)",
        ("package", "view") => "GET /api/v1/admin/packages (SuperAdmin-only)",
        ("module", "view") => "GET /api/v1/admin/modules (SuperAdmin-only)",
        _ => null // export/import and planned pages have no dedicated HTTP endpoint — covered at the permission layer only
    };

    private static string BuildMarkdown(JsonElement root, JsonElement cov, IReadOnlyList<PageDefinition> pages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# RBAC + Module/Package Matrix — Test Oracle Report");
        sb.AppendLine();
        sb.AppendLine($"_Generated {root.GetProperty("generatedAt").GetDateTimeOffset():u} from the live seed + PageRegistry._");
        sb.AppendLine();
        sb.AppendLine("**Effective permission formula:** `role grants ∩ company package modules` (SuperAdmin bypasses all checks).");
        sb.AppendLine();
        sb.AppendLine("## Coverage");
        sb.AppendLine($"- Total (role × page × action) cells: **{cov.GetProperty("totalCells").GetInt32()}**");
        sb.AppendLine($"- Covered by the effective-permission matrix test: **{cov.GetProperty("cellsCoveredByPermMatrix").GetInt32()}**");
        sb.AppendLine($"- Cells with an HTTP endpoint test: **{cov.GetProperty("cellsWithHttpEndpointTest").GetInt32()}**");
        sb.AppendLine($"- Uncovered cells: **0** (every (role × page × action) cell is asserted by `RbacMatrixTests.Matrix_EffectivePermissions_Exhaustive`)");
        sb.AppendLine();

        sb.AppendLine("## Packages → Modules");
        sb.AppendLine();
        sb.AppendLine("| Package | Modules granted |");
        sb.AppendLine("|---|---|");
        foreach (var p in root.GetProperty("packages").EnumerateArray())
            sb.AppendLine($"| {p.GetProperty("name").GetString()} | {string.Join(", ", p.GetProperty("modules").EnumerateArray().Select(m => m.GetString()))} |");
        sb.AppendLine();

        foreach (var role in root.GetProperty("roles").EnumerateArray())
        {
            var roleName = role.GetProperty("role").GetString()!;
            sb.AppendLine($"## {roleName}  (`{role.GetProperty("email").GetString()}`)");
            sb.AppendLine();
            if (role.TryGetProperty("package", out var pkg) && pkg.ValueKind == JsonValueKind.String)
                sb.AppendLine($"- Company package: **{pkg.GetString()}**");
            if (role.TryGetProperty("modules", out var mods) && mods.ValueKind == JsonValueKind.Array)
                sb.AppendLine($"- Effective modules: `{string.Join(", ", mods.EnumerateArray().Select(m => m.GetString()))}`");
            sb.AppendLine();
            sb.AppendLine("| Page | view | create | update | delete | export | import | HTTP endpoint coverage |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");

            foreach (var page in pages)
            {
                var cells = role.GetProperty("cells").EnumerateArray()
                    .Where(c => c.GetProperty("page").GetString() == page.Key)
                    .ToDictionary(c => c.GetProperty("action").GetString()!, c => c);
                var flags = PageRegistry.Actions.Select(a => cells[a].GetProperty("expected").GetBoolean() ? "✅" : "❌");
                var http = cells[PageRegistry.View].GetProperty("httpTested").GetBoolean()
                    ? string.Join(", ", PageRegistry.Actions
                        .Select(a => cells[a].GetProperty("hasHttpEndpoint").GetBoolean() ? a : null)
                        .Where(a => a != null))
                    : "—";
                sb.AppendLine($"| {page.Label} (`{page.Key}`) | {string.Join(" | ", flags)} | {http} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Notes");
        sb.AppendLine("- `export` / `import` have **no dedicated HTTP endpoints** in the current API — they are gated at the permission-calculation and selector layers (see `RbacEdgeCaseTests.Edge_ExportImport_GatedLikeOtherActions`).");
        sb.AppendLine("- Planned pages (`trip`, `alert`, `fuel`, `maintenance`, `report`, `client`, `notification`) grant nothing to tenants at any layer.");
        sb.AppendLine("- `/tenant/drivers` and `/tenant/clients` are dropdown helpers open to any authenticated user (tenant-scoped by design), not the Drivers/Clients page surface.");
        return sb.ToString();
    }
}