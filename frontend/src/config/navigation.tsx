import {
  LayoutDashboard, Truck, Users, Map, Route,
  Settings, Building2, Shield,
  Package, Globe, UserCog, Crown, Navigation,
} from 'lucide-react';

export interface NavItem {
  path: string;
  label: string;
  icon: any;
  /** Permission required to see this item. If null, visible to all authenticated users. */
  permission?: string;
  /** If true, only shown to SuperAdmin */
  adminOnly?: boolean;
  /** Nested children — shown as sub-items */
  children?: NavItem[];
}

/**
 * Centralized navigation configuration.
 * Every item has a permission requirement.
 * The sidebar renderer checks permissions dynamically.
 */
export const NAVIGATION: NavItem[] = [
  { path: '/', label: 'Dashboard', icon: LayoutDashboard, permission: 'dashboard.view' },
  { path: '/companies', label: 'Companies', icon: Building2, permission: 'company.view', adminOnly: true },
  { path: '/vehicles', label: 'Vehicles', icon: Truck, permission: 'vehicle.view' },
  { path: '/drivers', label: 'Drivers', icon: Users, permission: 'driver.view' },
  { path: '/geofences', label: 'Geofences', icon: Globe, permission: 'geofence.view' },
  { path: '/routes', label: 'Routes', icon: Navigation, permission: 'route.view' },

  { path: '/users', label: 'Users', icon: UserCog, permission: 'user.view' },
  { path: '/roles', label: 'Roles & Permissions', icon: Shield, permission: 'role.view' },
  { path: '/packages', label: 'Packages', icon: Package, permission: 'package.view', adminOnly: true },
  { path: '/modules', label: 'Modules', icon: Crown, permission: 'configuration.view', adminOnly: true },
  { path: '/admin/companies', label: 'Platform Admin', icon: Crown, adminOnly: true },
  { path: '/localization', label: 'Localization', icon: Globe, permission: 'configuration.view' },
  { path: '/settings', label: 'Settings', icon: Settings, permission: 'configuration.view' },
];
