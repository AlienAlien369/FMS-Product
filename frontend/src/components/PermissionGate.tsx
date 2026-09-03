import { type ReactNode } from 'react';
import { useAuth } from '../contexts/AuthContext';

/**
 * PermissionGate — renders children only if user has the required permission(s).
 * If not authorized, renders nothing (or an optional fallback).
 *
 * Usage:
 *   <PermissionGate permission="driver.create">
 *     <button>Add Driver</button>
 *   </PermissionGate>
 *
 *   <PermissionGate permission="driver.delete" fallback={<span>No access</span>}>
 *     <button>Delete</button>
 *   </PermissionGate>
 */
export function PermissionGate({
  permission,
  permissions,
  requireAll = false,
  fallback = null,
  children,
}: {
  /** Single permission code required */
  permission?: string;
  /** Multiple permission codes — user needs ANY or ALL based on requireAll */
  permissions?: string[];
  /** If true, user must have ALL listed permissions. Default: false (ANY suffices) */
  requireAll?: boolean;
  /** What to render if not authorized */
  fallback?: ReactNode;
  children: ReactNode;
}) {
  const { hasPermission, hasAnyPermission, hasAllPermissions } = useAuth();

  let authorized = false;

  if (permission) {
    authorized = hasPermission(permission);
  } else if (permissions) {
    authorized = requireAll ? hasAllPermissions(permissions) : hasAnyPermission(permissions);
  }

  return authorized ? <>{children}</> : <>{fallback}</>;
}

/**
 * Hook: check a single permission.
 * SuperAdmin always returns true.
 */
export function useHasPermission() {
  const { hasPermission } = useAuth();
  return hasPermission;
}

/**
 * Hook: check if user has ANY of the given permissions.
 */
export function useHasAnyPermission() {
  const { hasAnyPermission } = useAuth();
  return hasAnyPermission;
}

/**
 * Hook: check if user has ALL of the given permissions.
 */
export function useHasAllPermissions() {
  const { hasAllPermissions } = useAuth();
  return hasAllPermissions;
}
