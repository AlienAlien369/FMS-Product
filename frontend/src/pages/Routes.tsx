import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import type { PagedResult } from '../lib/api';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  Eye, X, Map, Navigation, Zap, Clock, DollarSign, Route, Target, RotateCcw,
} from 'lucide-react';

// ── Types ────────────────────────────────────────────────
interface RouteDetail {
  id: string; name: string; description?: string;
  status: number; statusName: string; type: number; typeName: string;
  isOptimized: boolean; isTemplate: boolean; companyName: string;
  originName: string; originLatitude: number; originLongitude: number;
  destinationName?: string; destinationLatitude?: number; destinationLongitude?: number;
  waypoints?: string; routeGeometry?: string;
  totalDistance?: number; distanceUnit?: string; estimatedDuration?: string;
  estimatedFuelCost?: number; estimatedTollCost?: number; currency?: string; trafficLevel?: number;
  validFrom?: string; validUntil?: string; maxVehicles?: number; priority?: number;
  recurrenceRule?: string; dayOfWeek?: number; preferredStartTime?: string;
  assignedVehicleCount: number; completedTripCount: number; createdAt: string;
}

interface RouteStats {
  total: number; active: number; draft: number; inProgress: number; completed: number;
  templates: number; optimized: number; totalDistance: number;
}

// ── Constants ────────────────────────────────────────────
const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

const STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: 'Draft', color: 'bg-gray-100 text-gray-700' },
  1: { label: 'Active', color: 'bg-green-100 text-green-700' },
  2: { label: 'InProgress', color: 'bg-blue-100 text-blue-700' },
  3: { label: 'Completed', color: 'bg-purple-100 text-purple-700' },
  4: { label: 'Cancelled', color: 'bg-red-100 text-red-700' },
  5: { label: 'Suspended', color: 'bg-yellow-100 text-yellow-700' },
};

const TYPE_MAP: Record<number, { label: string; color: string }> = {
  0: { label: 'Standard', color: 'bg-gray-100 text-gray-700' },
  1: { label: 'Optimized', color: 'bg-green-100 text-green-700' },
  2: { label: 'Express', color: 'bg-orange-100 text-orange-700' },
  3: { label: 'MultiStop', color: 'bg-blue-100 text-blue-700' },
  4: { label: 'Circular', color: 'bg-purple-100 text-purple-700' },
};

type SortField = 'name' | 'totalDistance' | 'priority' | 'status';
interface SortState { field: SortField; desc: boolean; }

const STATUS_FILTERS: { key: string; label: string; value?: number; color: string; statKey?: string }[] = [
  { key: 'all', label: 'All', color: 'bg-blue-100 text-blue-700', statKey: 'total' },
  { key: '1', label: 'Active', value: 1, color: 'bg-green-100 text-green-700', statKey: 'active' },
  { key: '0', label: 'Draft', value: 0, color: 'bg-gray-100 text-gray-700', statKey: 'draft' },
  { key: '2', label: 'In Progress', value: 2, color: 'bg-blue-100 text-blue-700', statKey: 'inProgress' },
  { key: '3', label: 'Completed', value: 3, color: 'bg-purple-100 text-purple-700', statKey: 'completed' },
];

function fmtDuration(d?: string) {
  if (!d) return '—';
  const match = d.match(/(\d+):(\d+):(\d+)/);
  if (match) return `${match[1]}h ${match[2]}m`;
  return d;
}

