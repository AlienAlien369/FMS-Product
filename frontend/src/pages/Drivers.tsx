import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';
import type { PagedResult } from '../lib/api';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  Eye, X, Users, MapPin, Shield, Activity, Award, FileText, Truck, Mail, Phone,
  CreditCard, Calendar, Building2, Star,
} from 'lucide-react';

// ── Types ────────────────────────────────────────────────
interface DriverDetail {
  id: string; employeeId: string; firstName: string; lastName: string; fullName: string;
  phoneNumber?: string; email?: string; licenseNumber?: string; licenseExpiry?: string;
  licenseCategory?: string; address?: string; city?: string; country?: string;
  profileImageUrl?: string; companyId: string; companyName?: string;
  status: number; safetyScore?: number; behaviourScore?: number;
  assignedVehicleId?: string; assignedVehicleReg?: string;
  tripCount: number; createdAt: string;
}

interface DriverStats {
  total: number; active: number; inactive: number; onTrip: number;
  offDuty: number; suspended: number; avgSafety: number; avgBehaviour: number;
}

// ── Constants ────────────────────────────────────────────
const STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: 'Active', color: 'bg-green-100 text-green-700' },
  1: { label: 'Inactive', color: 'bg-gray-100 text-gray-700' },
  2: { label: 'On Trip', color: 'bg-blue-100 text-blue-700' },
  3: { label: 'Off Duty', color: 'bg-yellow-100 text-yellow-700' },
  4: { label: 'Suspended', color: 'bg-red-100 text-red-700' },
};

const INPUT = 'w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500';
const LABEL = 'block text-sm font-medium text-gray-700 mb-1';

type SortField = 'fullName' | 'employeeId' | 'safetyScore' | 'status';
interface SortState { field: SortField; desc: boolean; }

const STATUS_FILTERS: { key: string; label: string; value?: number; color: string; statKey: string }[] = [
  { key: 'all', label: 'All', color: 'bg-blue-100 text-blue-700', statKey: 'total' },
  { key: '0', label: 'Active', value: 0, color: 'bg-green-100 text-green-700', statKey: 'active' },
  { key: '2', label: 'On Trip', value: 2, color: 'bg-blue-100 text-blue-700', statKey: 'onTrip' },
  { key: '3', label: 'Off Duty', value: 3, color: 'bg-yellow-100 text-yellow-700', statKey: 'offDuty' },
  { key: '1', label: 'Inactive', value: 1, color: 'bg-gray-100 text-gray-700', statKey: 'inactive' },
  { key: '4', label: 'Suspended', value: 4, color: 'bg-red-100 text-red-700', statKey: 'suspended' },
];

