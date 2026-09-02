export const VEHICLE_STATUS: Record<number, { label: string; color: string }> = {
  0: { label: 'Active', color: 'bg-green-100 text-green-700' },
  1: { label: 'Inactive', color: 'bg-gray-100 text-gray-700' },
  2: { label: 'In Maintenance', color: 'bg-yellow-100 text-yellow-700' },
  3: { label: 'Retired', color: 'bg-red-100 text-red-700' },
  4: { label: 'Stolen', color: 'bg-red-100 text-red-800' },
};

export const FUEL_TYPE: Record<number, string> = {
  0: 'Petrol', 1: 'Diesel', 2: 'CNG', 3: 'LNG', 4: 'Electric', 5: 'Hybrid', 6: 'Hydrogen', 7: 'Other',
};

export const SUBSCRIPTION_STATUS: Record<number, { label: string; color: string }> = {
  0: { label: 'Active', color: 'bg-green-100 text-green-700' },
  1: { label: 'Trialing', color: 'bg-blue-100 text-blue-700' },
  2: { label: 'Past Due', color: 'bg-yellow-100 text-yellow-700' },
  3: { label: 'Canceled', color: 'bg-red-100 text-red-700' },
  4: { label: 'Expired', color: 'bg-red-100 text-red-800' },
  5: { label: 'Suspended', color: 'bg-orange-100 text-orange-700' },
};
