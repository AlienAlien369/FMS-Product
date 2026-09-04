import { useEffect, useState, useCallback } from 'react';
import api, { type VehicleDeviceAssignment } from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';
import type { PagedResult } from '../lib/api';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  Eye, X, Truck, MapPin, Wrench, User, Settings, Activity, Shield, Sliders, Radio, CreditCard, Link2,
} from 'lucide-react';
import { VEHICLE_STATUS, FUEL_TYPE } from '../lib/constants';

// ── Types ────────────────────────────────────────────────
interface VehicleDetail {
  id: string; registrationNumber: string; name?: string; vehicleType?: string; make?: string; model?: string;
  year?: number; color?: string; fuelType: number; fuelTankCapacity?: number; fuelCapacityUnit?: string;
  engineNumber?: string; chassisNumber?: string; vinNumber?: string;
  companyId: string; driverId?: string; driverName?: string; clientId?: string; clientName?: string;
  status: number; deviceImei?: string; deviceType?: string; deviceSerialNumber?: string; deviceCount?: number;
  lastLatitude?: number; lastLongitude?: number; lastSpeed?: number; lastHeading?: number;
  lastLocationUpdate?: string; ignitionStatus?: boolean;
  odometerReading?: number; engineHours?: number; customAttributes?: string; createdAt: string;
}

interface DriverOpt { id: string; fullName: string; }
interface ClientOpt { id: string; name: string; }
interface VehicleStats { total: number; active: number; inactive: number; maintenance: number; retired: number; stolen: number; withDriver: number; withDevice: number; unassigned: number; }

// ── Constants ────────────────────────────────────────────
const STATUS_MAP = VEHICLE_STATUS;
const FUEL_MAP = FUEL_TYPE;
const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

type SortField = 'registrationNumber' | 'make' | 'status' | 'name' | 'year';
interface SortState { field: SortField; desc: boolean; }

const STATUS_FILTERS: { key: string; label: string; value?: number; color: string; statKey?: string }[] = [
  { key: 'all', label: 'All', color: 'bg-blue-100 text-blue-700', statKey: 'total' },
  { key: '0', label: 'Active', value: 0, color: 'bg-green-100 text-green-700', statKey: 'active' },
  { key: '2', label: 'Maintenance', value: 2, color: 'bg-yellow-100 text-yellow-700', statKey: 'maintenance' },
  { key: '1', label: 'Inactive', value: 1, color: 'bg-gray-100 text-gray-700', statKey: 'inactive' },
  { key: '3', label: 'Retired', value: 3, color: 'bg-red-100 text-red-700', statKey: 'retired' },
  { key: '4', label: 'Stolen', value: 4, color: 'bg-red-100 text-red-800', statKey: 'stolen' },
];

