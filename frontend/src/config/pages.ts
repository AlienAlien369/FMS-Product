import {
  LayoutDashboard, Building2, Truck, Users, Globe, Navigation,
  Settings, Shield, Package, Crown, UserCog, FileText, CreditCard,
  Bell, Fuel, Wrench, Radio, Cpu,
} from 'lucide-react';

/**
 * CANONICAL PAGE + MODULE REGISTRY (frontend mirror).
 *
 * Two levels, mirroring src/Freebuff.Platform.Infrastructure/Data/PageRegistry.cs:
 *   MODULE (top-level group: Dashboard / Fleet Operations / Organization & Access /
 *           Platform Administration) — what a Package grants a Company.
 *     └── PAGE (Vehicles, Drivers, …) — what Roles grant permissions on.
 *
 *   - Sidebar nav renders grouped by MODULE (filtered by View permission + module
 *     access, which is expressed through the effective permission set).
 *   - Modules page lists MODULES and the pages inside them.
 *   - Packages page picks MODULES (moduleIds) that a package grants.
 *   - RoleModal permission groups still derive from PAGES.
 *
 * Keep keys/labels/routes/module assignments in sync with the backend registry.
 * The 6 actions per page are fixed: view | create | update | delete | export | import.
 */
export const PAGE_ACTIONS = ['view', 'create', 'update', 'delete', 'export', 'import'] as const;
export type PageAction = typeof PAGE_ACTIONS[number];

export interface ModuleDef {
  code: string;
  label: string;
  icon: any;
  adminOnly: boolean;
  order: number;
  /** Short blurb for the Modules page */
  description?: string;
}

/** Top-level modules. Every page below belongs to exactly one of these. */
export const MODULES: ModuleDef[] = [
  { code: 'dashboard',    label: 'Dashboard',               icon: LayoutDashboard, adminOnly: false, order: 1, description: 'Analytics and overview' },
  { code: 'fleet',        label: 'Fleet Operations',        icon: Truck,           adminOnly: false, order: 2, description: 'Vehicle, driver, route and geofence operations' },
  { code: 'organization', label: 'Organization & Access',   icon: Building2,       adminOnly: false, order: 3, description: 'Companies, users, roles, localization and settings' },
  { code: 'platform',     label: 'Platform Administration', icon: Crown,           adminOnly: true,  order: 4, description: 'Super-admin platform management' },
];

export interface PageDef {
  /** Stable key — equals the permission module code ("vehicle" → vehicle.view) */
  key: string;
  /** Canonical display name (nav label + permission group label) */
  label: string;
  /** Frontend route. Undefined = no standalone page yet (planned/tab feature). */
  route?: string;
  icon: any;
  /** Appears in the sidebar */
  nav: boolean;
  /** SuperAdmin-only page (also hidden from nav for non-SuperAdmin) */
  adminOnly: boolean;
  /** Real feature, page not built yet — flagged in the UI, never silently kept */
  planned: boolean;
  /** Top-level module this page belongs to (one owner per page) */
  module: string;
  order: number;
}

