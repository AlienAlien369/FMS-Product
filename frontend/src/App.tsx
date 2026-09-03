import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import Layout from './components/Layout';
import PermissionRoute from './components/PermissionRoute';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import Companies from './pages/Companies';
import Vehicles from './pages/Vehicles';
import Drivers from './pages/Drivers';
import Localization from './pages/Localization';
import Settings from './pages/Settings';
import Modules from './pages/Modules';
import Roles from './pages/Roles';
import Users from './pages/Users';
import AdminCompanies from './pages/AdminCompanies';
import CompanyDetail from './pages/CompanyDetail';
import Packages from './pages/Packages';
import RoutesPage from './pages/Routes';
import GeofencesPage from './pages/Geofences';
import type { ReactNode } from 'react';

const queryClient = new QueryClient();

function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();
  if (isLoading) return <div className="flex items-center justify-center h-screen"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>
        <Route path="/" element={<PermissionRoute permission="dashboard.view"><Dashboard /></PermissionRoute>} />
        <Route path="/companies" element={<PermissionRoute permission="company.view"><Companies /></PermissionRoute>} />
        <Route path="/vehicles" element={<PermissionRoute permission="vehicle.view"><Vehicles /></PermissionRoute>} />
        <Route path="/drivers" element={<PermissionRoute permission="driver.view"><Drivers /></PermissionRoute>} />
        <Route path="/geofences" element={<PermissionRoute permission="geofence.view"><GeofencesPage /></PermissionRoute>} />
        <Route path="/admin/companies" element={<PermissionRoute permission="company.view"><AdminCompanies /></PermissionRoute>} />
        <Route path="/admin/companies/:id" element={<PermissionRoute permission="company.view"><CompanyDetail /></PermissionRoute>} />
        <Route path="/users" element={<PermissionRoute permission="user.view"><Users /></PermissionRoute>} />
        <Route path="/roles" element={<PermissionRoute permission="role.view"><Roles /></PermissionRoute>} />
        <Route path="/packages" element={<PermissionRoute permission="package.view"><Packages /></PermissionRoute>} />
        <Route path="/modules" element={<PermissionRoute permission="configuration.view"><Modules /></PermissionRoute>} />
        <Route path="/routes" element={<PermissionRoute permission="route.view"><RoutesPage /></PermissionRoute>} />
        <Route path="/localization" element={<PermissionRoute permission="configuration.view"><Localization /></PermissionRoute>} />
        <Route path="/settings" element={<PermissionRoute permission="configuration.edit"><Settings /></PermissionRoute>} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  );
}
