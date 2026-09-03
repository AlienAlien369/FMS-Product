import { NAV_PAGES, pagePermission } from './pages';

export interface NavItem {
  path: string;
  label: string;
  icon: any;
  /** Permission required to see this item (always `{page}.view`). */
  permission?: string;
  /** If true, only shown to SuperAdmin */
  adminOnly?: boolean;
  /** Nested children — shown as sub-items */
  children?: NavItem[];
}

/**
 * Navigation is DERIVED from the canonical page registry (config/pages.ts).
 * There is no separate hardcoded nav list — adding/removing a page in the
 * registry automatically adds/removes its nav item.
 */
export const NAVIGATION: NavItem[] = NAV_PAGES.map(p => ({
  path: p.route!,
  label: p.label,
  icon: p.icon,
  permission: pagePermission(p.key),
  adminOnly: p.adminOnly,
}));