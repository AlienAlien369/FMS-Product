using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// Checklist items 1–2 from the architecture-rework verification: the startup
/// migration must run cleanly on a real Postgres, the PackageModules table must
/// actually exist after startup, no duplicate modules may remain, every page must
/// have exactly the 6 standard action permissions, and companies must resolve to
/// live packages. Also exercises the two real legacy-drift repairs: a dropped
/// PackageModules table (EnsureCreated no-op gap) and a dangling company PackageId.
/// </summary>
public sealed class StartupMigrationTests : IClassFixture<E2eFixture>
{
        private readonly E2eDb _db;
        private readonly Checker _checker;

        public StartupMigrationTests(E2eFixture fixture, ITestOutputHelper output)
        {
            _db = fixture.Db;
            _checker = new Checker(output);
        }

    private const string PACKAGE_MODULES_COUNT =
        "SELECT COUNT(*) FROM \"PackageModules\" WHERE \"IsDeleted\" = false";

    [Fact]
    public async Task FreshBoot_CreatesSchema_CanonicalRegistry_NoDuplicates()
    {
        var c = _checker;

        // Table that EnsureCreated would never add to an existing DB must exist.
        var tableExists = await _db.ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'PackageModules'");
        c.Check("PackageModules table exists after startup", tableExists == "1", $"count={tableExists}");

        var pmCount = await _db.ScalarAsync(PACKAGE_MODULES_COUNT);
        c.Check("PackageModules grants seeded (Basic 2 + Professional 3 + Enterprise 4)", pmCount == "9", $"count={pmCount}");

        // Exactly the 4 canonical group modules, no duplicate codes among live rows.
        var modules = await _db.ScalarAsync(
            "SELECT string_agg(\"Code\", ',' ORDER BY \"Code\") FROM \"Modules\" WHERE \"IsDeleted\" = false");
        c.Check("Modules deduped to the 4 canonical groups", modules == "dashboard,fleet,organization,platform", $"codes={modules}");
        var moduleDupes = await _db.ScalarAsync(
            "SELECT COUNT(*) FROM (SELECT \"Code\" FROM \"Modules\" WHERE \"IsDeleted\" = false GROUP BY \"Code\" HAVING COUNT(*) > 1) d");
        c.Check("No duplicate live module codes", moduleDupes == "0", $"dupes={moduleDupes}");

        // Permissions: exactly the 6 standard actions per page, nothing else.
        var totalPerms = await _db.ScalarAsync("SELECT COUNT(*) FROM \"Permissions\" WHERE \"IsDeleted\" = false");
        var distinctPages = await _db.ScalarAsync(
            "SELECT COUNT(DISTINCT split_part(\"Code\", '.', 1)) FROM \"Permissions\" WHERE \"IsDeleted\" = false");
        c.Check("Exactly 6 permissions per page",
            totalPerms == (int.Parse(distinctPages ?? "0") * 6).ToString(),
            $"total={totalPerms}, pages={distinctPages}");
        var badActionPerms = await _db.ScalarAsync(
            "SELECT COUNT(*) FROM \"Permissions\" WHERE \"IsDeleted\" = false AND \"Code\" !~ '^[a-z]+\\.(view|create|update|delete|export|import)$'");
        c.Check("No legacy/extra action permissions remain", badActionPerms == "0", $"bad={badActionPerms}");

        // Companies resolve to live packages (demo-fleet → Professional).
        var demoPkg = await _db.ScalarAsync(
            "SELECT p.\"Name\" FROM \"Companies\" c JOIN \"Packages\" p ON p.\"Id\" = c.\"PackageId\" WHERE c.\"Slug\" = 'demo-fleet' AND c.\"IsDeleted\" = false");
        c.Check("Demo Fleet Company resolves to a live package", demoPkg == "Professional", $"pkg={demoPkg}");
        var platformPkg = await _db.ScalarAsync(
            "SELECT p.\"Name\" FROM \"Companies\" c JOIN \"Packages\" p ON p.\"Id\" = c.\"PackageId\" WHERE c.\"Slug\" = 'platform' AND c.\"IsDeleted\" = false");
        c.Check("Platform Company resolves to a live package", platformPkg == "Enterprise", $"pkg={platformPkg}");

        c.AssertAll();
    }

    [Fact]
    public async Task DroppedPackageModulesTable_IsRecreatedOnReboot_WithGrantsRestored()
    {
        // Simulate the real production gap: an existing DB that predates the
        // PackageModules table (EnsureCreated is a no-op on existing DBs).
        await _db.ExecuteAsync("DROP TABLE IF EXISTS \"PackageModules\"");
        var gone = await _db.ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'PackageModules'");
        Assert.Equal("0", gone);

        await _db.RebootAsync(); // EnsureCreated no-ops; SchemaBootstrap + seed must repair

        var tableExists = await _db.ScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'PackageModules'");
        var pmCount = await _db.ScalarAsync(PACKAGE_MODULES_COUNT);
        Assert.Equal("1", tableExists);
        Assert.Equal("9", pmCount); // grants re-seeded for all 3 live packages
    }

    [Fact]
    public async Task DanglingCompanyPackageId_IsReHomedOnReboot()
    {
        // Simulate the legacy drift that locked a company out: its PackageId points
        // at a package row that no longer resolves as live (soft-deleted).
        var demoId = await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Companies\" WHERE \"Slug\" = 'demo-fleet'");
        Assert.NotNull(demoId);
        var basicId = await _db.ScalarAsync("SELECT \"Id\"::text FROM \"Packages\" WHERE \"Name\" = 'Basic'");
        Assert.NotNull(basicId);

        await _db.ExecuteAsync(
            $"UPDATE \"Companies\" SET \"PackageId\" = '{basicId}' WHERE \"Id\" = '{demoId}'");
        await _db.ExecuteAsync(
            $"UPDATE \"Packages\" SET \"IsDeleted\" = true WHERE \"Id\" = '{basicId}'");

        try
        {
            await _db.RebootAsync();

            var pkg = await _db.ScalarAsync(
                "SELECT p.\"Name\" FROM \"Companies\" c JOIN \"Packages\" p ON p.\"Id\" = c.\"PackageId\" WHERE c.\"Id\" = '" + demoId + "'");
            Assert.Equal("Professional", pkg); // demo-fleet re-homed by the seed repair

            var activeSubs = await _db.ScalarAsync(
                "SELECT COUNT(*) FROM \"Subscriptions\" s WHERE s.\"CompanyId\" = '" + demoId +
                "' AND s.\"IsDeleted\" = false AND s.\"Status\" = 0");
            Assert.Equal("1", activeSubs);
            var activePkg = await _db.ScalarAsync(
                "SELECT p.\"Name\" FROM \"Subscriptions\" s JOIN \"Packages\" p ON p.\"Id\" = s.\"PackageId\" WHERE s.\"CompanyId\" = '" + demoId +
                "' AND s.\"IsDeleted\" = false AND s.\"Status\" = 0");
            Assert.Equal("Professional", activePkg);
        }
        finally
        {
            // Restore Basic so this test's drift never leaks into sibling tests
            // (the module-grant re-seed counts assume all 3 packages stay live).
            await _db.ExecuteAsync(
                $"UPDATE \"Packages\" SET \"IsDeleted\" = false WHERE \"Id\" = '{basicId}'");
        }
    }
}
