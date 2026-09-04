import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { useTargetCompany } from '../hooks/useTargetCompany';
import TargetCompanyField from '../components/TargetCompanyField';
import RouteMapPane from '../components/RouteMapPane';
import type { PagedResult } from '../lib/api';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  Eye, X, Navigation, Zap, Route, Target, Flag, Ban, MapPin, ArrowUp, ArrowDown, Layers,
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
  // Geofence linkage + corridor config
  geofenceCount: number; checkpointCount: number; restrictedZoneCount: number; boundaryZoneCount: number;
  routeGeofences?: RouteGeofenceRow[];
  pathSource?: number; pathSourceName?: string;
  corridorEnabled?: boolean; corridorBufferMeters?: number; deviationThresholdMinutes?: number;
}

interface RouteGeofenceRow {
  id: string; routeId: string; geofenceId: string; geofenceName: string;
  geofenceType: number; geofenceTypeName?: string;
  role: number; roleName: string; sequenceOrder?: number | null;
  geometry?: string; centerLatitude?: number; centerLongitude?: number; radius?: number;
}

interface GeofenceOption {
  id: string; name: string; type: number; typeName: string; companyName?: string;
  geometry?: string; centerLatitude?: number; centerLongitude?: number; radius?: number;
}

const ROLE_LABELS = ['Checkpoint', 'Restricted Zone', 'Start Zone', 'End Zone'];
const ROLE_COLORS = ['text-green-700 bg-green-100', 'text-red-700 bg-red-100', 'text-blue-700 bg-blue-100', 'text-indigo-700 bg-indigo-100'];
const ROLE_DESC = ['Expected stop — vehicle must pass through', 'Do-not-enter area on this route', 'Route starts inside this geofence', 'Route ends inside this geofence'];

interface WaypointStop { name: string; lat: number; lng: number; stopDurationMinutes?: number; }

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
  3: { label: 'Round Trip', color: 'bg-blue-100 text-blue-700' },
  4: { label: 'Multi-Stop', color: 'bg-purple-100 text-purple-700' },
};

