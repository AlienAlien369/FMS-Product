import axios from 'axios';

const PRODUCTION_API = 'https://fms-product-api.onrender.com';
const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL
    ? `${import.meta.env.VITE_API_URL}/api/v1`
    : window.location.hostname === 'localhost'
      ? '/api/v1'
      : `${PRODUCTION_API}/api/v1`,
  headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  // Company scope selector: stateless header on every call. The backend treats it
  // as intent only — it intersects it with the user's permitted-company set.
  // sessionStorage keeps the JSON-array form; the header must be bare
  // comma-separated ids (or ALL), so normalize here.
  const scope = sessionStorage.getItem('companyScope');
  if (scope && scope !== '') {
    let headerValue: string | null = null;
    if (scope === 'ALL') headerValue = 'ALL';
    else if (scope.startsWith('[')) {
      try {
        const ids = JSON.parse(scope);
        if (Array.isArray(ids) && ids.length > 0) headerValue = ids.join(',');
      } catch { headerValue = null; }
    } else if (scope.length > 0) headerValue = scope; // already comma-joined
    if (headerValue) config.headers['X-Company-Scope'] = headerValue;
  }
  return config;
});

api.interceptors.response.use(
  (res) => res,
  (err) => {
    // Don't bounce the login request itself to /login — that reload wipes the
    // error message before the form can show it.
    const isLoginRequest = err.config?.url?.includes('/auth/login');
    if (err.response?.status === 401 && !isLoginRequest) {
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
  deviceCount?: number;
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

export interface DeviceSim {
  id: string;
  deviceId: string;
  iccid?: string;
  phoneNumber?: string;
  carrier?: string;
  status: number;
  isPrimary: boolean;
  activatedAt?: string;
}

export interface DeviceVendor {
  id: string;
  code: string;
  name: string;
  description?: string;
  adapterVersion?: string;
  protocolType: number;
  payloadFormat?: string;
  listenerConfig?: string;
  capabilities?: string;
  status: number;
  deviceCount?: number;
}

export interface Device {
  id: string;
  companyId: string;
  vendorId?: string;
  vendorCode?: string;
  vendorName?: string;
  deviceType: number;
  deviceTypeOverride?: string;
  identityType: number;
  identityValue: string;
  model?: string;
  firmwareVersion?: string;
  status: number;
  installDate?: string;
  activatedAt?: string;
  lastSeenAt?: string;
  createdAt: string;
  sims: DeviceSim[];
  currentVehicleId?: string;
  currentVehicleRegistration?: string;
}

export interface VehicleDeviceAssignment {
  id: string;
  vehicleId: string;
  deviceId: string;
  role: number;
  roleName: string;
  assignedFrom: string;
  assignedTo?: string;
  unassignReason?: string;
  vendorCode?: string;
  vendorName?: string;
  deviceType: number;
  deviceTypeOverride?: string;
  identityType: number;
  identityValue: string;
  model?: string;
  deviceStatus: number;
  sims: DeviceSim[];
}

export const DEVICE_TYPE_LABELS: Record<number, string> = {
  0: 'GPS Tracker', 1: 'Dashcam', 2: 'ADAS', 3: 'Fuel Sensor', 4: 'Temperature Sensor', 5: 'Dual Camera', 99: 'Other',
};

export const DEVICE_IDENTITY_LABELS: Record<number, string> = {
  0: 'IMEI', 1: 'Serial', 2: 'MAC', 3: 'Phone Number',
};

export const DEVICE_STATUS_LABELS: Record<number, string> = {
  0: 'Active', 1: 'Inactive', 2: 'Retired', 3: 'Lost', 4: 'Awaiting Vendor',
};

export const DEVICE_ROLE_LABELS: Record<number, string> = {
  0: 'Primary Tracker', 1: 'Secondary Tracker', 2: 'Dashcam', 3: 'ADAS', 4: 'Fuel Sensor', 5: 'Temperature Sensor', 6: 'Spare',
};

export interface AuthState {
  isAuthenticated: boolean;
  user?: AuthResponse['user'];
  token?: string;
}

export default api;