// ── Main Component ───────────────────────────────────────
export default function RoutesPage() {
  const { hasPermission } = useAuth();
  const canCreate = hasPermission('geofence.create') || hasPermission('trip.create');
  const canEdit = hasPermission('geofence.edit') || hasPermission('trip.edit');
  const canDelete = hasPermission('geofence.delete') || hasPermission('trip.delete');

  const [data, setData] = useState<PagedResult<RouteDetail> | null>(null);
  const [stats, setStats] = useState<RouteStats | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('all');
  const [sort, setSort] = useState<SortState>({ field: 'name', desc: false });
  const [modal, setModal] = useState<{ open: boolean; edit?: RouteDetail; view?: RouteDetail }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<RouteDetail | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search });
      if (statusFilter !== 'all') params.set('status', statusFilter);
      const res = await api.get(`/routes?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, statusFilter]);

  const fetchStats = useCallback(async () => {
    try { const res = await api.get('/routes/stats'); setStats(res.data.data); } catch { /* ignore */ }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);
  useEffect(() => { fetchStats(); }, [fetchStats]);

  const handleDelete = async (r: RouteDetail) => {
    try { await api.delete(`/routes/${r.id}`); setDeleteConfirm(null); fetchData(); fetchStats(); } catch (err) { console.error(err); }
  };

  const items = data?.items ?? [];
  const sorted = [...items].sort((a, b) => {
    const mul = sort.desc ? -1 : 1;
    switch (sort.field) {
      case 'name': return mul * a.name.localeCompare(b.name);
      case 'totalDistance': return mul * ((a.totalDistance ?? 0) - (b.totalDistance ?? 0));
      case 'priority': return mul * ((a.priority ?? 0) - (b.priority ?? 0));
      case 'status': return mul * (a.status - b.status);
      default: return 0;
    }
  });

  const toggleSort = (field: SortField) => setSort(s => ({ field, desc: s.field === field ? !s.desc : false }));

  const SortIcon = ({ field }: { field: SortField }) => (
    sort.field === field ? (sort.desc ? <ChevronDown className="w-3 h-3" /> : <ChevronUp className="w-3 h-3" />) : null
  );

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div><h1 className="text-2xl font-bold text-gray-900">Routes</h1><p className="text-gray-500 text-sm mt-1">Plan, optimize, and manage fleet routes</p></div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })} className="flex items-center gap-2 bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm">
            <Plus className="w-4 h-4" /> Add Route
          </button>
        )}
      </div>

      {/* Stats */}
      {stats && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {[
            { label: 'Total Routes', value: stats.total, icon: Route, color: 'text-blue-600 bg-blue-100' },
            { label: 'Active', value: stats.active, icon: Zap, color: 'text-green-600 bg-green-100' },
            { label: 'Templates', value: stats.templates, icon: Target, color: 'text-purple-600 bg-purple-100' },
            { label: 'Total Distance', value: `${stats.totalDistance.toLocaleString()} km`, icon: Navigation, color: 'text-orange-600 bg-orange-100' },
          ].map(s => (
            <div key={s.label} className="bg-white rounded-xl border p-4 flex items-center gap-3">
              <div className={`p-2 rounded-lg ${s.color}`}><s.icon className="w-5 h-5" /></div>
              <div><p className="text-xs text-gray-500">{s.label}</p><p className="text-lg font-bold text-gray-900">{s.value}</p></div>
            </div>
          ))}
        </div>
      )}

      {/* Filters + Search */}
      <div className="flex flex-col md:flex-row gap-4 items-start md:items-center justify-between">
        <div className="flex flex-wrap gap-2">
          {STATUS_FILTERS.map(f => {
            const count = f.statKey && stats ? (stats as any)[f.statKey] ?? 0 : items.filter(i => f.value === undefined || i.status === f.value).length;
            return (
              <button key={f.key} onClick={() => { setStatusFilter(f.key); setPage(1); }}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${statusFilter === f.key ? f.color : 'bg-gray-100 text-gray-600 hover:bg-gray-200'}`}>
                {f.label} <span className="ml-1 font-bold">{count}</span>
              </button>
            );
          })}
        </div>
        <div className="relative w-full md:w-72">
          <Search className="absolute left-3 top-2.5 w-4 h-4 text-gray-400" />
          <input value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            placeholder="Search routes..." className={`${INPUT} pl-10`} />
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl border overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                {[
                  { label: 'Route', field: 'name' as SortField },
                  { label: 'Origin → Destination', field: null },
                  { label: 'Distance', field: 'totalDistance' as SortField },
                  { label: 'Duration', field: null },
                  { label: 'Priority', field: 'priority' as SortField },
                  { label: 'Status', field: 'status' as SortField },
                  { label: 'Vehicles', field: null },
                  { label: 'Actions', field: null },
                ].map(col => (
                  <th key={col.label} className={`px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase ${col.field ? 'cursor-pointer hover:text-gray-700 select-none' : ''}`}
                    onClick={() => col.field && toggleSort(col.field)}>
                    <span className="flex items-center gap-1">{col.label}{col.field && <SortIcon field={col.field} />}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y">
              {loading ? (
                <tr><td colSpan={8} className="px-4 py-12 text-center text-gray-500">Loading routes...</td></tr>
              ) : sorted.length === 0 ? (
                <tr><td colSpan={8} className="px-4 py-12 text-center text-gray-500">No routes found</td></tr>
              ) : sorted.map(r => (
                <tr key={r.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-lg bg-blue-100 flex items-center justify-center">
                        <Route className="w-5 h-5 text-blue-600" />
                      </div>
                      <div>
                        <p className="font-medium text-gray-900">{r.name}</p>
                        <div className="flex items-center gap-2 mt-0.5">
                          <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${(TYPE_MAP[r.type] ?? TYPE_MAP[0]).color}`}>
                            {(TYPE_MAP[r.type] ?? TYPE_MAP[0]).label}
                          </span>
                          {r.isOptimized && <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-100 text-green-700">Optimized</span>}
                          {r.isTemplate && <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-purple-100 text-purple-700">Template</span>}
                        </div>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <div className="text-xs">
                      <p className="font-medium text-gray-900">{r.originName}</p>
                      {r.destinationName && <><p className="text-gray-400 my-0.5">↓</p><p className="font-medium text-gray-900">{r.destinationName}</p></>}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-xs font-medium text-gray-900">{r.totalDistance != null ? `${r.totalDistance} ${r.distanceUnit ?? 'km'}` : '—'}</td>
                  <td className="px-4 py-3 text-xs text-gray-600">{fmtDuration(r.estimatedDuration)}</td>
                  <td className="px-4 py-3">
                    {r.priority != null ? (
                      <div className="flex items-center gap-1">
                        <div className={`w-2 h-2 rounded-full ${r.priority >= 4 ? 'bg-red-500' : r.priority >= 3 ? 'bg-orange-500' : r.priority >= 2 ? 'bg-yellow-500' : 'bg-green-500'}`} />
                        <span className="text-xs font-medium">{r.priority}</span>
                      </div>
                    ) : <span className="text-gray-400 text-xs">—</span>}
                  </td>
                  <td className="px-4 py-3">
                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${(STATUS_MAP[r.status] ?? STATUS_MAP[0]).color}`}>
                      {(STATUS_MAP[r.status] ?? STATUS_MAP[0]).label}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-xs text-gray-600">{r.assignedVehicleCount} / {r.maxVehicles ?? '∞'}</td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1">
                      <button onClick={() => setModal({ open: true, view: r })} className="p-1.5 rounded-lg text-gray-400 hover:text-blue-600 hover:bg-blue-50" title="View">
                        <Eye className="w-4 h-4" />
                      </button>
                      {canEdit && (
                        <button onClick={() => setModal({ open: true, edit: r })} className="p-1.5 rounded-lg text-gray-400 hover:text-amber-600 hover:bg-amber-50" title="Edit">
                          <Edit className="w-4 h-4" />
                        </button>
                      )}
                      {canDelete && (
                        <button onClick={() => setDeleteConfirm(r)} className="p-1.5 rounded-lg text-gray-400 hover:text-red-600 hover:bg-red-50" title="Delete">
                          <Trash2 className="w-4 h-4" />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {/* Pagination */}
        {data && data.totalCount > 10 && (
          <div className="flex items-center justify-between px-4 py-3 border-t">
            <span className="text-xs text-gray-500">Showing {((page - 1) * 10) + 1}–{Math.min(page * 10, data.totalCount)} of {data.totalCount}</span>
            <div className="flex items-center gap-2">
              <button disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="p-1.5 rounded-lg border hover:bg-gray-50 disabled:opacity-40"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-xs font-medium">Page {page}</span>
              <button disabled={page * 10 >= data.totalCount} onClick={() => setPage(p => p + 1)} className="p-1.5 rounded-lg border hover:bg-gray-50 disabled:opacity-40"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Delete Confirmation */}
      {deleteConfirm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-xl p-6 w-full max-w-md">
            <h3 className="text-lg font-semibold">Delete Route</h3>
            <p className="text-gray-600 mt-2">Are you sure you want to delete <strong>{deleteConfirm.name}</strong>? This action cannot be undone.</p>
            <div className="flex justify-end gap-3 mt-6">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm)} className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {/* View / Create / Edit Modal */}
      {modal.open && <RouteModal route={modal.edit || modal.view} isView={!!modal.view && !modal.edit} onClose={() => setModal({ open: false })}
        onSaved={() => { setModal({ open: false }); fetchData(); fetchStats(); }} canEdit={canEdit} />}
    </div>
  );
}

// ── Route Modal ──────────────────────────────────────────
function RouteModal({ route, isView, onClose, onSaved, canEdit }: {
  route?: RouteDetail; isView: boolean; onClose: () => void; onSaved: () => void; canEdit: boolean;
}) {
  const [form, setForm] = useState({
    name: route?.name ?? '', description: route?.description ?? '',
    type: route?.type ?? 0, isTemplate: route?.isTemplate ?? false,
    originName: route?.originName ?? '', originLatitude: route?.originLatitude ?? 0, originLongitude: route?.originLongitude ?? 0,
    destinationName: route?.destinationName ?? '', destinationLatitude: route?.destinationLatitude ?? 0, destinationLongitude: route?.destinationLongitude ?? 0,
    waypoints: route?.waypoints ?? '', totalDistance: route?.totalDistance ?? 0, distanceUnit: route?.distanceUnit ?? 'km',
    estimatedDuration: route?.estimatedDuration ?? '', estimatedFuelCost: route?.estimatedFuelCost ?? 0, estimatedTollCost: route?.estimatedTollCost ?? 0,
    currency: route?.currency ?? 'INR', trafficLevel: route?.trafficLevel ?? 0,
    priority: route?.priority ?? 1, maxVehicles: route?.maxVehicles ?? 0,
    status: route?.status ?? 0,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const set = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }));

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Name is required'); return; }
    setSaving(true); setError('');
    try {
      if (route) { await api.put(`/routes/${route.id}`, form); }
      else { await api.post('/routes', form); }
      onSaved();
    } catch (err: any) { setError(err.response?.data?.message ?? 'Failed to save route'); }
    setSaving(false);
  };

  const readonly = isView || !canEdit;

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-6 border-b">
          <h2 className="text-lg font-semibold">{isView ? 'Route Details' : route ? 'Edit Route' : 'Create Route'}</h2>
          <button onClick={onClose} className="p-2 rounded-lg hover:bg-gray-100"><X className="w-4 h-4" /></button>
        </div>

        {isView && route ? (
          <div className="p-6 space-y-6">
            <div className="grid grid-cols-2 gap-4">
              <div><p className="text-xs text-gray-500">Route Name</p><p className="font-medium">{route.name}</p></div>
              <div><p className="text-xs text-gray-500">Company</p><p className="font-medium">{route.companyName}</p></div>
              <div><p className="text-xs text-gray-500">Type</p><span className={`px-2 py-1 rounded-full text-xs font-medium ${(TYPE_MAP[route.type] ?? TYPE_MAP[0]).color}`}>{(TYPE_MAP[route.type] ?? TYPE_MAP[0]).label}</span></div>
              <div><p className="text-xs text-gray-500">Status</p><span className={`px-2 py-1 rounded-full text-xs font-medium ${(STATUS_MAP[route.status] ?? STATUS_MAP[0]).color}`}>{(STATUS_MAP[route.status] ?? STATUS_MAP[0]).label}</span></div>
              <div><p className="text-xs text-gray-500">Distance</p><p className="font-medium">{route.totalDistance != null ? `${route.totalDistance} ${route.distanceUnit ?? 'km'}` : '—'}</p></div>
              <div><p className="text-xs text-gray-500">Duration</p><p className="font-medium">{fmtDuration(route.estimatedDuration)}</p></div>
              <div><p className="text-xs text-gray-500">Fuel Cost</p><p className="font-medium">{route.estimatedFuelCost != null ? `${route.currency ?? ''} ${route.estimatedFuelCost}` : '—'}</p></div>
              <div><p className="text-xs text-gray-500">Toll Cost</p><p className="font-medium">{route.estimatedTollCost != null ? `${route.currency ?? ''} ${route.estimatedTollCost}` : '—'}</p></div>
            </div>
            {route.description && <div><p className="text-xs text-gray-500">Description</p><p className="text-sm text-gray-700">{route.description}</p></div>}
            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-2">Origin & Destination</h4>
              <div className="grid grid-cols-2 gap-4 text-xs">
                <div><p className="text-gray-500">Origin</p><p className="font-medium">{route.originName}</p><p className="text-gray-400">{route.originLatitude}, {route.originLongitude}</p></div>
                <div><p className="text-gray-500">Destination</p><p className="font-medium">{route.destinationName ?? '—'}</p>{route.destinationLatitude && <p className="text-gray-400">{route.destinationLatitude}, {route.destinationLongitude}</p>}</div>
              </div>
            </div>
            <div className="grid grid-cols-3 gap-4 text-xs">
              <div><p className="text-gray-500">Assigned Vehicles</p><p className="font-bold text-lg">{route.assignedVehicleCount}</p></div>
              <div><p className="text-gray-500">Completed Trips</p><p className="font-bold text-lg">{route.completedTripCount}</p></div>
              <div><p className="text-gray-500">Traffic Level</p><p className="font-bold text-lg">{route.trafficLevel ?? '—'}%</p></div>
            </div>
          </div>
        ) : (
          <div className="p-6 space-y-4">
            {error && <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-2 rounded-lg text-sm">{error}</div>}

            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Route Name *</label><input className={INPUT} value={form.name} onChange={e => set('name', e.target.value)} /></div>
              <div><label className={LABEL}>Type</label>
                <select className={INPUT} value={form.type} onChange={e => set('type', Number(e.target.value))}>
                  <option value={0}>Standard</option><option value={1}>Optimized</option><option value={2}>Express</option><option value={3}>MultiStop</option><option value={4}>Circular</option>
                </select>
              </div>
            </div>
            <div><label className={LABEL}>Description</label><textarea className={INPUT} rows={2} value={form.description} onChange={e => set('description', e.target.value)} /></div>

            <div className="grid grid-cols-3 gap-4">
              <div className="col-span-2"><label className={LABEL}>Origin Name</label><input className={INPUT} value={form.originName} onChange={e => set('originName', e.target.value)} /></div>
              <div><label className={LABEL}>Origin Lat</label><input type="number" step="any" className={INPUT} value={form.originLatitude} onChange={e => set('originLatitude', Number(e.target.value))} /></div>
            </div>
            <div className="grid grid-cols-3 gap-4">
              <div className="col-span-2"><label className={LABEL}>Destination Name</label><input className={INPUT} value={form.destinationName} onChange={e => set('destinationName', e.target.value)} /></div>
              <div><label className={LABEL}>Dest Lat</label><input type="number" step="any" className={INPUT} value={form.destinationLatitude} onChange={e => set('destinationLatitude', Number(e.target.value))} /></div>
            </div>

            <div className="grid grid-cols-4 gap-4">
              <div><label className={LABEL}>Distance (km)</label><input type="number" className={INPUT} value={form.totalDistance} onChange={e => set('totalDistance', Number(e.target.value))} /></div>
              <div><label className={LABEL}>Fuel Cost</label><input type="number" className={INPUT} value={form.estimatedFuelCost} onChange={e => set('estimatedFuelCost', Number(e.target.value))} /></div>
              <div><label className={LABEL}>Toll Cost</label><input type="number" className={INPUT} value={form.estimatedTollCost} onChange={e => set('estimatedTollCost', Number(e.target.value))} /></div>
              <div><label className={LABEL}>Priority</label><input type="number" min={1} max={5} className={INPUT} value={form.priority} onChange={e => set('priority', Number(e.target.value))} /></div>
            </div>

            <div className="flex items-center gap-2">
              <input type="checkbox" checked={form.isTemplate} onChange={e => set('isTemplate', e.target.checked)} className="rounded" />
              <label className="text-sm text-gray-700">Save as template</label>
            </div>
          </div>
        )}

        <div className="flex justify-end gap-3 p-6 border-t">
          <button onClick={onClose} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Close</button>
          {!readonly && (
            <button onClick={handleSubmit} disabled={saving} className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
              {saving ? 'Saving...' : route ? 'Update Route' : 'Create Route'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
