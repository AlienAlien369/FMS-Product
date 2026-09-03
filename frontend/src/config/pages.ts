import {
  LayoutDashboard, Building2, Truck, Users, Globe, Navigation,
  Settings, Shield, Package, Crown, UserCog, FileText, CreditCard,
  Bell, Fuel, Wrench,
} from 'lucide-react';

/**
 * CANONICAL PAGE REGISTRY (frontend mirror).
 *
 * This is the single source of truth for page identity on the frontend:
 *   - sidebar nav renders from PAGES (filtered by View permission)
 *   - App.tsx route guards derive permissions from PAGES
 *   - Roles & Permissions UI (RoleModal) renders its groups from PAGES
 *
 * Backend mirror: src/Freebuff.Platform.Infrastructure/Data/PageRegistry.cs
 * (SeedData derives Module + Permission rows from it). The two files must
 * stay in sync — same keys, same labels, same routes.
 *
 * The 6 actions per page are fixed: view | create | update | delete | export | import.
 */
export const PAGE_ACTIONS = ['view', 'create', 'update', 'delete', 'export', 'import'] as const;
export type PageAction = typeof PAGE_ACTIONS[number];

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
  order: number;
}

export const PAGES: PageDef[] = [
  // ── Top-level nav pages ──────────────────────────────────────────────
  { key: 'dashboard',    label: 'Dashboard',           route: '/',                icon: LayoutDashboard, nav: true,  adminOnly: false, planned: false, order: 1 },
  { key: 'company',      label: 'Companies',           route: '/companies',       icon: Building2,       nav: true,  adminOnly: false, planned: false, order: 2 },
  { key: 'platform',     label: 'Platform Admin',      route: '/admin/companies', icon: Crown,           nav: true,  adminOnly: true,  planned: false, order: 3 },
  { key: 'vehicle',      label: 'Vehicles',            route: '/vehicles',        icon: Truck,           nav: true,  adminOnly: false, planned: false, order: 4 },
  { key: 'driver',       label: 'Drivers',             route: '/drivers',         icon: Users,           nav: true,  adminOnly: false, planned: false, order: 5 },
  { key: 'geofence',     label: 'Geofences',           route: '/geofences',       icon: Globe,           nav: true,  adminOnly: false, planned: false, order: 6 },
  { key: 'route',        label: 'Routes',              route: '/routes',          icon: Navigation,      nav: true,  adminOnly: false, planned: false, order: 7 },
  { key: 'user',         label: 'Users',               route: '/users',           icon: UserCog,         nav: true,  adminOnly: false, planned: false, order: 8 },
  { key: 'role',         label: 'Roles & Permissions', route: '/roles',           icon: Shield,          nav: true,  adminOnly: false, planned: false, order: 9 },
  { key: 'package',      label: 'Packages',            route: '/packages',        icon: Package,         nav: true,  adminOnly: true,  planned: false, order: 10 },
  { key: 'module',       label: 'Modules',             route: '/modules',         icon: Package,         nav: true,  adminOnly: true,  planned: false, order: 11 },
  { key: 'localization', label: 'Localization',        route: '/localization',    icon: Globe,           nav: true,  adminOnly: false, planned: false, order: 12 },
  { key: 'settings',     label: 'Settings',            route: '/settings',        icon: Settings,        nav: true,  adminOnly: false, planned: false, order: 13 },

  // ── Real features that are tabs/sections, not top-level nav items ─────
  { key: 'document',     label: 'Documents',     route: '/admin/companies/:id', icon: FileText,   nav: false, adminOnly: true,  planned: false, order: 14 },
  { key: 'subscription', label: 'Subscription',  icon: CreditCard,              nav: false, adminOnly: false, planned: false, order: 15 },

  // ── Planned features — real entities exist, pages not built yet ───────
  { key: 'client',       label: 'Clients',       icon: Building2,  nav: false, adminOnly: false, planned: true, order: 16 },
  { key: 'trip',         label: 'Trips',         icon: Navigation, nav: false, adminOnly: false, planned: true, order: 17 },
  { key: 'alert',        label: 'Alerts',        icon: Bell,       nav: false, adminOnly: false, planned: true, order: 18 },
  { key: 'fuel',         label: 'Fuel',          icon: Fuel,       nav: false, adminOnly: false, planned: true, order: 19 },
  { key: 'maintenance',  label: 'Maintenance',   icon: Wrench,     nav: false, adminOnly: false, planned: true, order: 20 },
  { key: 'report',       label: 'Reports',       icon: FileText,   nav: false, adminOnly: false, planned: true, order: 21 },
  { key: 'notification', label: 'Notifications', icon: Bell,       nav: false, adminOnly: false, planned: true, order: 22 },
];

/** Permission code for a page + action, e.g. pagePermission('vehicle', 'create') → 'vehicle.create'. */
export const pagePermission = (key: string, action: PageAction = 'view') => `${key}.${action}`;

export const pageByKey = (key: string): PageDef | undefined => PAGES.find(p => p.key === key);

/** Only pages that can appear in the sidebar, ordered. */
export const NAV_PAGES: PageDef[] = PAGES.filter(p => p.nav).sort((a, b) => a.order - b.order);

/** All pages that participate in role permissions (nav + real tabs), excluding planned-only rows. */
export const PERMISSION_GROUPS: PageDef[] = PAGES
  .filter(p => !p.planned)
  .sort((a, b) => a.order - b.order);