// ── Main Component ───────────────────────────────────────
export default function Vehicles() {
  const { can } = usePermissions();
  const canCreate = can('vehicle.create');
  const canEdit = can('vehicle.update');
  const canDelete = can('vehicle.delete');
  const canExport = can('vehicle.export');

  const [data, setData] = useState<PagedResult<VehicleDetail> | null>(null);
  const [stats, setStats] = useState<VehicleStats | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('all');
  const [sort, setSort] = useState<SortState>({ field: 'registrationNumber', desc: false });
  const [modal, setModal] = useState<{ open: boolean; edit?: VehicleDetail; view?: VehicleDetail }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<VehicleDetail | null>(null);
  const [drivers, setDrivers] = useState<DriverOpt[]>([]);
  const [clients, setClients] = useState<ClientOpt[]>([]);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search, sortBy: sort.field, sortDescending: String(sort.desc) });
      if (statusFilter !== 'all') params.set('status', statusFilter);
      const res = await api.get(`/vehicles?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, sort, statusFilter]);

  const fetchStats = useCallback(async () => {
    try { const res = await api.get('/vehicles/stats'); setStats(res.data.data); } catch { /* ignore */ }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);
  useEffect(() => { fetchStats(); }, [fetchStats]);

  useEffect(() => {
    if (modal.open) {
      api.get('/tenant/drivers').then(r => setDrivers(r.data.data || [])).catch(() => {});
      api.get('/tenant/clients').then(r => setClients(r.data.data || [])).catch(() => {});
    }
  }, [modal.open]);

  const handleDelete = async (id: string) => {
    try { await api.delete(`/vehicles/${id}`); setDeleteConfirm(null); fetchData(); fetchStats(); }
    catch (e: any) { alert(e.response?.data?.message || 'Failed to delete vehicle'); }
  };

  const handleSort = (field: SortField) => {
    setSort(s => s.field === field ? { field, desc: !s.desc } : { field, desc: false });
    setPage(1);
  };

  const SortIcon = ({ field }: { field: SortField }) => {
    if (sort.field !== field) return <ChevronDown className="w-3 h-3 text-gray-300" />;
    return sort.desc ? <ChevronDown className="w-3 h-3 text-blue-600" /> : <ChevronUp className="w-3 h-3 text-blue-600" />;
  };

  const onSaved = () => { setModal({ open: false }); fetchData(); fetchStats(); };

  // ── Stats Cards ──────────────────────────────────────
  const StatCard = ({ label, value, icon: Icon, color }: { label: string; value: number; icon: any; color: string }) => (
    <div className="bg-white rounded-xl border border-gray-200 p-4 flex items-center gap-3">
      <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${color}`}>
        <Icon className="w-5 h-5" />
      </div>
      <div>
        <div className="text-xl font-bold text-gray-900">{value}</div>
        <div className="text-xs text-gray-500">{label}</div>
      </div>
    </div>
  );

  return (
    <div className="space-y-4">
      {/* Stats Row */}
      {stats && (
        <div className="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-5 gap-3">
          <StatCard label="Total Vehicles" value={stats.total} icon={Truck} color="bg-blue-100 text-blue-600" />
          <StatCard label="Active" value={stats.active} icon={Activity} color="bg-green-100 text-green-600" />
          <StatCard label="In Maintenance" value={stats.maintenance} icon={Wrench} color="bg-yellow-100 text-yellow-600" />
          <StatCard label="Unassigned" value={stats.unassigned} icon={User} color="bg-orange-100 text-orange-600" />
          <StatCard label="With Device" value={stats.withDevice} icon={MapPin} color="bg-purple-100 text-purple-600" />
        </div>
      )}

      {/* Status Filter Tabs */}
      <div className="flex flex-wrap gap-2">
        {STATUS_FILTERS.map(f => {
          const count = f.statKey && stats ? (stats as any)[f.statKey] ?? 0 : 0;
          return (
            <button key={f.key} onClick={() => { setStatusFilter(f.key); setPage(1); }}
              className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${statusFilter === f.key ? f.color : 'bg-gray-100 text-gray-600 hover:bg-gray-200'}`}>
              {f.label} <span className="ml-1 opacity-70">{count}</span>
            </button>
          );
        })}
      </div>

      {/* Search + Add */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search by reg, name, make, model, type..." />
        </div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4" /> Add Vehicle
          </button>
        )}
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th onClick={() => handleSort('registrationNumber')} className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-100 select-none">
                  <span className="flex items-center gap-1">Vehicle <SortIcon field="registrationNumber" /></span>
                </th>
                <th onClick={() => handleSort('make')} className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-100 select-none">
                  <span className="flex items-center gap-1">Make / Model <SortIcon field="make" /></span>
                </th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Client</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Fuel</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Driver</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Device</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Tracking</th>
                <th onClick={() => handleSort('status')} className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-100 select-none">
                  <span className="flex items-center gap-1">Status <SortIcon field="status" /></span>
                </th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">Loading...</td></tr>
              ) : data?.items?.length === 0 ? (
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">No vehicles found</td></tr>
              ) : (
                data?.items?.map(v => (
                  <tr key={v.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-9 h-9 bg-blue-100 rounded-lg flex items-center justify-center text-blue-700">
                          <Truck className="w-4 h-4" />
                        </div>
                        <div>
                          <div className="text-sm font-medium text-gray-900">{v.registrationNumber}</div>
                          <div className="text-xs text-gray-400">{[v.make, v.model].filter(Boolean).join(' ') || v.name || '\u2014'}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="text-sm text-gray-600">{v.make || '\u2014'}</div>
                      <div className="text-xs text-gray-400">{[v.model, v.year].filter(Boolean).join(' / ') || '\u2014'}</div>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">{v.clientName || <span className="text-gray-300">None</span>}</td>
                    <td className="px-4 py-3">
                      <span className="text-sm text-gray-600">{FUEL_MAP[v.fuelType] || 'Unknown'}</span>
                      {v.fuelTankCapacity && <div className="text-xs text-gray-400">{v.fuelTankCapacity} {v.fuelCapacityUnit || 'L'}</div>}
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">{v.driverName || <span className="text-gray-300">Unassigned</span>}</td>
                    <td className="px-4 py-3">
                      {(v.deviceCount ?? (v.deviceImei ? 1 : 0)) > 0 ? (
                        <button onClick={() => setModal({ open: false, view: v })}
                          className="inline-flex items-center gap-1.5 text-xs font-medium text-blue-600 hover:text-blue-700" title="View devices">
                          <Link2 className="w-3.5 h-3.5" /> {v.deviceCount ?? (v.deviceImei ? 1 : 0)} device{(v.deviceCount ?? 1) > 1 ? 's' : ''}
                        </button>
                      ) : <span className="text-xs text-gray-300">No device</span>}
                    </td>
                    <td className="px-4 py-3">
                      {v.lastLatitude ? (
                        <div className="text-xs text-gray-500">
                          <div className="flex items-center gap-1">
                            {v.lastSpeed?.toFixed(0)} km/h
                            {v.ignitionStatus !== null && (v.ignitionStatus ? <span className="text-green-500">●</span> : <span className="text-gray-300">●</span>)}
                          </div>
                          <div className="text-gray-400">{v.lastLocationUpdate ? new Date(v.lastLocationUpdate).toLocaleTimeString() : '\u2014'}</div>
                        </div>
                      ) : <span className="text-xs text-gray-300">No data</span>}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_MAP[v.status]?.color || 'bg-gray-100 text-gray-700'}`}>
                        {STATUS_MAP[v.status]?.label || 'Unknown'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button onClick={() => setModal({ open: false, view: v })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="View Details"><Eye className="w-4 h-4 text-gray-500" /></button>
                        {canEdit && (
                          <button onClick={() => setModal({ open: true, edit: v })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit"><Edit className="w-4 h-4 text-gray-500" /></button>
                        )}
                        {canDelete && (
                          <button onClick={() => setDeleteConfirm(v)} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete"><Trash2 className="w-4 h-4 text-red-500" /></button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        {data && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-gray-200">
            <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} vehicles</span>
            <div className="flex items-center gap-2">
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Modals */}
      {modal.view && <VehicleViewModal vehicle={modal.view} onClose={() => setModal({ open: false })} />}
      {modal.open && !modal.view && (
        <VehicleFormModal vehicle={modal.edit} drivers={drivers} clients={clients} onClose={() => setModal({ open: false })} onSaved={onSaved} />
      )}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setDeleteConfirm(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Vehicle</h3>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete <strong>{deleteConfirm.registrationNumber}</strong> ({deleteConfirm.name || '\u2014'})?</p>
            <div className="flex justify-end gap-3">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm.id)} className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── View Detail Modal (Tabbed) ──────────────────────────
function VehicleViewModal({ vehicle, onClose }: { vehicle: VehicleDetail; onClose: () => void }) {
  type Tab = 'overview' | 'tracking' | 'assignment' | 'device' | 'audit';
  const [activeTab, setActiveTab] = useState<Tab>('overview');
  const [auditLog, setAuditLog] = useState<any[]>([]);
  const [deviceAssignments, setDeviceAssignments] = useState<VehicleDeviceAssignment[]>([]);

  useEffect(() => {
    if (activeTab === 'audit') {
      api.get(`/vehicles/${vehicle.id}/audit`).then(r => setAuditLog(r.data.data || [])).catch(() => {});
    }
    if (activeTab === 'device') {
      api.get(`/vehicles/${vehicle.id}/devices`).then(r => setDeviceAssignments(r.data.data || [])).catch(() => {});
    }
  }, [activeTab, vehicle.id]);

  const tabs: { key: Tab; label: string; icon: any }[] = [
    { key: 'overview', label: 'Overview', icon: Truck },
    { key: 'tracking', label: 'Tracking', icon: MapPin },
    { key: 'assignment', label: 'Assignment', icon: User },
    { key: 'device', label: 'Device', icon: Wrench },
    { key: 'audit', label: 'Audit Log', icon: Shield },
  ];

  const Field = ({ label, value }: { label: string; value?: string | number | null }) => (
    <div><div className="text-xs text-gray-500">{label}</div><div className="text-sm font-medium text-gray-900">{value || '\u2014'}</div></div>
  );

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-3xl max-h-[85vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 bg-blue-100 rounded-lg flex items-center justify-center"><Truck className="w-5 h-5 text-blue-600" /></div>
            <div>
              <h2 className="text-lg font-semibold text-gray-900">{vehicle.registrationNumber}</h2>
              <p className="text-sm text-gray-500">{[vehicle.make, vehicle.model].filter(Boolean).join(' ') || vehicle.name || 'No details'}</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <span className={`inline-flex px-2.5 py-1 rounded-full text-xs font-medium ${STATUS_MAP[vehicle.status]?.color}`}>{STATUS_MAP[vehicle.status]?.label}</span>
            <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
          </div>
        </div>

        {/* Tabs */}
        <div className="flex border-b border-gray-200 px-6">
          {tabs.map(t => {
            const Icon = t.icon;
            return (
              <button key={t.key} onClick={() => setActiveTab(t.key)}
                className={`flex items-center gap-1.5 px-3 py-2.5 text-xs font-medium border-b-2 transition-colors ${activeTab === t.key ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
                <Icon className="w-3.5 h-3.5" /> {t.label}
              </button>
            );
          })}
        </div>

        {/* Tab Content */}
        <div className="flex-1 overflow-y-auto px-6 py-5">
          {activeTab === 'overview' && (
            <div className="space-y-5">
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Truck className="w-4 h-4" /> Vehicle Information</div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                  <Field label="Registration" value={vehicle.registrationNumber} />
                  <Field label="Name" value={vehicle.name} />
                  <Field label="Type" value={vehicle.vehicleType} />
                  <Field label="Make" value={vehicle.make} />
                  <Field label="Model" value={vehicle.model} />
                  <Field label="Year" value={vehicle.year} />
                  <Field label="Color" value={vehicle.color} />
                  <Field label="VIN" value={vehicle.vinNumber} />
                  <Field label="Created" value={vehicle.createdAt ? new Date(vehicle.createdAt).toLocaleDateString() : '\u2014'} />
                </div>
              </div>
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Settings className="w-4 h-4" /> Engine & Fuel</div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                  <Field label="Fuel Type" value={FUEL_MAP[vehicle.fuelType]} />
                  <Field label="Tank Capacity" value={vehicle.fuelTankCapacity ? `${vehicle.fuelTankCapacity} ${vehicle.fuelCapacityUnit || 'L'}` : null} />
                  <Field label="Engine Number" value={vehicle.engineNumber} />
                  <Field label="Chassis Number" value={vehicle.chassisNumber} />
                  <Field label="Odometer" value={vehicle.odometerReading ? `${vehicle.odometerReading.toLocaleString()} km` : null} />
                  <Field label="Engine Hours" value={vehicle.engineHours} />
                </div>
              </div>
            </div>
          )}

          {activeTab === 'tracking' && (
            <div className="space-y-5">
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><MapPin className="w-4 h-4" /> Last Known Location</div>
                {vehicle.lastLatitude ? (
                  <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                    <Field label="Latitude" value={vehicle.lastLatitude?.toFixed(6)} />
                    <Field label="Longitude" value={vehicle.lastLongitude?.toFixed(6)} />
                    <Field label="Speed" value={vehicle.lastSpeed ? `${vehicle.lastSpeed.toFixed(1)} km/h` : null} />
                    <Field label="Heading" value={vehicle.lastHeading ? `${vehicle.lastHeading.toFixed(0)}\u00b0` : null} />
                    <Field label="Ignition" value={vehicle.ignitionStatus !== null ? (vehicle.ignitionStatus ? 'ON' : 'OFF') : '\u2014'} />
                    <Field label="Last Update" value={vehicle.lastLocationUpdate ? new Date(vehicle.lastLocationUpdate).toLocaleString() : '\u2014'} />
                  </div>
                ) : (
                  <div className="text-sm text-gray-400 py-4">No tracking data available. Ensure a GPS device is installed and reporting.</div>
                )}
              </div>
              {vehicle.lastLatitude && (
                <div className="rounded-lg overflow-hidden border border-gray-200 h-48 bg-gray-100 flex items-center justify-center text-gray-400 text-sm">
                  Map view coming soon
                </div>
              )}
            </div>
          )}

          {activeTab === 'assignment' && (
            <div className="space-y-5">
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><User className="w-4 h-4" /> Driver</div>
                {vehicle.driverName ? (
                  <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                    <Field label="Driver Name" value={vehicle.driverName} />
                    <Field label="Driver ID" value={vehicle.driverId?.slice(0, 8)} />
                  </div>
                ) : (
                  <div className="text-sm text-gray-400 py-2">No driver assigned</div>
                )}
              </div>
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Sliders className="w-4 h-4" /> Client</div>
                {vehicle.clientName ? (
                  <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                    <Field label="Client Name" value={vehicle.clientName} />
                    <Field label="Client ID" value={vehicle.clientId?.slice(0, 8)} />
                  </div>
                ) : (
                  <div className="text-sm text-gray-400 py-2">No client assigned</div>
                )}
              </div>
            </div>
          )}

          {activeTab === 'device' && (
            <div className="space-y-3">
              <div className="flex items-center gap-2 text-gray-900 font-semibold"><Radio className="w-4 h-4" /> Devices ({deviceAssignments.length})</div>
              {deviceAssignments.length === 0 ? (
                <div className="text-sm text-gray-400 py-2">No devices assigned to this vehicle.</div>
              ) : deviceAssignments.map(a => (
                <div key={a.id} className="flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg px-4 py-3">
                  <div>
                    <div className="text-sm font-medium text-gray-900 flex items-center gap-2">
                      <Radio className="w-4 h-4 text-blue-500" /> {a.identityValue}
                      <span className="px-1.5 py-0.5 bg-blue-100 text-blue-700 text-xs rounded-full">{a.roleName}</span>
                    </div>
                    <div className="text-xs text-gray-500 mt-0.5">
                      {a.vendorName || 'No vendor'} · {['GPS Tracker', 'Dashcam', 'ADAS', 'Fuel Sensor', 'Temperature Sensor', 'Dual Camera', '', 'Other'][a.deviceType] || 'Other'}
                      {a.sims.length > 0 && <> · <CreditCard className="inline w-3 h-3" /> {a.sims.length} SIM{a.sims.length > 1 ? 's' : ''}</>}
                    </div>
                  </div>
                  <span className={`text-xs px-2 py-0.5 rounded-full ${a.deviceStatus === 0 ? 'bg-green-100 text-green-700' : a.deviceStatus === 4 ? 'bg-amber-100 text-amber-700' : 'bg-gray-100 text-gray-600'}`}>
                    {a.deviceStatus === 0 ? 'Active' : a.deviceStatus === 4 ? 'Awaiting Vendor' : 'Inactive'}
                  </span>
                </div>
              ))}
            </div>
          )}

          {activeTab === 'audit' && (
            <div className="space-y-3">
              <div className="flex items-center gap-2 text-gray-900 font-semibold"><Shield className="w-4 h-4" /> Audit History</div>
              {auditLog.length === 0 ? (
                <div className="text-sm text-gray-400 py-4">No audit records found</div>
              ) : (
                <div className="space-y-2">
                  {auditLog.map((entry: any) => (
                    <div key={entry.id} className="flex items-start gap-3 p-3 bg-gray-50 rounded-lg">
                      <div className="w-8 h-8 bg-gray-200 rounded-full flex items-center justify-center text-xs font-medium text-gray-600">
                        {entry.userName?.[0] || '?'}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="text-sm font-medium text-gray-900">{entry.userName || 'System'}</div>
                        <div className="text-xs text-gray-500">{entry.action === 1 ? 'Created' : entry.action === 2 ? 'Updated' : entry.action === 3 ? 'Deleted' : 'Action'} &middot; {new Date(entry.createdAt).toLocaleString()}</div>
                        {entry.oldValues && <div className="text-xs text-gray-400 mt-1 truncate">Old: {entry.oldValues}</div>}
                        {entry.newValues && <div className="text-xs text-gray-400 mt-1 truncate">New: {entry.newValues}</div>}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Create/Edit Modal ────────────────────────────────────
function VehicleFormModal({ vehicle, drivers, clients, onClose, onSaved }: { vehicle?: VehicleDetail; drivers: DriverOpt[]; clients: ClientOpt[]; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!vehicle?.id;
  const [form, setForm] = useState({
    registrationNumber: vehicle?.registrationNumber || '', name: vehicle?.name || '', vehicleType: vehicle?.vehicleType || '',
    make: vehicle?.make || '', model: vehicle?.model || '', year: vehicle?.year?.toString() || '',
    color: vehicle?.color || '', fuelType: vehicle?.fuelType ?? 1, fuelTankCapacity: vehicle?.fuelTankCapacity?.toString() || '',
    fuelCapacityUnit: vehicle?.fuelCapacityUnit || 'liters',
    engineNumber: vehicle?.engineNumber || '', chassisNumber: vehicle?.chassisNumber || '', vinNumber: vehicle?.vinNumber || '',
    driverId: vehicle?.driverId || '', clientId: vehicle?.clientId || '',
    status: vehicle?.status ?? 0, odometerReading: vehicle?.odometerReading?.toString() || '', engineHours: vehicle?.engineHours?.toString() || '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  // ── Devices (multi-device model; legacy single-device fields are no longer edited here) ──
  const [assignments, setAssignments] = useState<VehicleDeviceAssignment[]>([]);
  const [availableDevices, setAvailableDevices] = useState<any[]>([]);
  const [selectedDeviceId, setSelectedDeviceId] = useState('');
  const [selectedRole, setSelectedRole] = useState(0);
  const [devicesError, setDevicesError] = useState('');

  const fetchAssignments = useCallback(async () => {
    if (!isEdit) return;
    try {
      const [assigned, all] = await Promise.all([
        api.get(`/vehicles/${vehicle!.id}/devices`),
        api.get('/devices?pageSize=100'),
      ]);
      const assignmentsData: VehicleDeviceAssignment[] = assigned.data.data || [];
      setAssignments(assignmentsData);
      const assignedIds = new Set(assignmentsData.map(a => a.deviceId));
      setAvailableDevices((all.data.data?.items || []).filter((d: any) => !assignedIds.has(d.id) && d.status === 0));
    } catch { /* ignore */ }
  }, [isEdit, vehicle]);

  useEffect(() => { fetchAssignments(); }, [fetchAssignments]);

  const assignDevice = async () => {
    setDevicesError('');
    if (!selectedDeviceId) { setDevicesError('Select a device to assign'); return; }
    try {
      await api.post(`/vehicles/${vehicle!.id}/devices`, { deviceId: selectedDeviceId, role: selectedRole });
      setSelectedDeviceId('');
      fetchAssignments();
    } catch (e: any) { setDevicesError(e.response?.data?.message || 'Failed to assign device'); }
  };

  const unassignDevice = async (assignmentId: string) => {
    try {
      await api.delete(`/vehicles/${vehicle!.id}/devices/${assignmentId}`);
      fetchAssignments();
    } catch (e: any) { setDevicesError(e.response?.data?.message || 'Failed to unassign device'); }
  };

  const handleSubmit = async () => {
    if (!form.registrationNumber.trim()) { setError('Registration number required'); return; }
    setSaving(true); setError('');
    const payload: any = {
      registrationNumber: form.registrationNumber, name: form.name || null, vehicleType: form.vehicleType || null,
      make: form.make || null, model: form.model || null, year: form.year ? parseInt(form.year) : null,
      color: form.color || null, fuelType: form.fuelType,
      fuelTankCapacity: form.fuelTankCapacity ? parseFloat(form.fuelTankCapacity) : null,
      fuelCapacityUnit: form.fuelCapacityUnit || null,
      engineNumber: form.engineNumber || null, chassisNumber: form.chassisNumber || null, vinNumber: form.vinNumber || null,
      driverId: form.driverId || null, clientId: form.clientId || null,
      status: form.status,
      odometerReading: form.odometerReading ? parseInt(form.odometerReading) : null,
      engineHours: form.engineHours ? parseInt(form.engineHours) : null,
    };
    try {
      if (isEdit) { await api.put(`/vehicles/${vehicle!.id}`, payload); }
      else { await api.post('/vehicles', payload); }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed to save'); }
    setSaving(false);
  };

  const Section = ({ icon: Icon, title, children }: { icon: any; title: string; children: React.ReactNode }) => (
    <div className="space-y-3">
      <div className="flex items-center gap-2 text-gray-900 font-semibold"><Icon className="w-4 h-4" /> {title}</div>
      {children}
    </div>
  );

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Vehicle' : 'Add Vehicle'}</h2>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}

          <Section icon={Truck} title="Vehicle Information">
            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Registration Number *</label><input className={INPUT} value={form.registrationNumber} onChange={e => setForm({ ...form, registrationNumber: e.target.value })} placeholder="e.g. MH-12-AB-1234" /></div>
              <div><label className={LABEL}>Name</label><input className={INPUT} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="Friendly name" /></div>
              <div><label className={LABEL}>Vehicle Type</label><select className={INPUT} value={form.vehicleType} onChange={e => setForm({ ...form, vehicleType: e.target.value })}><option value="">Select</option><option>Truck</option><option>Mini Truck</option><option>SUV</option><option>Heavy Truck</option><option>Bus</option><option>Car</option><option>Van</option><option>Pickup</option><option>Electric Auto</option><option>Other</option></select></div>
              <div><label className={LABEL}>Make</label><input className={INPUT} value={form.make} onChange={e => setForm({ ...form, make: e.target.value })} placeholder="e.g. Toyota" /></div>
              <div><label className={LABEL}>Model</label><input className={INPUT} value={form.model} onChange={e => setForm({ ...form, model: e.target.value })} placeholder="e.g. Hilux" /></div>
              <div><label className={LABEL}>Year</label><input className={INPUT} type="number" min="1900" max="2100" value={form.year} onChange={e => setForm({ ...form, year: e.target.value })} placeholder="e.g. 2023" /></div>
              <div><label className={LABEL}>Color</label><input className={INPUT} value={form.color} onChange={e => setForm({ ...form, color: e.target.value })} placeholder="e.g. White" /></div>
              <div><label className={LABEL}>VIN Number</label><input className={INPUT} value={form.vinNumber} onChange={e => setForm({ ...form, vinNumber: e.target.value })} /></div>
            </div>
          </Section>

          <Section icon={Settings} title="Engine & Fuel">
            <div className="grid grid-cols-3 gap-4">
              <div><label className={LABEL}>Fuel Type</label><select className={INPUT} value={form.fuelType} onChange={e => setForm({ ...form, fuelType: parseInt(e.target.value) })}>{Object.entries(FUEL_MAP).map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></div>
              <div><label className={LABEL}>Tank Capacity</label><input className={INPUT} type="number" step="0.1" min="0" value={form.fuelTankCapacity} onChange={e => setForm({ ...form, fuelTankCapacity: e.target.value })} placeholder="Liters" /></div>
              <div><label className={LABEL}>Capacity Unit</label><select className={INPUT} value={form.fuelCapacityUnit} onChange={e => setForm({ ...form, fuelCapacityUnit: e.target.value })}><option value="liters">Liters</option><option value="gallons">Gallons</option></select></div>
              <div><label className={LABEL}>Engine Number</label><input className={INPUT} value={form.engineNumber} onChange={e => setForm({ ...form, engineNumber: e.target.value })} /></div>
              <div><label className={LABEL}>Chassis Number</label><input className={INPUT} value={form.chassisNumber} onChange={e => setForm({ ...form, chassisNumber: e.target.value })} /></div>
              <div><label className={LABEL}>Odometer (km)</label><input className={INPUT} type="number" min="0" value={form.odometerReading} onChange={e => setForm({ ...form, odometerReading: e.target.value })} /></div>
              <div><label className={LABEL}>Engine Hours</label><input className={INPUT} type="number" min="0" value={form.engineHours} onChange={e => setForm({ ...form, engineHours: e.target.value })} /></div>
            </div>
          </Section>

          <Section icon={User} title="Assignment">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className={LABEL}>Driver</label>
                <select className={INPUT} value={form.driverId} onChange={e => setForm({ ...form, driverId: e.target.value })}>
                  <option value="">Unassigned</option>
                  {drivers.map(d => <option key={d.id} value={d.id}>{d.fullName}</option>)}
                </select>
              </div>
              <div>
                <label className={LABEL}>Client</label>
                <select className={INPUT} value={form.clientId} onChange={e => setForm({ ...form, clientId: e.target.value })}>
                  <option value="">None</option>
                  {clients.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
              </div>
            </div>
          </Section>

          <Section icon={Radio} title="Devices">
            {!isEdit ? (
              <div className="text-sm text-gray-400 bg-gray-50 border border-gray-200 rounded-lg px-4 py-3">
                Save the vehicle first — you can then attach tracking devices (GPS tracker, dashcam, fuel sensor…) with per-role assignments.
              </div>
            ) : (
              <div className="space-y-3">
                {devicesError && <div className="p-2 bg-red-50 border border-red-200 rounded-lg text-xs text-red-700">{devicesError}</div>}
                {assignments.length === 0 ? (
                  <div className="text-sm text-gray-400">No devices assigned yet.</div>
                ) : assignments.map(a => (
                  <div key={a.id} className="flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg px-3 py-2">
                    <div className="flex items-center gap-2 min-w-0">
                      <Radio className="w-4 h-4 text-blue-500 shrink-0" />
                      <div className="min-w-0">
                        <div className="text-sm font-medium text-gray-800 truncate">{a.identityValue}
                          <span className="ml-2 px-1.5 py-0.5 bg-blue-100 text-blue-700 text-xs rounded-full">{a.roleName}</span>
                        </div>
                        <div className="text-xs text-gray-500 truncate">{a.vendorName || 'No vendor'} · {['GPS Tracker', 'Dashcam', 'ADAS', 'Fuel Sensor', 'Temperature Sensor', 'Dual Camera', '', 'Other'][a.deviceType] || 'Other'}{a.sims.length > 0 ? ` · ${a.sims.length} SIM${a.sims.length > 1 ? 's' : ''}` : ''}</div>
                      </div>
                    </div>
                    <button onClick={() => unassignDevice(a.id)} className="text-xs text-red-500 hover:text-red-600 font-medium shrink-0 ml-2">Remove</button>
                  </div>
                ))}
                {availableDevices.length > 0 && (
                  <div className="flex items-end gap-2 border-t border-gray-100 pt-3">
                    <div className="flex-1">
                      <label className={LABEL}>Add Device</label>
                      <select className={INPUT} value={selectedDeviceId} onChange={e => setSelectedDeviceId(e.target.value)}>
                        <option value="">Select unassigned device...</option>
                        {availableDevices.map((d: any) => (
                          <option key={d.id} value={d.id}>{d.identityValue} — {d.vendorName || 'No vendor'}{d.model ? ` (${d.model})` : ''}</option>
                        ))}
                      </select>
                    </div>
                    <div>
                      <label className={LABEL}>Role</label>
                      <select className={INPUT} value={selectedRole} onChange={e => setSelectedRole(Number(e.target.value))}>
                        {['Primary Tracker', 'Secondary Tracker', 'Dashcam', 'ADAS', 'Fuel Sensor', 'Temperature Sensor', 'Spare'].map((r, i) => <option key={i} value={i}>{r}</option>)}
                      </select>
                    </div>
                    <button onClick={assignDevice} className="px-3 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm">Assign</button>
                  </div>
                )}
                {availableDevices.length === 0 && assignments.length > 0 && (
                  <div className="text-xs text-gray-400">All active devices are already assigned to a vehicle.</div>
                )}
              </div>
            )}
          </Section>

          <div>
            <label className={LABEL}>Status</label>
            <select className={INPUT + ' w-48'} value={form.status} onChange={e => setForm({ ...form, status: parseInt(e.target.value) })}>
              <option value={0}>Active</option>
              <option value={1}>Inactive</option>
              <option value={2}>In Maintenance</option>
              <option value={3}>Retired</option>
              <option value={4}>Stolen</option>
            </select>
          </div>
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">{saving ? 'Saving...' : isEdit ? 'Update' : 'Create'}</button>
        </div>
      </div>
    </div>
  );
}
