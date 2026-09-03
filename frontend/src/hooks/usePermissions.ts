import { useAuth } from '../contexts/AuthContext';

/**
 * Module-level permission helper — exactly 6 standardized actions per module.
 *
 * Usage:
 *   const { can, canAny, canAll, modulePerms } = usePermissions();
 *   can('vehicle.create')              → boolean
 *   canAny('vehicle.export', 'vehicle.import') → boolean
 *   modulePerms('vehicle')             → { view, create, update, delete, export, import }
 */
export function usePermissions() {
  const { hasPermission, hasAnyPermission, hasAllPermissions, user } = useAuth();
  const isSuperAdmin = user?.roles?.includes('SuperAdmin') ?? false;

  /** Check a single permission — SuperAdmin always returns true */
  const can = (permission: string): boolean => hasPermission(permission);

  /** Check if ANY of the listed permissions is granted */
  const canAny = (...permissions: string[]): boolean => hasAnyPermission(permissions);

  /** Check if ALL of the listed permissions are granted */
  const canAll = (...permissions: string[]): boolean => hasAllPermissions(permissions);

  /**
   * Returns exactly 6 boolean flags for a module:
   *   view, create, update, delete, export, import
   */
  const modulePerms = (module: string) => ({
    view:    can(`${module}.view`),
    create:  can(`${module}.create`),
    update:  can(`${module}.update`),
    delete:  can(`${module}.delete`),
    export:  can(`${module}.export`),
    import:  can(`${module}.import`),
  });

  return { can, canAny, canAll, modulePerms, isSuperAdmin };
}
