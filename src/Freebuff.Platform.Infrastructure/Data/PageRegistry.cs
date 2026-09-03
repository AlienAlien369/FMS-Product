namespace Freebuff.Platform.Infrastructure.Data;

/// <summary>
/// Canonical Page/Module Registry — the single source of truth for page identity.
///
/// Every page/module in the product is defined HERE and only here:
///   - Key    : stable id, doubles as the permission module code ("vehicle" → vehicle.view)
///   - Label  : canonical display name (nav, permission groups)
///   - Route  : frontend route (null = no standalone page yet)
///   - Nav    : appears in the sidebar
///   - AdminOnly : SuperAdmin-only page
///   - Planned : real feature, page not built yet — flagged, never silently kept
///
/// Consumers (all derived from this list, never hand-maintained separately):
///   - SeedData          → Module rows + Permission rows (exactly 6 actions per page)
///   - Backend guards    → permission codes follow "{Key}.{action}" (RequirePermission)
///   - Frontend config/pages.tsx → nav, route guards, RoleModal permission groups
///
/// Mirror: frontend/src/config/pages.tsx must contain the same keys/labels/routes.
/// SeedData logs a drift warning at startup if the DB contains rows not in this registry.
/// </summary>
public sealed record PageDefinition(
    string Key,
    string Label,
    string? Route,
    string Icon,
    bool Nav,
    bool AdminOnly,
    bool Planned,
    bool IsCore,
    int Order,
    string? Description = null);

public static class PageRegistry
{
    public const string View = "view";
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Export = "export";
    public const string Import = "import";

    /// <summary>Exactly these 6 actions exist for every page — no more, no fewer.</summary>
    public static readonly string[] Actions = { View, Create, Update, Delete, Export, Import };

    public static readonly IReadOnlyList<PageDefinition> All = new[]
    {
        // ── Top-level nav pages ──────────────────────────────────────────
        new PageDefinition("dashboard",    "Dashboard",            "/",                     "layout-dashboard", true,  false, false, true,  1,  "Analytics and overview"),
        new PageDefinition("company",      "Companies",            "/companies",            "building",         true,  false, false, true,  2,  "Company administration"),
        new PageDefinition("platform",     "Platform Admin",       "/admin/companies",      "crown",            true,  true,  false, true,  3,  "Platform-level company management"),
        new PageDefinition("vehicle",      "Vehicles",             "/vehicles",             "truck",            true,  false, false, true,  4,  "Vehicle CRUD and tracking"),
        new PageDefinition("driver",       "Drivers",              "/drivers",              "users",            true,  false, false, true,  5,  "Driver profiles and management"),
        new PageDefinition("geofence",     "Geofences",            "/geofences",            "globe",            true,  false, false, false, 6,  "Geofence creation and monitoring"),
        new PageDefinition("route",        "Routes",               "/routes",               "navigation",       true,  false, false, false, 7,  "Route planning and optimization"),
        new PageDefinition("user",         "Users",                "/users",                "user-cog",         true,  false, false, true,  8,  "User profiles and access control"),
        new PageDefinition("role",         "Roles & Permissions",  "/roles",                "shield",           true,  false, false, true,  9,  "Role and permission management"),
        new PageDefinition("package",      "Packages",             "/packages",             "package",          true,  true,  false, false, 10, "Subscription packages and plans"),
        new PageDefinition("module",       "Modules",              "/modules",              "package",          true,  true,  false, false, 11, "Module catalog management"),
        new PageDefinition("localization", "Localization",         "/localization",         "globe",            true,  false, false, false, 12, "Languages and currencies"),
        new PageDefinition("settings",     "Settings",             "/settings",             "settings",         true,  false, false, false, 13, "Company settings and preferences"),

        // ── Real features that are tabs/sections, not top-level nav items ──
        new PageDefinition("document",     "Documents",            "/admin/companies/:id",  "file-text",        false, true,  false, false, 14, "Company documents (Company Detail tab)"),
        new PageDefinition("subscription", "Subscription",         null,                    "credit-card",      false, false, false, false, 15, "Subscription (Settings / Company Detail)"),

        // ── Planned features — real entities exist, pages not built yet ──
        new PageDefinition("client",       "Clients",              null,                    "building",         false, false, true,  false, 16, "Planned: clients entity exists, page not built yet"),
        new PageDefinition("trip",         "Trips",                null,                    "navigation",       false, false, true,  false, 17, "Planned: trip entity exists, page not built yet"),
        new PageDefinition("alert",        "Alerts",               null,                    "bell",             false, false, true,  false, 18, "Planned: alert entity exists, page not built yet"),
        new PageDefinition("fuel",         "Fuel",                 null,                    "fuel",             false, false, true,  false, 19, "Planned: fuel entity exists, page not built yet"),
        new PageDefinition("maintenance",  "Maintenance",          null,                    "wrench",           false, false, true,  false, 20, "Planned: maintenance entity exists, page not built yet"),
        new PageDefinition("report",       "Reports",              null,                    "file-text",        false, false, true,  false, 21, "Planned: no page yet"),
        new PageDefinition("notification", "Notifications",        null,                    "bell",             false, false, true,  false, 22, "Planned: notification entity exists, page not built yet"),
    };

    public static PageDefinition? ByKey(string key)
        => All.FirstOrDefault(p => p.Key == key);

    /// <summary>All 6 permission codes for a page: vehicle → vehicle.view..vehicle.import.</summary>
    public static IEnumerable<string> CodesFor(string key)
        => Actions.Select(a => $"{key}.{a}");

    public static bool IsKnownAction(string action)
        => Actions.Contains(action);
}