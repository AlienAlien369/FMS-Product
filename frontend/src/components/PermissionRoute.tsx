import { Navigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

/**
 * Route guard: renders children only if user has the required permission.
 * If permission is missing, redirects to "/" (default deny).
 *
 * Usage in App.tsx:
 *   <Route path="/drivers" element={<PermissionRoute permission="driver.view"><Drivers /></PermissionRoute>} />
 *   <Route path="/packages" element={<PermissionRoute permission="package.view" adminOnly><Packages /></PermissionRoute>} />
 */
export default function PermissionRoute({
  permission,
  adminOnly = false,
  children,
}: {
  permission?: string;
  /** SuperAdmin-only page — blocks non-SuperAdmin even if they hold the permission */
  adminOnly?: boolean;
  children: React.ReactNode;
}) {
  const { hasPermission, isLoading, user } = useAuth();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  // No permission required → always allow
  if (!permission) return <>{children}</>;

  // SuperAdmin bypasses both the permission and the adminOnly check
  if (user?.roles?.includes('SuperAdmin')) return <>{children}</>;

  // SuperAdmin-only pages block everyone else regardless of held permissions
  if (adminOnly) return <Navigate to="/" replace />;

  // Permission gate (hasPermission already returns true for SuperAdmin)
  if (hasPermission(permission)) return <>{children}</>;

  // Default deny → redirect to home
  return <Navigate to="/" replace />;
}