type SortField = 'name' | 'totalDistance' | 'priority' | 'status' | 'type';
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
  const { can } = usePermissions();
  const { version: scopeVersion, isMultiCompany } = useCompanyScope();
  const canCreate = can('route.create');
  const canEdit = can('route.update');
  const canDelete = can('route.delete');
  const canExport = can('route.export');

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
      if (sort.field) {
        params.set('sortBy', sort.field);
        params.set('sortDesc', String(sort.desc));
      }
      const res = await api.get(`/routes?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, statusFilter, sort, scopeVersion]);

  const fetchStats = useCallback(async () => {
    try { const res = await api.get('/routes/stats'); setStats(res.data.data); } catch { /* ignore */ }
  }, [scopeVersion]);

  useEffect(() => { fetchData(); }, [fetchData]);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { fetchStats(); }, [fetchStats]);

  const handleDelete = async (r: RouteDetail) => {
    try { await api.delete(`/routes/${r.id}`); setDeleteConfirm(null); fetchData(); fetchStats(); } catch (err) { console.error(err); }
  };

  const items = data?.items ?? [];
  // Sorting is now server-side; items come pre-sorted from the API
  const sorted = items;

  const toggleSort = (field: SortField) => { setPage(1); setSort(s => ({ field, desc: s.field === field ? !s.desc : false })); };

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
                  { label: 'Geofences', field: null },
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
                <tr><td colSpan={9} className="px-4 py-12 text-center text-gray-500">Loading routes...</td></tr>
              ) : sorted.length === 0 ? (
                <tr><td colSpan={9} className="px-4 py-12 text-center text-gray-500">No routes found</td></tr>
              ) : sorted.map(r => (
                <tr key={r.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-lg bg-blue-100 flex items-center justify-center">
                        <Route className="w-5 h-5 text-blue-600" />
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <p className="font-medium text-gray-900">{r.name}</p>
                          {isMultiCompany && r.companyName && (
                            <span className="text-[10px] font-medium text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded" title={r.companyName}>{r.companyName}</span>
                          )}
                        </div>
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
                    {r.geofenceCount > 0 ? (
                      <div className="flex flex-wrap gap-1 max-w-[150px]">
                        {r.checkpointCount > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-100 text-green-700">{r.checkpointCount} checkpoint{r.checkpointCount > 1 ? 's' : ''}</span>
                        )}
                        {r.restrictedZoneCount > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-red-100 text-red-700">{r.restrictedZoneCount} restricted</span>
                        )}
                        {r.boundaryZoneCount > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-blue-100 text-blue-700">{r.boundaryZoneCount} boundary</span>
                        )}
                        <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-gray-100 text-gray-600">{r.geofenceCount} total</span>
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
  const tgt = useTargetCompany();
  const { isCrossTenant, needsPick, targetCompanyId } = tgt;
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
    pathSource: route?.pathSource ?? 0, corridorEnabled: route?.corridorEnabled ?? false,
    corridorBufferMeters: route?.corridorBufferMeters ?? 500, deviationThresholdMinutes: route?.deviationThresholdMinutes ?? 10,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [stops, setStops] = useState<WaypointStop[]>(() => {
    try {
      const parsed = JSON.parse(route?.waypoints ?? '[]');
      return Array.isArray(parsed) ? parsed : [];
    } catch { return []; }
  });
  // Route ↔ Geofence links (semantic roles) + the geofence options list.
  const [links, setLinks] = useState<RouteGeofenceRow[]>([]);
  const [options, setOptions] = useState<GeofenceOption[]>([]);
  const [optionsSearch, setOptionsSearch] = useState('');
  const [linksLoaded, setLinksLoaded] = useState(false);

  const set = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }));

  // Load the geofence pick-list and (for an existing route) its current links.
  useEffect(() => {
    let alive = true;
    api.get('/geofences?pageSize=100').then(res => {
      if (alive) setOptions((res.data.data?.items ?? []).map((g: any) => ({
        id: g.id, name: g.name, type: g.type, typeName: g.typeName, companyName: g.companyName,
        geometry: g.geometry, centerLatitude: g.centerLatitude, centerLongitude: g.centerLongitude, radius: g.radius,
      })));
    }).catch(() => { /* pick-list is a convenience; the server enforces */ });
    if (route) {
      api.get(`/routes/${route.id}/geofences`).then(res => {
        if (alive) { setLinks(res.data.data ?? []); setLinksLoaded(true); }
      }).catch(() => { setLinksLoaded(true); });
    } else setLinksLoaded(true);
    return () => { alive = false; };
  }, [route?.id]);

  const updateStops = (next: WaypointStop[]) => {
    setStops(next);
    set('waypoints', JSON.stringify(next));
  };

  const linkChange = (index: number, patch: Partial<RouteGeofenceRow>) =>
    setLinks(ls => ls.map((l, i) => (i === index ? { ...l, ...patch } : l)));

  const addLink = (geofenceId: string) => {
    const opt = options.find(o => o.id === geofenceId);
    if (!opt) return;
    if (links.some(l => l.geofenceId === geofenceId)) { setError('That geofence is already linked to this route — pick a single role for it.'); return; }
    const nextSeq = links.filter(l => l.role === 0).length + 1;
    setLinks(ls => [...ls, {
      id: '', routeId: route?.id ?? '', geofenceId: opt.id, geofenceName: opt.name,
      geofenceType: opt.type, geofenceTypeName: opt.typeName, role: 0, roleName: 'Checkpoint', sequenceOrder: nextSeq,
      geometry: opt.geometry, centerLatitude: opt.centerLatitude, centerLongitude: opt.centerLongitude, radius: opt.radius,
    }]);
    setError('');
  };

  const removeLink = (index: number) => setLinks(ls => ls.filter((_, i) => i !== index));

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Name is required'); return; }
    if (!route && isCrossTenant && needsPick) { setError('Select the company this route belongs to'); return; }

    // Client-side link validation (server re-checks): unique geofence, no
    // duplicate checkpoint sequence numbers.
    const seen = new Set<string>();
    const seqs = new Set<number>();
    for (const l of links) {
      if (seen.has(l.geofenceId)) { setError('A geofence can only be linked once per route — pick a single role for it.'); return; }
      seen.add(l.geofenceId);
      if (l.role === 0 && l.sequenceOrder != null) {
        if (seqs.has(l.sequenceOrder)) { setError(`Duplicate checkpoint sequence number ${l.sequenceOrder}.`); return; }
        seqs.add(l.sequenceOrder);
      }
    }

    setSaving(true); setError('');
    const payload: Record<string, unknown> = {
      ...form,
      name: form.name.trim(),
      corridorEnabled: form.corridorEnabled,
      corridorBufferMeters: form.corridorEnabled ? form.corridorBufferMeters : null,
      deviationThresholdMinutes: form.corridorEnabled ? form.deviationThresholdMinutes : null,
    };
    try {
      let id = route?.id;
      if (route) {
        await api.put(`/routes/${route.id}`, payload);
      } else {
        const res = await api.post('/routes', { ...payload, ...(isCrossTenant ? { companyId: targetCompanyId } : {}) });
        id = res.data.data?.id;
        if (!id) { setError('Route was created but the id could not be read.'); return; }
      }
      if (linksLoaded && id) {
        const body = links.map(l => ({ geofenceId: l.geofenceId, role: l.role, sequenceOrder: l.role === 0 ? (l.sequenceOrder ?? null) : null }));
        await api.put(`/routes/${id}/geofences`, body);
      }
      onSaved();
    } catch (err: any) { setError(err.response?.data?.message ?? 'Failed to save route'); }
    setSaving(false);
  };

  const readonly = isView || !canEdit;
  const canLink = !readonly && !!form.originName.trim() && !!form.destinationName?.trim();
  const visibleOptions = options.filter(o =>
    o.name.toLowerCase().includes(optionsSearch.toLowerCase())
    && !links.some(l => l.geofenceId === o.id));
  const [showMap, setShowMap] = useState(false);
  const mapPath = [
    { lat: form.originLatitude, lng: form.originLongitude },
    ...stops.map(s => ({ lat: s.lat, lng: s.lng })),
    { lat: form.destinationLatitude ?? 0, lng: form.destinationLongitude ?? 0 },
  ].filter(p => isFinite(p.lat) && isFinite(p.lng));
  const mapFences = links.map(l => ({
    geofenceId: l.geofenceId, geofenceName: l.geofenceName, role: l.role,
    geometry: l.geometry, centerLatitude: l.centerLatitude, centerLongitude: l.centerLongitude, radius: l.radius,
  }));

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-xl w-full max-w-3xl max-h-[90vh] overflow-y-auto">
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

            {/* Linked geofences in view mode */}
            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-2">Linked Geofences</h4>
              {links.length === 0 ? <p className="text-xs text-gray-500">No geofences linked to this route.</p> : (
                <ul className="space-y-2">
                  {links.map(l => (
                    <li key={l.geofenceId + l.role} className="flex items-center justify-between bg-white rounded-lg px-3 py-2 border">
                      <div className="flex items-center gap-2">
                        <span className={`px-2 py-0.5 rounded-full text-[10px] font-medium ${ROLE_COLORS[l.role] ?? ROLE_COLORS[0]}`}>{ROLE_LABELS[l.role] ?? l.roleName}</span>
                        <span className="text-sm font-medium text-gray-800">{l.geofenceName}</span>
                        {l.role === 0 && l.sequenceOrder != null && <span className="text-xs text-gray-500">stop #{l.sequenceOrder}</span>}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <div className="grid grid-cols-3 gap-4 text-xs">
              <div><p className="text-gray-500">Assigned Vehicles</p><p className="font-bold text-lg">{route.assignedVehicleCount}</p></div>
              <div><p className="text-gray-500">Completed Trips</p><p className="font-bold text-lg">{route.completedTripCount}</p></div>
              <div><p className="text-gray-500">Traffic Level</p><p className="font-bold text-lg">{route.trafficLevel ?? '—'}%</p></div>
            </div>
          </div>
        ) : (
          <div className="p-6 space-y-5">
            {error && <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-2 rounded-lg text-sm">{error}</div>}

            {!route && <TargetCompanyField hook={tgt} error={error} />}

            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Route Name *</label><input className={INPUT} value={form.name} onChange={e => set('name', e.target.value)} /></div>
              <div><label className={LABEL}>Type</label>
                <select className={INPUT} value={form.type} onChange={e => set('type', Number(e.target.value))}>
                  <option value={0}>Standard</option><option value={1}>Optimized</option><option value={2}>Express</option><option value={3}>Round Trip</option><option value={4}>Multi-Stop</option>
                </select>
              </div>
            </div>
            <div><label className={LABEL}>Description</label><textarea className={INPUT} rows={2} value={form.description} onChange={e => set('description', e.target.value)} /></div>

            <button type="button" onClick={() => setShowMap(s => !s)}
              className="flex items-center gap-1.5 text-xs text-blue-600 hover:underline">
              <MapPin className="w-3.5 h-3.5" /> {showMap ? 'Hide map' : 'Show route on map'}
            </button>
            {showMap && (
              <RouteMapPane waypoints={mapPath} routeGeometry={null} fences={mapFences} />
            )}

            <div className="bg-gray-50 rounded-lg p-4 space-y-3">
              <div className="flex items-center justify-between">
                <h4 className="text-sm font-medium">Origin → Destination</h4>
                <span className="text-[10px] text-gray-400">geofence linking unlocks once both are set</span>
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div className="col-span-2"><label className={LABEL}>Origin Name</label><input className={INPUT} value={form.originName} onChange={e => set('originName', e.target.value)} /></div>
                <div className="grid grid-cols-2 gap-2">
                  <div><label className={LABEL}>Lat</label><input type="number" step="any" className={INPUT} value={form.originLatitude} onChange={e => set('originLatitude', Number(e.target.value))} /></div>
                  <div><label className={LABEL}>Lng</label><input type="number" step="any" className={INPUT} value={form.originLongitude} onChange={e => set('originLongitude', Number(e.target.value))} /></div>
                </div>
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div className="col-span-2"><label className={LABEL}>Destination Name</label><input className={INPUT} value={form.destinationName ?? ''} onChange={e => set('destinationName', e.target.value)} /></div>
                <div className="grid grid-cols-2 gap-2">
                  <div><label className={LABEL}>Lat</label><input type="number" step="any" className={INPUT} value={form.destinationLatitude ?? 0} onChange={e => set('destinationLatitude', Number(e.target.value))} /></div>
                  <div><label className={LABEL}>Lng</label><input type="number" step="any" className={INPUT} value={form.destinationLongitude ?? 0} onChange={e => set('destinationLongitude', Number(e.target.value))} /></div>
                </div>
              </div>

              {/* Waypoint stops (ordered; persisted as the waypoints JSON) */}
              <div className="pt-1">
                <div className="flex items-center justify-between mb-1">
                  <label className="text-xs font-medium text-gray-600">Stops / Waypoints</label>
                  <button type="button" onClick={() => updateStops([...stops, { name: `Stop ${stops.length + 1}`, lat: 0, lng: 0 }])}
                    className="text-xs text-blue-600 hover:underline">+ Add stop</button>
                </div>
                {stops.length === 0 ? <p className="text-[11px] text-gray-400">No intermediate stops — this is a direct origin→destination run.</p> : (
                  <div className="space-y-1.5">
                    {stops.map((s, i) => (
                      <div key={i} className="flex items-center gap-2">
                        <span className="w-5 text-[10px] font-bold text-gray-400 text-center">{i + 1}</span>
                        <input className="flex-1 px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Stop name" value={s.name}
                          onChange={e => { const n = [...stops]; n[i] = { ...s, name: e.target.value }; updateStops(n); }} />
                        <input className="w-24 px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Lat" type="number" step="any" value={s.lat}
                          onChange={e => { const n = [...stops]; n[i] = { ...s, lat: Number(e.target.value) }; updateStops(n); }} />
                        <input className="w-24 px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Lng" type="number" step="any" value={s.lng}
                          onChange={e => { const n = [...stops]; n[i] = { ...s, lng: Number(e.target.value) }; updateStops(n); }} />
                        <button type="button" disabled={i === 0} onClick={() => { const n = [...stops]; [n[i - 1], n[i]] = [n[i], n[i - 1]]; updateStops(n); }}
                          className="p-1 text-gray-400 hover:text-gray-700 disabled:opacity-30"><ArrowUp className="w-3.5 h-3.5" /></button>
                        <button type="button" disabled={i === stops.length - 1} onClick={() => { const n = [...stops]; [n[i + 1], n[i]] = [n[i], n[i + 1]]; updateStops(n); }}
                          className="p-1 text-gray-400 hover:text-gray-700 disabled:opacity-30"><ArrowDown className="w-3.5 h-3.5" /></button>
                        <button type="button" onClick={() => updateStops(stops.filter((_, j) => j !== i))}
                          className="p-1 text-gray-300 hover:text-red-500"><X className="w-3.5 h-3.5" /></button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            {/* Path + corridor configuration */}
            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Path Source</label>
                <select className={INPUT} value={form.pathSource} onChange={e => set('pathSource', Number(e.target.value))}>
                  <option value={0}>Directions (road-snapped)</option><option value={1}>Manually drawn (not road-snapped)</option>
                </select>
              </div>
              <div className="flex items-end">
                <label className="flex items-center gap-2 text-sm text-gray-700 pb-2">
                  <input type="checkbox" checked={form.corridorEnabled} onChange={e => set('corridorEnabled', e.target.checked)} className="rounded" />
                  Corridor deviation alerts
                </label>
              </div>
            </div>
            {form.corridorEnabled && (
              <div className="grid grid-cols-2 gap-4">
                <div><label className={LABEL}>Corridor buffer (m)</label>
                  <input type="number" min={50} max={10000} step={50} className={INPUT} value={form.corridorBufferMeters} onChange={e => set('corridorBufferMeters', Number(e.target.value))} />
                </div>
                <div><label className={LABEL}>Deviation threshold (min)</label>
                  <input type="number" min={1} max={60} className={INPUT} value={form.deviationThresholdMinutes} onChange={e => set('deviationThresholdMinutes', Number(e.target.value))} />
                </div>
              </div>
            )}

            {/* Route ↔ Geofence linking */}
            <div className="bg-gray-50 rounded-lg p-4 space-y-3">
              <div className="flex items-center justify-between">
                <h4 className="text-sm font-medium flex items-center gap-1.5"><Layers className="w-4 h-4 text-gray-500" /> Linked Geofences</h4>
                <span className="text-[10px] text-gray-400">checkpoint / restricted / start / end</span>
              </div>

              {!canLink ? (
                <p className="text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded-lg px-3 py-2">
                  Set the origin and destination first — checkpoints need a defined start→end path.
                </p>
              ) : (
                <>
                  <div className="flex gap-2">
                    <input className={`${INPUT} flex-1`} placeholder="Search geofences to link…" value={optionsSearch} onChange={e => setOptionsSearch(e.target.value)} />
                    <select className={`${INPUT} w-44`} value="" onChange={e => { if (e.target.value) addLink(e.target.value); e.target.value = ''; }} disabled={visibleOptions.length === 0}>
                      <option value="">{visibleOptions.length === 0 ? 'No geofences left' : 'Select a geofence…'}</option>
                      {visibleOptions.map(o => (
                        <option key={o.id} value={o.id}>{o.name}{o.typeName ? ` · ${o.typeName}` : ''}</option>
                      ))}
                    </select>
                  </div>

                  {links.length === 0 ? (
                    <p className="text-xs text-gray-400">No geofences linked yet.</p>
                  ) : (
                    <ul className="space-y-2">
                      {links.map((l, i) => (
                        <li key={i} className="bg-white rounded-lg border px-3 py-2 flex items-center gap-3">
                          <Flag className="w-4 h-4 text-gray-300 shrink-0" />
                          <div className="flex-1 min-w-0">
                            <p className="text-sm font-medium text-gray-800 truncate">{l.geofenceName}</p>
                            <p className="text-[10px] text-gray-400">{ROLE_DESC[l.role] ?? ''}</p>
                          </div>
                          <select className="border border-gray-300 rounded-lg px-2 py-1.5 text-xs" value={l.role} onChange={e => linkChange(i, { role: Number(e.target.value), roleName: ROLE_LABELS[Number(e.target.value)] })}>
                            {ROLE_LABELS.map((rl, ri) => <option key={ri} value={ri}>{rl}</option>)}
                          </select>
                          {l.role === 0 && (
                            <input type="number" min={1} className="w-16 px-2 py-1.5 border border-gray-300 rounded-lg text-xs" title="Checkpoint sequence"
                              placeholder="Seq" value={l.sequenceOrder ?? ''}
                              onChange={e => linkChange(i, { sequenceOrder: e.target.value === '' ? null : Number(e.target.value) })} />
                          )}
                          <button type="button" onClick={() => removeLink(i)} className="p-1 text-gray-300 hover:text-red-500" title="Remove link"><X className="w-4 h-4" /></button>
                        </li>
                      ))}
                    </ul>
                  )}
                </>
              )}
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
