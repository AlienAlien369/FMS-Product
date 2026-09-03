import { Navigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

/**
 * Route guard: renders children only if user has the required permission.
 * If permission is missing, redirects to "/" (default deny).
 *
 * Usage in App.tsx:
 *   <Route path="/drivers" element={<PermissionRoute permission="driver.view"><Drivers /></PermissionRoute>} />
 */
export default function PermissionRoute({
  permission,
  children,
}: {
  permission?: string;
  children: React.ReactNode;
}) {
  const { hasPermission, isLoading } = useAuth();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  // No permission required → always allow
  if (!permission) return <>{children}</>;

  // SuperAdmin always passes (hasPermission already handles this)
  if (hasPermission(permission)) return <>{children}</>;

  // Default deny → redirect to home
  return <Navigate to="/" replace />;
}
