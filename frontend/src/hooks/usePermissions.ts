import { useAuth } from '../contexts/AuthContext';

/**
 * Module-level permission helper.
 * Usage: const { can, canAny } = usePermissions();
 *        can('vehicle.create') → boolean
 *        canAny('vehicle.export', 'vehicle.import') → boolean
 */
export function usePermissions() {
  const { hasPermission, hasAnyPermission, hasAllPermissions, isSuperAdmin } = useAuth();

  /** Check a single permission — SuperAdmin always returns true */
  const can = (permission: string): boolean => hasPermission(permission);

  /** Check if ANY of the listed permissions is granted */
  const canAny = (...permissions: string[]): boolean => hasAnyPermission(permissions);

  /** Check if ALL of the listed permissions are granted */
  const canAll = (...permissions: string[]): boolean => hasAllPermissions(permissions);

  /** Convenience: returns an object of common CRUD+export flags for a module */
  const modulePerms = (module: string) => ({
    view:    can(`${module}.view`),
    create:  can(`${module}.create`),
    edit:    can(`${module}.edit`),
    delete:  can(`${module}.delete`),
    export:  can(`${module}.export`),
    import:  can(`${module}.import`),
    assign:  can(`${module}.assign`),
    track:   can(`${module}.track`),
    approve: can(`${module}.approve`),
  });

  return { can, canAny, canAll, modulePerms, isSuperAdmin };
}
