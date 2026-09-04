using System.Text.RegularExpressions;
using Freebuff.Platform.Infrastructure.Data;
using Xunit;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Guards the "one registry, two views" invariant: the sidebar nav, permission
/// matrix and route guards all derive from the backend PageRegistry, and the
/// frontend mirror (frontend/src/config/pages.ts) must never drift from it.
/// Parses the committed TypeScript file and compares every field that the two
/// sides must agree on: keys, labels, routes, module assignment, nav/adminOnly/
/// planned flags and the fixed 6-action set.
/// </summary>
public class PageRegistryFrontendMirrorTests
{
    private static string FrontendPagesPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "frontend", "src", "config", "pages.ts");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("frontend/src/config/pages.ts not found from test output");
    }

    private static readonly Regex PageKeyRegex = new(@"\{\s*key:\s*'(?<key>[^']+)'", RegexOptions.Compiled);

    /// <summary>Extracts a single quoted property from a page-entry line, e.g. route, label.</summary>
    private static string? Prop(string line, string name)
    {
        var m = Regex.Match(line, $@"\b{name}:\s*'(?<v>(?:[^'\\]|\\.)*)'");
        return m.Success ? m.Groups["v"].Value : null;
    }

    private static bool Flag(string line, string name)
        => Regex.IsMatch(line, $@"\b{name}:\s*true");

    private static int Order(string line)
        => int.Parse(Regex.Match(line, @"\border:\s*(\d+)").Groups[1].Value);

    [Fact]
    public void FrontendMirror_MatchesPageRegistry_Exactly()
    {
        var lines = File.ReadAllLines(FrontendPagesPath());
        var entries = lines.Where(l => PageKeyRegex.IsMatch(l)).ToList();

        Assert.Equal(PageRegistry.All.Count, entries.Count);

        var backend = PageRegistry.All.ToDictionary(p => p.Key);
        var problems = new List<string>();

        foreach (var line in entries)
        {
            var key = PageKeyRegex.Match(line).Groups["key"].Value;
            if (!backend.TryGetValue(key, out var page))
            {
                problems.Add($"frontend has page '{key}' not in backend registry");
                continue;
            }

            if (Prop(line, "label") != page.Label)
                problems.Add($"{key}: label '{Prop(line, "label")}' != '{page.Label}'");
            if (Prop(line, "module") != page.Module)
                problems.Add($"{key}: module '{Prop(line, "module")}' != '{page.Module}'");
            if (Flag(line, "nav") != page.Nav)
                problems.Add($"{key}: nav {Flag(line, "nav")} != {page.Nav}");
            if (Flag(line, "adminOnly") != page.AdminOnly)
                problems.Add($"{key}: adminOnly {Flag(line, "adminOnly")} != {page.AdminOnly}");
            if (Flag(line, "planned") != page.Planned)
                problems.Add($"{key}: planned {Flag(line, "planned")} != {page.Planned}");
            if (Order(line) != page.Order)
                problems.Add($"{key}: order {Order(line)} != {page.Order}");

            // route is omitted entirely when null; quoted otherwise.
            var route = Regex.Match(line, @"\broute:\s*(?:'(?<v>(?:[^'\\]|\\.)*)'|undefined)");
            var routeVal = route.Success ? route.Groups["v"].Value : null;
            if (routeVal != page.Route)
                problems.Add($"{key}: route '{routeVal}' != '{page.Route}'");
        }

        // Backend pages missing from the frontend mirror.
        var frontendKeys = entries.Select(l => PageKeyRegex.Match(l).Groups["key"].Value).ToHashSet();
        foreach (var key in backend.Keys.Where(k => !frontendKeys.Contains(k)))
            problems.Add($"backend page '{key}' missing from frontend mirror");

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void FrontendMirror_ModuleGroups_Match()
    {
        var lines = File.ReadAllLines(FrontendPagesPath());
        var moduleRegex = new Regex(@"\{\s*code:\s*'(?<code>[^']+)'", RegexOptions.Compiled);
        var entries = lines.Where(l => moduleRegex.IsMatch(l)).ToList();
        Assert.Equal(PageRegistry.Modules.Count, entries.Count);

        var backend = PageRegistry.Modules.ToDictionary(m => m.Code);
        var problems = new List<string>();
        foreach (var line in entries)
        {
            var code = moduleRegex.Match(line).Groups["code"].Value;
            if (!backend.TryGetValue(code, out var mod))
            {
                problems.Add($"frontend module '{code}' not in backend registry");
                continue;
            }
            if (Prop(line, "label") != mod.Label) problems.Add($"{code}: label mismatch");
            if (Flag(line, "adminOnly") != mod.AdminOnly) problems.Add($"{code}: adminOnly mismatch");
            if (Order(line) != mod.Order) problems.Add($"{code}: order mismatch");
        }
        foreach (var code in backend.Keys.Where(k => !entries.Select(l => moduleRegex.Match(l).Groups["code"].Value).Contains(k)))
            problems.Add($"backend module '{code}' missing from frontend mirror");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void FrontendMirror_ExactlySixActions()
    {
        var ts = File.ReadAllText(FrontendPagesPath());
        Assert.Matches(@"PAGE_ACTIONS\s*=\s*\['view',\s*'create',\s*'update',\s*'delete',\s*'export',\s*'import'\]", ts);
    }
}