import axios from 'axios';

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL
    ? `${import.meta.env.VITE_API_URL}/api/v1`
    : '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

api.interceptors.response.use(
  (res) => res,
  (err) => {
    if (err.response?.status === 401) {
      localStorage.removeItem('token');
      localStorage.removeItem('refreshToken');
      localStorage.removeItem('user');
      window.location.href = '/login';
    }
    return Promise.reject(err);
  }
);

export interface ApiResponse<T> {
  success: boolean;
  message?: string;
  code?: string;
  data?: T;
  traceId?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  expiresAt: string;
  user: {
    id: string;
    email: string;
    firstName: string;
    lastName: string;
    companyId: string;
    companyName: string;
    roles: string[];
  };
}

export interface Company {
  id: string;
  name: string;
  slug?: string;
  logoUrl?: string;
  contactEmail?: string;
  contactPhone?: string;
  country?: string;
  defaultLanguage: string;
  defaultTimezone: string;
  defaultCurrency: string;
  status: number;
  createdAt: string;
  userCount?: number;
  vehicleCount?: number;
}

export interface Vehicle {
  id: string;
  registrationNumber: string;
  name?: string;
  vehicleType?: string;
  make?: string;
  model?: string;
  year?: number;
  color?: string;
  fuelType: number;
  fuelTankCapacity?: number;
  fuelCapacityUnit?: string;
  engineNumber?: string;
  chassisNumber?: string;
  vinNumber?: string;
  companyId: string;
  driverId?: string;
  driverName?: string;
  clientId?: string;
  clientName?: string;
  status: number;
  deviceImei?: string;
  deviceType?: string;
  deviceSerialNumber?: string;
  lastLatitude?: number;
  lastLongitude?: number;
  lastSpeed?: number;
  lastHeading?: number;
  lastLocationUpdate?: string;
  ignitionStatus?: boolean;
  odometerReading?: number;
  engineHours?: number;
  createdAt: string;
}

export interface Driver {
  id: string;
  employeeId: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phoneNumber?: string;
  email?: string;
  licenseNumber?: string;
  licenseExpiry?: string;
  companyId: string;
  status: number;
  safetyScore?: number;
  behaviourScore?: number;
}

export interface AuthState {
  isAuthenticated: boolean;
  user?: AuthResponse['user'];
  token?: string;
}

export default api;