export const PAGES: PageDef[] = [
  // ── Dashboard module ─────────────────────────────────────────────────────
  { key: 'dashboard',    label: 'Dashboard',           route: '/',                icon: LayoutDashboard, nav: true,  adminOnly: false, planned: false, module: 'dashboard',    order: 1 },

  // ── Fleet Operations module ──────────────────────────────────────────────
  { key: 'vehicle',      label: 'Vehicles',            route: '/vehicles',        icon: Truck,           nav: true,  adminOnly: false, planned: false, module: 'fleet',        order: 2 },
  { key: 'device',       label: 'Devices',             route: '/devices',         icon: Radio,           nav: true,  adminOnly: false, planned: false, module: 'fleet',        order: 3 },
  { key: 'driver',       label: 'Drivers',             route: '/drivers',         icon: Users,           nav: true,  adminOnly: false, planned: false, module: 'fleet',        order: 4 },
  { key: 'geofence',     label: 'Geofences',           route: '/geofences',       icon: Globe,           nav: true,  adminOnly: false, planned: false, module: 'fleet',        order: 5 },
  { key: 'route',        label: 'Routes',              route: '/routes',          icon: Navigation,      nav: true,  adminOnly: false, planned: false, module: 'fleet',        order: 6 },
  { key: 'trip',         label: 'Trips',               icon: Navigation,          nav: false, adminOnly: false, planned: true,  module: 'fleet',        order: 7 },
  { key: 'alert',        label: 'Alerts',              icon: Bell,                nav: false, adminOnly: false, planned: true,  module: 'fleet',        order: 8 },
  { key: 'fuel',         label: 'Fuel',                icon: Fuel,                nav: false, adminOnly: false, planned: true,  module: 'fleet',        order: 9 },
  { key: 'maintenance',  label: 'Maintenance',         icon: Wrench,              nav: false, adminOnly: false, planned: true,  module: 'fleet',        order: 10 },
  { key: 'report',       label: 'Reports',             icon: FileText,            nav: false, adminOnly: false, planned: true,  module: 'fleet',        order: 11 },

  // ── Organization & Access module ─────────────────────────────────────────
  { key: 'company',      label: 'Companies',           route: '/companies',       icon: Building2,       nav: true,  adminOnly: false, planned: false, module: 'organization', order: 11 },
  { key: 'user',         label: 'Users',               route: '/users',           icon: UserCog,         nav: true,  adminOnly: false, planned: false, module: 'organization', order: 12 },
  { key: 'role',         label: 'Roles & Permissions', route: '/roles',           icon: Shield,          nav: true,  adminOnly: false, planned: false, module: 'organization', order: 13 },
  { key: 'localization', label: 'Localization',        route: '/localization',    icon: Globe,           nav: true,  adminOnly: false, planned: false, module: 'organization', order: 14 },
  { key: 'settings',     label: 'Settings',            route: '/settings',        icon: Settings,        nav: true,  adminOnly: false, planned: false, module: 'organization', order: 15 },
  { key: 'document',     label: 'Documents',     route: '/admin/companies/:id', icon: FileText,   nav: false, adminOnly: true,  planned: false, module: 'organization', order: 16 },
  { key: 'subscription', label: 'Subscription',  icon: CreditCard,              nav: false, adminOnly: false, planned: false, module: 'organization', order: 17 },
  { key: 'client',       label: 'Clients',       icon: Building2,  nav: false, adminOnly: false, planned: true,  module: 'organization', order: 18 },
  { key: 'notification', label: 'Notifications', icon: Bell,       nav: false, adminOnly: false, planned: true,  module: 'organization', order: 19 },

  // ── Platform Administration module ───────────────────────────────────────
  { key: 'platform',     label: 'Platform Admin',      route: '/admin/companies', icon: Crown,           nav: true,  adminOnly: true,  planned: false, module: 'platform',     order: 20 },
  { key: 'package',      label: 'Packages',            route: '/packages',        icon: Package,         nav: true,  adminOnly: true,  planned: false, module: 'platform',     order: 21 },
  { key: 'module',       label: 'Modules',             route: '/modules',         icon: Package,         nav: true,  adminOnly: true,  planned: false, module: 'platform',     order: 22 },
  { key: 'devicevendor', label: 'Device Vendors',      route: '/admin/device-vendors', icon: Cpu,     nav: true,  adminOnly: true,  planned: false, module: 'platform',     order: 23 },
];

/** Permission code for a page + action, e.g. pagePermission('vehicle', 'create') → 'vehicle.create'. */
export const pagePermission = (key: string, action: PageAction = 'view') => `${key}.${action}`;

export const pageByKey = (key: string): PageDef | undefined => PAGES.find(p => p.key === key);
export const moduleByCode = (code: string): ModuleDef | undefined => MODULES.find(m => m.code === code);

/** Pages that can appear in the sidebar, ordered. */
export const NAV_PAGES: PageDef[] = PAGES.filter(p => p.nav).sort((a, b) => a.order - b.order);

/** All pages that participate in role permissions (nav + real tabs), excluding planned-only rows. */
export const PERMISSION_GROUPS: PageDef[] = PAGES
  .filter(p => !p.planned)
  .sort((a, b) => a.order - b.order);

/** Pages inside a module, ordered by their registry order. */
export const pagesInModule = (moduleCode: string): PageDef[] =>
  PAGES.filter(p => p.module === moduleCode).sort((a, b) => a.order - b.order);
