using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Infrastructure.Data;

/// <summary>
/// Canonical Page + Module Registry — the single source of truth for what
/// exists in the product.
///
/// Two levels, exactly as the product is structured:
///
///   MODULE (top-level grouping, e.g. "Fleet Operations")
///     └── PAGE  (one or more per module — Dashboard, Companies, Vehicles…)
///           └── 6 PERMISSIONS per page (view/create/update/delete/export/import)
///
///   - Page Key : stable id, doubles as the permission module code ("vehicle" → vehicle.view)
///   - Page.Module : the top-level module the page belongs to (one owner per page)
///   - Modules are what a Package grants a Company access to (Task 2/3);
///     a page is reachable only when (page.module ∈ company's package modules)
///     AND the user's role holds the page permission.
///
/// Consumers (all derived from this list, never hand-maintained separately):
///   - SeedData          → Module rows (module groups) + Permission rows (pages)
///   - PermissionService → effective permissions intersect page-module × role grants
///   - Backend guards    → permission codes follow "{PageKey}.{action}"
///   - Frontend config/pages.ts → nav groups, Modules page, package module pickers
///
/// Mirror: frontend/src/config/pages.ts must contain the same keys/labels/routes.
/// SeedData logs a drift warning at startup if the DB contains rows not in this registry.
/// </summary>
public sealed record ModuleDefinition(
    string Code,
    string Label,
    string Icon,
    bool IsCore,
    bool AdminOnly,
    int Order,
    string? Description = null);

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
    string Module,
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

    /// <summary>
    /// Top-level modules. These are the Module rows in the database and the unit
    /// of access a Package grants a Company. No duplicate codes/names may exist.
    /// </summary>
    public static readonly IReadOnlyList<ModuleDefinition> Modules = new[]
    {
        new ModuleDefinition("dashboard",    "Dashboard",            "layout-dashboard", true,  false, 1, "Analytics and overview"),
        new ModuleDefinition("fleet",        "Fleet Operations",     "truck",            true,  false, 2, "Vehicle, driver, route and geofence operations"),
        new ModuleDefinition("organization", "Organization & Access","building",         true,  false, 3, "Companies, users, roles, localization and settings"),
        new ModuleDefinition("platform",     "Platform Administration","crown",          true,  true,  4, "Super-admin platform management"),
    };

    public static readonly IReadOnlyList<PageDefinition> All = new[]
    {
        // ── Dashboard module ──────────────────────────────────────────────
        new PageDefinition("dashboard",    "Dashboard",            "/",                     "layout-dashboard", true,  false, false, true,  1,  "dashboard", "Analytics and overview"),

        // ── Fleet Operations module ───────────────────────────────────────
        new PageDefinition("vehicle",      "Vehicles",             "/vehicles",             "truck",            true,  false, false, true,  2,  "fleet", "Vehicle CRUD and tracking"),
        new PageDefinition("device",       "Devices",              "/devices",              "radio",            true,  false, false, false, 3,  "fleet", "Tracking devices, SIMs and vehicle assignments"),
        new PageDefinition("driver",       "Drivers",              "/drivers",              "users",            true,  false, false, true,  4,  "fleet", "Driver profiles and management"),
        new PageDefinition("geofence",     "Geofences",            "/geofences",            "globe",            true,  false, false, false, 5,  "fleet", "Geofence creation and monitoring"),
        new PageDefinition("route",        "Routes",               "/routes",               "navigation",       true,  false, false, false, 6,  "fleet", "Route planning and optimization"),
        new PageDefinition("trip",         "Trips",                "/trips",                "navigation",       true,  false, false, false, 7,  "fleet", "Trip planning, tracking and replay"),
        new PageDefinition("alert",        "Alerts",               null,                    "bell",             false, false, true,  false, 8,  "fleet", "Planned: alert entity exists, page not built yet"),
        new PageDefinition("fuel",         "Fuel",                 null,                    "fuel",             false, false, true,  false, 9,  "fleet", "Planned: fuel entity exists, page not built yet"),
        new PageDefinition("maintenance",  "Maintenance",          null,                    "wrench",           false, false, true,  false, 10, "fleet", "Planned: maintenance entity exists, page not built yet"),
        new PageDefinition("report",       "Reports",              null,                    "file-text",        false, false, true,  false, 11, "fleet", "Planned: no page yet"),

        // ── Organization & Access module ──────────────────────────────────
        new PageDefinition("company",      "Companies",            "/companies",            "building",         true,  false, false, true,  11, "organization", "Company administration"),
        new PageDefinition("user",         "Users",                "/users",                "user-cog",         true,  false, false, true,  12, "organization", "User profiles and access control"),
        new PageDefinition("role",         "Roles & Permissions",  "/roles",                "shield",           true,  false, false, true,  13, "organization", "Role and permission management"),
        new PageDefinition("localization", "Localization",         "/localization",         "globe",            true,  false, false, false, 14, "organization", "Languages and currencies"),
        new PageDefinition("settings",     "Settings",             "/settings",             "settings",         true,  false, false, false, 15, "organization", "Company settings and preferences"),
        new PageDefinition("document",     "Documents",            "/admin/companies/:id",  "file-text",        false, true,  false, false, 16, "organization", "Company documents (Company Detail tab)"),
        new PageDefinition("subscription", "Subscription",         null,                    "credit-card",      false, false, false, false, 17, "organization", "Subscription (Settings / Company Detail)"),
        new PageDefinition("client",       "Clients",              null,                    "building",         false, false, true,  false, 18, "organization", "Planned: clients entity exists, page not built yet"),
        new PageDefinition("notification", "Notifications",        null,                    "bell",             false, false, true,  false, 19, "organization", "Planned: notification entity exists, page not built yet"),

        // ── Platform Administration module ────────────────────────────────
        new PageDefinition("platform",     "Platform Admin",       "/admin/companies",      "crown",            true,  true,  false, true,  20, "platform", "Platform-level company management"),
        new PageDefinition("package",      "Packages",             "/packages",             "package",          true,  true,  false, false, 21, "platform", "Subscription packages and plans"),
        new PageDefinition("module",       "Modules",              "/modules",              "package",          true,  true,  false, false, 22, "platform", "Module catalog management"),
        new PageDefinition("devicevendor", "Device Vendors",       "/admin/device-vendors", "cpu",              true,  true,  false, false, 23, "platform", "Device vendor and adapter registry (Super Admin)"),
    };

    public static PageDefinition? ByKey(string key)
        => All.FirstOrDefault(p => p.Key == key);

    public static ModuleDefinition? ModuleByCode(string code)
        => Modules.FirstOrDefault(m => m.Code == code);

    /// <summary>The top-level module a page belongs to (null → page not registered).</summary>
    public static string? ModuleOfPage(string pageKey)
        => All.FirstOrDefault(p => p.Key == pageKey)?.Module;

    /// <summary>All pages that belong to a module, in registry order.</summary>
    public static IEnumerable<PageDefinition> PagesInModule(string moduleCode)
        => All.Where(p => p.Module == moduleCode).OrderBy(p => p.Order);

    /// <summary>All 6 permission codes for a page: vehicle → vehicle.view..vehicle.import.</summary>
    public static IEnumerable<string> CodesFor(string key)
        => Actions.Select(a => $"{key}.{a}");

    public static bool IsKnownAction(string action)
        => Actions.Contains(action);

    /// <summary>
    /// Tenant-visibility rule: what a tenant can actually navigate to. Active,
    /// non-Planned, non-AdminOnly pages that are in the sidebar with a real
    /// route — the same set the tenant /modules catalog, sidebar and permission
    /// engine expose. (The SQL predicate in ModulesController mirrors this.)
    /// </summary>
    public static bool IsLiveForTenant(Page p)
        => !p.Planned && p.Status == EntityStatus.Active && !p.AdminOnly && p.Nav && p.Route != null;

    /// <summary>Tenant-visibility rule for a module: Active only.</summary>
    public static bool IsLiveModuleForTenant(Module m)
        => m.Status == EntityStatus.Active;

    /// <summary>
    /// Canonical page view used by every catalog endpoint (public Modules screen,
    /// admin company-modules, admin modules). Field names are stable: Key/Label
    /// are what nav + permission groups consume.
    /// </summary>
    public static object PageView(Page p) => new
    {
        p.Id,
        p.Key,
        Label = p.Name,
        p.Planned,
        p.Nav,
        p.Route,
        p.AdminOnly,
        p.IsCore,
        Status = (int)p.Status,
        p.DisplayOrder,
        p.Description
    };

    /// <summary>All 6 permission codes for a page key, in canonical order.</summary>
    public static string[] PagePermissionCodes(string key)
        => Actions.Select(a => $"{key}.{a}").ToArray();
}
