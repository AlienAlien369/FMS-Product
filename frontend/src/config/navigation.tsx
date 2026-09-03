import { MODULES, NAV_PAGES, pagePermission } from './pages';

export interface NavItem {
  path: string;
  label: string;
  icon: any;
  /** Permission required to see this item (always `{page}.view`). */
  permission?: string;
  /** If true, only shown to SuperAdmin */
  adminOnly?: boolean;
}

export interface NavGroup {
  code: string;
  label: string;
  items: NavItem[];
}

/**
 * Navigation is DERIVED from the canonical page registry (config/pages.ts),
 * grouped by top-level module. There is no separate hardcoded nav list:
 * adding/removing a page or module in the registry changes the nav automatically.
 *
 * The renderer (Layout) shows a group only when at least one of its items is
 * visible for the current user (page-level View permission / SuperAdmin-only).
 */
const toNavItem = (p: { route?: string; label: string; icon: any; key: string; adminOnly: boolean }): NavItem | null =>
  p.route ? { path: p.route, label: p.label, icon: p.icon, permission: pagePermission(p.key), adminOnly: p.adminOnly } : null;

export const NAV_GROUPS: NavGroup[] = MODULES
  .map(mod => ({
    code: mod.code,
    label: mod.label,
    items: NAV_PAGES
      .filter(p => p.module === mod.code)
      .map(toNavItem)
      .filter((i): i is NavItem => i !== null),
  }))
  .filter(g => g.items.length > 0);

/** Flat list kept for header title lookup & simple consumers. */
export const NAVIGATION: NavItem[] = NAV_GROUPS.flatMap(g => g.items);