// ── Main Component ───────────────────────────────────────
export default function Drivers() {
  const { can } = usePermissions();
  const canCreate = can('driver.create');
  const canEdit = can('driver.edit');
  const canDelete = can('driver.delete');
  const canExport = can('driver.export');

  const [data, setData] = useState<PagedResult<DriverDetail> | null>(null);
  const [stats, setStats] = useState<DriverStats | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('all');
  const [sort, setSort] = useState<SortState>({ field: 'fullName', desc: false });
  const [modal, setModal] = useState<{ open: boolean; edit?: DriverDetail; view?: DriverDetail }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<DriverDetail | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search, sortBy: sort.field === 'fullName' ? 'lastname' : sort.field, sortDescending: String(sort.desc) });
      if (statusFilter !== 'all') params.set('status', statusFilter);
      const res = await api.get(`/drivers?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, sort, statusFilter]);

  const fetchStats = useCallback(async () => {
    try { const res = await api.get('/drivers/stats'); setStats(res.data.data); } catch { /* ignore */ }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);
  useEffect(() => { fetchStats(); }, [fetchStats]);

  const handleDelete = async (id: string) => {
    try { await api.delete(`/drivers/${id}`); setDeleteConfirm(null); fetchData(); fetchStats(); }
    catch (e: any) { alert(e.response?.data?.message || 'Failed to delete driver'); }
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

  const StatCard = ({ label, value, icon: Icon, color }: { label: string; value: number | string; icon: any; color: string }) => (
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
          <StatCard label="Total Drivers" value={stats.total} icon={Users} color="bg-blue-100 text-blue-600" />
          <StatCard label="Active" value={stats.active} icon={Activity} color="bg-green-100 text-green-600" />
          <StatCard label="On Trip" value={stats.onTrip} icon={Truck} color="bg-indigo-100 text-indigo-600" />
          <StatCard label="Avg Safety" value={`${stats.avgSafety}%`} icon={Shield} color="bg-emerald-100 text-emerald-600" />
          <StatCard label="Avg Behaviour" value={`${stats.avgBehaviour}%`} icon={Award} color="bg-purple-100 text-purple-600" />
        </div>
      )}

      {/* Status Filter Tabs */}
      <div className="flex flex-wrap gap-2">
        {STATUS_FILTERS.map(f => {
          const count = stats ? (stats as any)[f.statKey] ?? 0 : 0;
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
            placeholder="Search by name, employee ID..." />
        </div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4" /> Add Driver
          </button>
        )}
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th onClick={() => handleSort('fullName')} className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-100 select-none">
                  <span className="flex items-center gap-1">Driver <SortIcon field="fullName" /></span>
                </th>
                <th onClick={() => handleSort('employeeId')} className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-100 select-none">
                  <span className="flex items-center gap-1">Employee ID <SortIcon field="employeeId" /></span>
                </th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Contact</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">License</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Vehicle</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Trips</th>
                <th onClick={() => handleSort('safetyScore')} className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase cursor-pointer hover:bg-gray-100 select-none">
                  <span className="flex items-center gap-1">Safety <SortIcon field="safetyScore" /></span>
                </th>
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
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">No drivers found</td></tr>
              ) : (
                data?.items?.map(d => (
                  <tr key={d.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-9 h-9 bg-blue-100 rounded-full flex items-center justify-center text-blue-700 text-sm font-medium">
                          {d.firstName?.[0]}{d.lastName?.[0]}
                        </div>
                        <div>
                          <div className="text-sm font-medium text-gray-900">{d.fullName}</div>
                          <div className="text-xs text-gray-400">{d.city || '\u2014'}{d.country ? `, ${d.country}` : ''}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600 font-mono">{d.employeeId}</td>
                    <td className="px-4 py-3">
                      <div className="text-sm text-gray-600">{d.phoneNumber || '\u2014'}</div>
                      <div className="text-xs text-gray-400">{d.email || ''}</div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="text-sm text-gray-600">{d.licenseNumber || '\u2014'}</div>
                      {d.licenseExpiry && (
                        <div className={`text-xs ${new Date(d.licenseExpiry) < new Date() ? 'text-red-500 font-medium' : 'text-gray-400'}`}>
                          Exp: {new Date(d.licenseExpiry).toLocaleDateString()}
                        </div>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {d.assignedVehicleReg ? (
                        <span className="inline-flex items-center gap-1 text-sm text-blue-600">
                          <Truck className="w-3.5 h-3.5" /> {d.assignedVehicleReg}
                        </span>
                      ) : <span className="text-xs text-gray-300">Unassigned</span>}
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">{d.tripCount}</td>
                    <td className="px-4 py-3">
                      {d.safetyScore != null ? (
                        <div className="flex items-center gap-1.5">
                          <div className="w-16 h-1.5 bg-gray-200 rounded-full overflow-hidden">
                            <div className={`h-full rounded-full ${d.safetyScore >= 80 ? 'bg-green-500' : d.safetyScore >= 60 ? 'bg-yellow-500' : 'bg-red-500'}`} style={{ width: `${d.safetyScore}%` }} />
                          </div>
                          <span className="text-xs font-medium text-gray-600">{d.safetyScore}%</span>
                        </div>
                      ) : <span className="text-xs text-gray-300">\u2014</span>}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_MAP[d.status]?.color || 'bg-gray-100 text-gray-700'}`}>
                        {STATUS_MAP[d.status]?.label || 'Unknown'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button onClick={() => setModal({ open: false, view: d })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="View Details"><Eye className="w-4 h-4 text-gray-500" /></button>
                        {canEdit && (
                          <button onClick={() => setModal({ open: true, edit: d })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit"><Edit className="w-4 h-4 text-gray-500" /></button>
                        )}
                        {canDelete && (
                          <button onClick={() => setDeleteConfirm(d)} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete"><Trash2 className="w-4 h-4 text-red-500" /></button>
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
            <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} drivers</span>
            <div className="flex items-center gap-2">
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Modals */}
      {modal.view && <DriverViewModal driver={modal.view} onClose={() => setModal({ open: false })} />}
      {modal.open && !modal.view && (
        <DriverFormModal driver={modal.edit} onClose={() => setModal({ open: false })} onSaved={onSaved} />
      )}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setDeleteConfirm(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Driver</h3>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete <strong>{deleteConfirm.fullName}</strong> ({deleteConfirm.employeeId})?</p>
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
function DriverViewModal({ driver, onClose }: { driver: DriverDetail; onClose: () => void }) {
  type Tab = 'overview' | 'license' | 'scores' | 'assignment' | 'audit';
  const [activeTab, setActiveTab] = useState<Tab>('overview');
  const [auditLog, setAuditLog] = useState<any[]>([]);

  useEffect(() => {
    if (activeTab === 'audit') {
      api.get(`/drivers/${driver.id}/audit`).then(r => setAuditLog(r.data.data || [])).catch(() => {});
    }
  }, [activeTab, driver.id]);

  const tabs: { key: Tab; label: string; icon: any }[] = [
    { key: 'overview', label: 'Overview', icon: Users },
    { key: 'license', label: 'License', icon: CreditCard },
    { key: 'scores', label: 'Scores', icon: Award },
    { key: 'assignment', label: 'Assignment', icon: Truck },
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
            <div className="w-10 h-10 bg-blue-100 rounded-full flex items-center justify-center text-blue-700 text-sm font-medium">
              {driver.firstName?.[0]}{driver.lastName?.[0]}
            </div>
            <div>
              <h2 className="text-lg font-semibold text-gray-900">{driver.fullName}</h2>
              <p className="text-sm text-gray-500">{driver.employeeId} &middot; {driver.companyName || 'Unknown Company'}</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <span className={`inline-flex px-2.5 py-1 rounded-full text-xs font-medium ${STATUS_MAP[driver.status]?.color}`}>{STATUS_MAP[driver.status]?.label}</span>
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
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Users className="w-4 h-4" /> Personal Information</div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                  <Field label="First Name" value={driver.firstName} />
                  <Field label="Last Name" value={driver.lastName} />
                  <Field label="Employee ID" value={driver.employeeId} />
                  <Field label="Phone" value={driver.phoneNumber} />
                  <Field label="Email" value={driver.email} />
                  <Field label="Company" value={driver.companyName} />
                </div>
              </div>
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><MapPin className="w-4 h-4" /> Address</div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                  <Field label="Address" value={driver.address} />
                  <Field label="City" value={driver.city} />
                  <Field label="Country" value={driver.country} />
                </div>
              </div>
            </div>
          )}

          {activeTab === 'license' && (
            <div className="space-y-5">
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><CreditCard className="w-4 h-4" /> License Details</div>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                  <Field label="License Number" value={driver.licenseNumber} />
                  <Field label="Category" value={driver.licenseCategory} />
                  <Field label="Expiry Date" value={driver.licenseExpiry ? new Date(driver.licenseExpiry).toLocaleDateString() : null} />
                </div>
                {driver.licenseExpiry && new Date(driver.licenseExpiry) < new Date() && (
                  <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">
                    License has expired on {new Date(driver.licenseExpiry).toLocaleDateString()}
                  </div>
                )}
                {driver.licenseExpiry && new Date(driver.licenseExpiry) > new Date() && new Date(driver.licenseExpiry) < new Date(Date.now() + 90 * 24 * 60 * 60 * 1000) && (
                  <div className="p-3 bg-amber-50 border border-amber-200 rounded-lg text-sm text-amber-700">
                    License expires within 90 days ({new Date(driver.licenseExpiry).toLocaleDateString()})
                  </div>
                )}
              </div>
            </div>
          )}

          {activeTab === 'scores' && (
            <div className="space-y-5">
              <div className="grid grid-cols-2 gap-4">
                <div className="bg-white rounded-xl border border-gray-200 p-5 text-center">
                  <Shield className="w-8 h-8 mx-auto text-emerald-500 mb-2" />
                  <div className="text-3xl font-bold text-gray-900">{driver.safetyScore != null ? `${driver.safetyScore}%` : '\u2014'}</div>
                  <div className="text-sm text-gray-500 mt-1">Safety Score</div>
                  {driver.safetyScore != null && (
                    <div className="mt-3">
                      <div className="w-full h-2 bg-gray-200 rounded-full overflow-hidden">
                        <div className={`h-full rounded-full ${driver.safetyScore >= 80 ? 'bg-green-500' : driver.safetyScore >= 60 ? 'bg-yellow-500' : 'bg-red-500'}`} style={{ width: `${driver.safetyScore}%` }} />
                      </div>
                    </div>
                  )}
                </div>
                <div className="bg-white rounded-xl border border-gray-200 p-5 text-center">
                  <Star className="w-8 h-8 mx-auto text-purple-500 mb-2" />
                  <div className="text-3xl font-bold text-gray-900">{driver.behaviourScore != null ? `${driver.behaviourScore}%` : '\u2014'}</div>
                  <div className="text-sm text-gray-500 mt-1">Behaviour Score</div>
                  {driver.behaviourScore != null && (
                    <div className="mt-3">
                      <div className="w-full h-2 bg-gray-200 rounded-full overflow-hidden">
                        <div className={`h-full rounded-full ${driver.behaviourScore >= 80 ? 'bg-green-500' : driver.behaviourScore >= 60 ? 'bg-yellow-500' : 'bg-red-500'}`} style={{ width: `${driver.behaviourScore}%` }} />
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}

          {activeTab === 'assignment' && (
            <div className="space-y-5">
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Truck className="w-4 h-4" /> Vehicle Assignment</div>
                {driver.assignedVehicleReg ? (
                  <div className="p-4 bg-blue-50 border border-blue-200 rounded-lg flex items-center gap-3">
                    <Truck className="w-8 h-8 text-blue-600" />
                    <div>
                      <div className="text-sm font-semibold text-blue-900">{driver.assignedVehicleReg}</div>
                      <div className="text-xs text-blue-600">Currently assigned</div>
                    </div>
                  </div>
                ) : (
                  <div className="text-sm text-gray-400 py-2">No vehicle currently assigned</div>
                )}
              </div>
              <div className="space-y-3">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><FileText className="w-4 h-4" /> Trip History</div>
                <div className="text-sm text-gray-600">{driver.tripCount} total trips completed</div>
              </div>
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
function DriverFormModal({ driver, onClose, onSaved }: { driver?: DriverDetail; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!driver?.id;
  const [form, setForm] = useState({
    employeeId: driver?.employeeId || '', firstName: driver?.firstName || '', lastName: driver?.lastName || '',
    phoneNumber: driver?.phoneNumber || '', email: driver?.email || '',
    licenseNumber: driver?.licenseNumber || '', licenseExpiry: driver?.licenseExpiry ? driver.licenseExpiry.split('T')[0] : '',
    licenseCategory: driver?.licenseCategory || '',
    address: driver?.address || '', city: driver?.city || '', country: driver?.country || '',
    status: driver?.status ?? 0,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async () => {
    if (!form.firstName.trim() || !form.lastName.trim()) { setError('First and last name are required'); return; }
    if (!form.employeeId.trim()) { setError('Employee ID is required'); return; }
    setSaving(true); setError('');
    const payload: any = {
      employeeId: form.employeeId, firstName: form.firstName, lastName: form.lastName,
      phoneNumber: form.phoneNumber || null, email: form.email || null,
      licenseNumber: form.licenseNumber || null,
      licenseExpiry: form.licenseExpiry ? new Date(form.licenseExpiry).toISOString() : null,
      licenseCategory: form.licenseCategory || null,
      address: form.address || null, city: form.city || null, country: form.country || null,
    };
    try {
      if (isEdit) { payload.status = form.status; await api.put(`/drivers/${driver!.id}`, payload); }
      else { await api.post('/drivers', payload); }
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
          <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Driver' : 'Add Driver'}</h2>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}

          <Section icon={Users} title="Personal Information">
            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Employee ID *</label><input className={INPUT} value={form.employeeId} onChange={e => setForm({ ...form, employeeId: e.target.value })} placeholder="e.g. EMP-001" /></div>
              <div /><div><label className={LABEL}>First Name *</label><input className={INPUT} value={form.firstName} onChange={e => setForm({ ...form, firstName: e.target.value })} /></div>
              <div><label className={LABEL}>Last Name *</label><input className={INPUT} value={form.lastName} onChange={e => setForm({ ...form, lastName: e.target.value })} /></div>
              <div><label className={LABEL}>Phone</label><input className={INPUT} value={form.phoneNumber} onChange={e => setForm({ ...form, phoneNumber: e.target.value })} placeholder="+91-98765-43210" /></div>
              <div><label className={LABEL}>Email</label><input className={INPUT} type="email" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} placeholder="driver@email.com" /></div>
            </div>
          </Section>

          <Section icon={MapPin} title="Address">
            <div className="grid grid-cols-3 gap-4">
              <div className="col-span-3"><label className={LABEL}>Street Address</label><input className={INPUT} value={form.address} onChange={e => setForm({ ...form, address: e.target.value })} /></div>
              <div><label className={LABEL}>City</label><input className={INPUT} value={form.city} onChange={e => setForm({ ...form, city: e.target.value })} /></div>
              <div><label className={LABEL}>Country</label><input className={INPUT} value={form.country} onChange={e => setForm({ ...form, country: e.target.value })} /></div>
            </div>
          </Section>

          <Section icon={CreditCard} title="License">
            <div className="grid grid-cols-3 gap-4">
              <div><label className={LABEL}>License Number</label><input className={INPUT} value={form.licenseNumber} onChange={e => setForm({ ...form, licenseNumber: e.target.value })} /></div>
              <div><label className={LABEL}>Category</label><input className={INPUT} value={form.licenseCategory} onChange={e => setForm({ ...form, licenseCategory: e.target.value })} placeholder="e.g. A, B, C" /></div>
              <div><label className={LABEL}>Expiry Date</label><input className={INPUT} type="date" value={form.licenseExpiry} onChange={e => setForm({ ...form, licenseExpiry: e.target.value })} /></div>
            </div>
          </Section>

          {isEdit && (
            <div>
              <label className={LABEL}>Status</label>
              <select className={INPUT + ' w-48'} value={form.status} onChange={e => setForm({ ...form, status: parseInt(e.target.value) })}>
                {Object.entries(STATUS_MAP).map(([v, l]) => <option key={v} value={v}>{l.label}</option>)}
              </select>
            </div>
          )}
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">{saving ? 'Saving...' : isEdit ? 'Update' : 'Create'}</button>
        </div>
      </div>
    </div>
  );
}
