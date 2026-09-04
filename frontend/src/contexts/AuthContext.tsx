import { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import api from '../lib/api';
import type { AuthResponse, AuthState } from '../lib/api';

interface AuthContextType extends AuthState {
  permissions: string[];
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  isLoading: boolean;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: string[]) => boolean;
  hasAllPermissions: (permissions: string[]) => boolean;
}

const AuthContext = createContext<AuthContextType>({} as AuthContextType);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ isAuthenticated: false });
  const [permissions, setPermissions] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  const fetchPermissions = async () => {
    try {
      const res = await api.get('/auth/permissions');
      setPermissions(res.data.data?.permissions || []);
    } catch {
      setPermissions([]);
    }
  };

  useEffect(() => {
    const boot = async () => {
      try {
        const token = localStorage.getItem('token');
        const user = localStorage.getItem('user');
        if (token && user) {
          setState({ isAuthenticated: true, token, user: JSON.parse(user) });
          // Permissions load asynchronously; keep isLoading true until they
          // resolve so route guards never default-deny a direct URL load.
          await fetchPermissions();
        }
      } catch {
        localStorage.removeItem('token');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('user');
      } finally {
        setIsLoading(false);
      }
    };
    boot();
  }, []);

  const login = async (email: string, password: string) => {
    const res = await api.post<import('../lib/api').ApiResponse<AuthResponse>>('/auth/login', { email, password });
    const data = res.data.data;
    if (!data) throw new Error(res.data.message || 'Login failed');
    localStorage.setItem('token', data.token);
    localStorage.setItem('refreshToken', data.refreshToken);
    localStorage.setItem('user', JSON.stringify(data.user));
    setState({ isAuthenticated: true, token: data.token, user: data.user });
    await fetchPermissions();
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('user');
    setState({ isAuthenticated: false });
    setPermissions([]);
  };

  const isSuperAdmin = state.user?.roles?.includes('SuperAdmin') ?? false;

  const hasPermission = (permission: string) => {
    if (isSuperAdmin) return true;
    return permissions.includes(permission);
  };

  const hasAnyPermission = (perms: string[]) => {
    if (isSuperAdmin) return true;
    return perms.some(p => permissions.includes(p));
  };

  const hasAllPermissions = (perms: string[]) => {
    if (isSuperAdmin) return true;
    return perms.every(p => permissions.includes(p));
  };

  return (
    <AuthContext.Provider value={{ ...state, permissions, login, logout, isLoading, hasPermission, hasAnyPermission, hasAllPermissions }}>
      {children}
    </AuthContext.Provider>
  );
}

export const useAuth = () => useContext(AuthContext);
