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
    try {
      const token = localStorage.getItem('token');
      const user = localStorage.getItem('user');
      if (token && user) {
        setState({ isAuthenticated: true, token, user: JSON.parse(user) });
        fetchPermissions();
      }
    } catch {
      localStorage.removeItem('token');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('user');
    } finally {
      setIsLoading(false);
    }
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

  const hasPermission = (permission: string) => {
    if (state.user?.roles?.includes('SuperAdmin')) return true;
    return permissions.includes(permission);
  };

  return (
    <AuthContext.Provider value={{ ...state, permissions, login, logout, isLoading, hasPermission }}>
      {children}
    </AuthContext.Provider>
  );
}

export const useAuth = () => useContext(AuthContext);
