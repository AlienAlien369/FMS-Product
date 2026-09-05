import { useEffect, useState, useCallback, useRef } from 'react';
import api from '../lib/api';
import { usePermissions } from '../hooks/usePermissions';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { useTargetCompany } from '../hooks/useTargetCompany';
import TargetCompanyField from '../components/TargetCompanyField';
import RouteMapPane from '../components/RouteMapPane';
import type { PagedResult } from '../lib/api';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  Eye, X, Navigation, Zap, MapPin, ArrowUp, ArrowDown, Layers, Flag, Ban, Play, CheckCircle2,
  Clock, Radio, History, Calendar, Route as RouteIcon,
} from 'lucide-react';

// ── Types (mirror TripDtos.cs) ────────────────────────────
interface TripWaypoint {
  id?: string;
  sequenceOrder: number;
  legType: number;
  legTypeName?: string;
  waypointType: number;
  waypointTypeName?: string;
  name: string;
  latitude: number;
  longitude: number;
  address?: string;
  expectedArrival?: string | null;
  actualArrival?: string | null;
  linkedGeofenceId?: string | null;
}

interface TripGeofenceRow {
  id: string; tripId: string; geofenceId: string; geofenceName: string;
  geofenceType: number; geofenceTypeName?: string;
  geometry?: string; centerLatitude?: number; centerLongitude?: number; radius?: number;
  role: number; roleName: string; sequenceOrder?: number | null;
  visited?: boolean | null; visitedAt?: string | null;
}

interface TripStatusHistoryRow {
  fromStatus: number; toStatus: number; reason?: string; source: string; changedAt: string;
}

interface TripDetail {
  id: string; name: string; description?: string;
  status: number; statusName: string; isDelayed: boolean; delayReason?: string; cancelReason?: string;
  type: number; typeName: string; companyName: string;
  vehicleId: string; vehicleName: string; driverId: string; driverName: string;
  routeId?: string | null; routeName?: string | null;
  scheduledStartTime?: string | null; scheduledEndTime?: string | null;
  actualStartTime?: string | null; actualEndTime?: string | null;
  plannedDistance?: number | null; actualDistance?: number | null;
  plannedDuration?: string | null; actualDuration?: string | null;
  maxSpeed?: number | null; averageSpeed?: number | null;
  fuelUsedLiters?: number | null; idleMinutes?: number | null;
  routeGeometry?: string | null;
  corridorEnabled: boolean; corridorBufferMeters?: number | null; deviationThresholdMinutes?: number | null;
  waypointCount: number; geofenceCount: number; checkpointCount: number; restrictedZoneCount: number; boundaryZoneCount: number;
  waypoints?: TripWaypoint[];
  tripGeofences?: TripGeofenceRow[];
  statusHistory?: TripStatusHistoryRow[];
  createdAt: string;
}

interface GeofenceOption {
  id: string; name: string; type: number; typeName: string; companyName?: string;
  geometry?: string; centerLatitude?: number; centerLongitude?: number; radius?: number;
}

interface TripStats {
  total: number; scheduled: number; inProgress: number; completed: number;
  delayed: number; cancelled: number; totalDistance: number;
}

interface LivePosition { latitude?: number | null; longitude?: number | null; speedKmh?: number | null; headingDeg?: number | null; updatedAt?: string | null; }
interface ReplayPoint { eventTimeUtc: string; latitude?: number | null; longitude?: number | null; speedKmh?: number | null; headingDeg?: number | null; ignition?: boolean | null; }

// ── Constants ────────────────────────────────────────────
const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

const STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: 'Draft', color: 'bg-gray-100 text-gray-700' },
  1: { label: 'Scheduled', color: 'bg-blue-100 text-blue-700' },
  2: { label: 'In Progress', color: 'bg-green-100 text-green-700' },
  3: { label: 'Completed', color: 'bg-purple-100 text-purple-700' },
  4: { label: 'Cancelled', color: 'bg-red-100 text-red-700' },
  5: { label: 'Aborted', color: 'bg-orange-100 text-orange-700' },
};

const TYPE_MAP: Record<number, { label: string; color: string }> = {
  0: { label: 'Single Trip', color: 'bg-gray-100 text-gray-700' },
  1: { label: 'Round Trip', color: 'bg-blue-100 text-blue-700' },
};

const LEG_LABELS = ['Outbound', 'Return'];
const LEG_COLORS = ['bg-blue-100 text-blue-700', 'bg-purple-100 text-purple-700'];

const WAYPOINT_TYPE_LABELS = ['Pickup', 'Delivery', 'Rest', 'Fuel', 'Checkpoint', 'Other'];

const ROLE_LABELS = ['Checkpoint', 'Restricted Zone', 'Start Zone', 'End Zone'];
const ROLE_COLORS = ['text-green-700 bg-green-100', 'text-red-700 bg-red-100', 'text-blue-700 bg-blue-100', 'text-indigo-700 bg-indigo-100'];
const ROLE_DESC = ['Expected stop — vehicle must pass through', 'Do-not-enter area on this trip', 'Trip starts inside this geofence', 'Trip ends inside this geofence'];

const STATUS_FILTERS: { key: string; label: string; value?: number; color: string; statKey?: string }[] = [
  { key: 'all', label: 'All', color: 'bg-blue-100 text-blue-700', statKey: 'total' },
  { key: '1', label: 'Scheduled', value: 1, color: 'bg-blue-100 text-blue-700', statKey: 'scheduled' },
  { key: '2', label: 'In Progress', value: 2, color: 'bg-green-100 text-green-700', statKey: 'inProgress' },
  { key: '3', label: 'Completed', value: 3, color: 'bg-purple-100 text-purple-700', statKey: 'completed' },
  { key: '4', label: 'Cancelled', value: 4, color: 'bg-red-100 text-red-700', statKey: 'cancelled' },
];

function fmtDate(d?: string | null) {
  if (!d) return '—';
  const dt = new Date(d);
  if (isNaN(dt.getTime())) return d;
  return dt.toLocaleString(undefined, { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
}

function fmtDuration(d?: string | null) {
  if (!d) return '—';
  const match = d.match(/(\d+):(\d+):(\d+)/);
  if (match) return `${match[1]}h ${match[2]}m`;
  return d;
}

// ── Main Component ───────────────────────────────────────
export default function TripsPage() {
  const { can } = usePermissions();
  const { version: scopeVersion, isMultiCompany } = useCompanyScope();
  const canCreate = can('trip.create');
  const canEdit = can('trip.update');
  const canDelete = can('trip.delete');

  const [data, setData] = useState<PagedResult<TripDetail> | null>(null);
  const [stats, setStats] = useState<TripStats | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('all');
  const [typeFilter, setTypeFilter] = useState<'all' | '0' | '1'>('all');
  const [modal, setModal] = useState<{ open: boolean; edit?: TripDetail; view?: TripDetail }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<TripDetail | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search });
      if (statusFilter !== 'all') params.set('status', statusFilter);
      if (typeFilter !== 'all') params.set('type', typeFilter);
      params.set('sortBy', 'scheduledStartTime');
      params.set('sortDesc', 'true');
      const res = await api.get(`/trips?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, statusFilter, typeFilter, scopeVersion]);

  const fetchStats = useCallback(async () => {
    try { const res = await api.get('/trips/stats'); setStats(res.data.data); } catch { /* ignore */ }
  }, [scopeVersion]);

  useEffect(() => { fetchData(); }, [fetchData]);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { fetchStats(); }, [fetchStats]);

  const handleDelete = async (t: TripDetail) => {
    try {
      await api.delete(`/trips/${t.id}`);
      setDeleteConfirm(null); fetchData(); fetchStats();
    } catch (err: any) {
      alert(err.response?.data?.message ?? 'Failed to delete trip');
    }
  };

  const items = data?.items ?? [];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div><h1 className="text-2xl font-bold text-gray-900">Trips</h1><p className="text-gray-500 text-sm mt-1">Plan, dispatch and track vehicle journeys</p></div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })} className="flex items-center gap-2 bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm">
            <Plus className="w-4 h-4" /> Add Trip
          </button>
        )}
      </div>

      {/* Stats */}
      {stats && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {[
            { label: 'Total Trips', value: stats.total, icon: Navigation, color: 'text-blue-600 bg-blue-100' },
            { label: 'In Progress', value: stats.inProgress, icon: Play, color: 'text-green-600 bg-green-100' },
            { label: 'Scheduled', value: stats.scheduled, icon: Calendar, color: 'text-indigo-600 bg-indigo-100' },
            { label: 'Completed', value: stats.completed, icon: CheckCircle2, color: 'text-purple-600 bg-purple-100' },
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
          <select className="px-3 py-1.5 rounded-lg text-xs font-medium border border-gray-200 text-gray-600" value={typeFilter} onChange={e => { setTypeFilter(e.target.value as any); setPage(1); }}>
            <option value="all">All types</option>
            <option value="0">Single</option>
            <option value="1">Round</option>
          </select>
        </div>
        <div className="relative w-full md:w-72">
          <Search className="absolute left-3 top-2.5 w-4 h-4 text-gray-400" />
          <input value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            placeholder="Search trips..." className={`${INPUT} pl-10`} />
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl border overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                {['Trip', 'Vehicle / Driver', 'Type', 'Route', 'Geofences', 'Scheduled Start', 'Status', 'Actions'].map(label => (
                  <th key={label} className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">{label}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y">
              {loading ? (
                <tr><td colSpan={8} className="px-4 py-12 text-center text-gray-500">Loading trips...</td></tr>
              ) : items.length === 0 ? (
                <tr><td colSpan={8} className="px-4 py-12 text-center text-gray-500">No trips found</td></tr>
              ) : items.map(t => (
                <tr key={t.id} className="hover:bg-gray-50 transition-colors">
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 rounded-lg bg-blue-100 flex items-center justify-center">
                        <Navigation className="w-5 h-5 text-blue-600" />
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <p className="font-medium text-gray-900">{t.name}</p>
                          {t.isDelayed && t.status !== 3 && (
                            <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-amber-100 text-amber-700" title={t.delayReason ?? 'Delayed'}>Delayed</span>
                          )}
                          {isMultiCompany && t.companyName && (
                            <span className="text-[10px] font-medium text-gray-500 bg-gray-100 px-1.5 py-0.5 rounded" title={t.companyName}>{t.companyName}</span>
                          )}
                        </div>
                        <div className="flex items-center gap-2 mt-0.5">
                          <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${(TYPE_MAP[t.type] ?? TYPE_MAP[0]).color}`}>
                            {(TYPE_MAP[t.type] ?? TYPE_MAP[0]).label}
                          </span>
                          <span className="text-[10px] text-gray-400">{t.waypointCount} waypoints</span>
                        </div>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <p className="text-xs font-medium text-gray-900">{t.vehicleName}</p>
                    <p className="text-[11px] text-gray-500">{t.driverName}</p>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${(TYPE_MAP[t.type] ?? TYPE_MAP[0]).color}`}>
                      {(TYPE_MAP[t.type] ?? TYPE_MAP[0]).label}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-xs text-gray-600">{t.routeName ?? 'Dynamic route'}</td>
                  <td className="px-4 py-3">
                    {t.geofenceCount > 0 ? (
                      <div className="flex flex-wrap gap-1 max-w-[160px]">
                        {t.checkpointCount > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-100 text-green-700">{t.checkpointCount} ckpt</span>
                        )}
                        {t.restrictedZoneCount > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-red-100 text-red-700">{t.restrictedZoneCount} restricted</span>
                        )}
                        {t.boundaryZoneCount > 0 && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-blue-100 text-blue-700">{t.boundaryZoneCount} boundary</span>
                        )}
                      </div>
                    ) : <span className="text-gray-400 text-xs">—</span>}
                  </td>
                  <td className="px-4 py-3 text-xs text-gray-600">{fmtDate(t.scheduledStartTime)}</td>
                  <td className="px-4 py-3">
                    <span className={`px-2 py-1 rounded-full text-xs font-medium ${(STATUS_MAP[t.status] ?? STATUS_MAP[0]).color}`}>
                      {(STATUS_MAP[t.status] ?? STATUS_MAP[0]).label}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1">
                      <button onClick={() => setModal({ open: true, view: t })} className="p-1.5 rounded-lg text-gray-400 hover:text-blue-600 hover:bg-blue-50" title="View / Track">
                        <Eye className="w-4 h-4" />
                      </button>
                      {canEdit && (t.status === 0 || t.status === 1) && (
                        <button onClick={() => setModal({ open: true, edit: t })} className="p-1.5 rounded-lg text-gray-400 hover:text-amber-600 hover:bg-amber-50" title="Edit">
                          <Edit className="w-4 h-4" />
                        </button>
                      )}
                      {canDelete && (
                        <button onClick={() => setDeleteConfirm(t)} className="p-1.5 rounded-lg text-gray-400 hover:text-red-600 hover:bg-red-50" title="Delete">
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
            <h3 className="text-lg font-semibold">Delete Trip</h3>
            <p className="text-gray-600 mt-2">Are you sure you want to delete <strong>{deleteConfirm.name}</strong>? This action cannot be undone.</p>
            <div className="flex justify-end gap-3 mt-6">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm)} className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {/* View / Create / Edit Modal */}
      {modal.open && <TripModal trip={modal.edit || modal.view} isView={!!modal.view && !modal.edit} onClose={() => setModal({ open: false })}
        onSaved={() => { setModal({ open: false }); fetchData(); fetchStats(); }} canEdit={canEdit} />}
    </div>
  );
}

// ── Trip Modal ───────────────────────────────────────────
function TripModal({ trip, isView, onClose, onSaved, canEdit }: {
  trip?: TripDetail; isView: boolean; onClose: () => void; onSaved: () => void; canEdit: boolean;
}) {
  const tgt = useTargetCompany();
  const { isCrossTenant, needsPick, targetCompanyId } = tgt;
  const [form, setForm] = useState({
    name: trip?.name ?? '', description: trip?.description ?? '',
    type: trip?.type ?? 0, vehicleId: trip?.vehicleId ?? '', driverId: trip?.driverId ?? '',
    routeId: trip?.routeId ?? '', scheduledStartTime: trip?.scheduledStartTime ?? '',
    corridorEnabled: trip?.corridorEnabled ?? false,
    corridorBufferMeters: trip?.corridorBufferMeters ?? 500,
    deviationThresholdMinutes: trip?.deviationThresholdMinutes ?? 10,
  });
  const [vehicles, setVehicles] = useState<any[]>([]);
  const [drivers, setDrivers] = useState<any[]>([]);
  const [routes, setRoutes] = useState<any[]>([]);
  const [waypoints, setWaypoints] = useState<TripWaypoint[]>(() => trip?.waypoints?.map(w => ({ ...w })) ?? []);
  const [links, setLinks] = useState<TripGeofenceRow[]>([]);
  const [options, setOptions] = useState<GeofenceOption[]>([]);
  const [optionsSearch, setOptionsSearch] = useState('');
  const [linksLoaded, setLinksLoaded] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [showMap, setShowMap] = useState(false);
  // View-only extras
  const [live, setLive] = useState<LivePosition | null>(null);
  const [replay, setReplay] = useState<ReplayPoint[] | null>(null);
  const [detail, setDetail] = useState<TripDetail | null>(null);
  const [reasonPrompt, setReasonPrompt] = useState<{ target: number; label: string } | null>(null);
  const [reasonText, setReasonText] = useState('');

  const set = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }));
  const readonly = isView || !canEdit;

  // Load pick-lists + geofence options; for an existing trip, its detail + links.
  useEffect(() => {
    let alive = true;
    api.get('/vehicles?pageSize=100').then(r => { if (alive) setVehicles(r.data.data?.items ?? []); }).catch(() => {});
    api.get('/drivers?pageSize=100').then(r => { if (alive) setDrivers(r.data.data?.items ?? []); }).catch(() => {});
    api.get('/routes?pageSize=100').then(r => { if (alive) setRoutes(r.data.data?.items ?? []); }).catch(() => {});
    api.get('/geofences?pageSize=100').then(r => {
      if (alive) setOptions((r.data.data?.items ?? []).map((g: any) => ({
        id: g.id, name: g.name, type: g.type, typeName: g.typeName, companyName: g.companyName,
        geometry: g.geometry, centerLatitude: g.centerLatitude, centerLongitude: g.centerLongitude, radius: g.radius,
      })));
    }).catch(() => {});
    if (trip) {
      api.get(`/trips/${trip.id}`).then(r => { if (alive) setDetail(r.data.data); }).catch(() => {});
      api.get(`/trips/${trip.id}/geofences`).then(r => { if (alive) { setLinks(r.data.data ?? []); setLinksLoaded(true); } }).catch(() => setLinksLoaded(true));
      api.get(`/trips/${trip.id}/live`).then(r => { if (alive) setLive(r.data.data); }).catch(() => {});
    } else setLinksLoaded(true);
    return () => { alive = false; };
  }, [trip?.id]);

  const refreshViewData = useCallback(() => {
    if (!trip) return;
    api.get(`/trips/${trip.id}`).then(r => setDetail(r.data.data)).catch(() => {});
    api.get(`/trips/${trip.id}/live`).then(r => setLive(r.data.data)).catch(() => {});
  }, [trip]);

  // ── Waypoint editor ────────────────────────────────────
  const updateWaypoints = (next: TripWaypoint[]) => {
    setWaypoints(next.map((w, i) => ({ ...w, sequenceOrder: i + 1 })));
  };
  const addWaypoint = () => {
    const last = waypoints[waypoints.length - 1];
    const isReturn = form.type === 1 && waypoints.length > 0 && last?.legType === 1;
    updateWaypoints([...waypoints, {
      sequenceOrder: waypoints.length + 1,
      legType: form.type === 1 ? (isReturn ? 1 : 0) : 0,
      waypointType: 5,
      name: `Stop ${waypoints.length + 1}`, latitude: 0, longitude: 0,
    }]);
  };
  const patchWaypoint = (i: number, patch: Partial<TripWaypoint>) => {
    const n = [...waypoints]; n[i] = { ...n[i], ...patch }; updateWaypoints(n);
  };

  // ── Geofence linking ───────────────────────────────────
  const linkChange = (index: number, patch: Partial<TripGeofenceRow>) =>
    setLinks(ls => ls.map((l, i) => (i === index ? { ...l, ...patch } : l)));
  const removeLink = (index: number) => setLinks(ls => ls.filter((_, i) => i !== index));
  const addLink = (geofenceId: string) => {
    const opt = options.find(o => o.id === geofenceId);
    if (!opt) return;
    if (links.some(l => l.geofenceId === geofenceId)) { setError('That geofence is already linked to this trip — pick a single role for it.'); return; }
    const nextSeq = links.filter(l => l.role === 0).length + 1;
    setLinks(ls => [...ls, {
      id: '', tripId: trip?.id ?? '', geofenceId: opt.id, geofenceName: opt.name,
      geofenceType: opt.type, geofenceTypeName: opt.typeName, role: 0, roleName: 'Checkpoint', sequenceOrder: nextSeq,
      geometry: opt.geometry, centerLatitude: opt.centerLatitude, centerLongitude: opt.centerLongitude, radius: opt.radius,
    }]);
    setError('');
  };

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Name is required'); return; }
    if (!form.vehicleId) { setError('Select a vehicle'); return; }
    if (!form.driverId) { setError('Select a driver'); return; }
    if (waypoints.length < 2) { setError('A trip needs at least an origin and a destination waypoint.'); return; }
    if (waypoints.some(w => !isFinite(w.latitude) || !isFinite(w.longitude) || (w.latitude === 0 && w.longitude === 0))) {
      setError('Every waypoint needs a valid latitude/longitude (use 0,0 only for a real origin).');
      return;
    }
    if (!trip && isCrossTenant && needsPick) { setError('Select the company this trip belongs to'); return; }

    // Link validation (server re-checks): unique geofence, no duplicate checkpoint sequence.
    const seen = new Set<string>();
    const seqs = new Set<number>();
    for (const l of links) {
      if (seen.has(l.geofenceId)) { setError('A geofence can only be linked once per trip — pick a single role for it.'); return; }
      seen.add(l.geofenceId);
      if (l.role === 0 && l.sequenceOrder != null) {
        if (seqs.has(l.sequenceOrder)) { setError(`Duplicate checkpoint sequence number ${l.sequenceOrder}.`); return; }
        seqs.add(l.sequenceOrder);
      }
    }

    setSaving(true); setError('');
    const wpPayload = waypoints.map(w => ({
      sequenceOrder: w.sequenceOrder, legType: w.legType, waypointType: w.waypointType,
      name: w.name, latitude: w.latitude, longitude: w.longitude, address: w.address ?? null,
      expectedArrival: w.expectedArrival ? new Date(w.expectedArrival).toISOString() : null,
      linkedGeofenceId: w.linkedGeofenceId ?? null,
    }));
    const payload: Record<string, unknown> = {
      name: form.name.trim(), description: form.description || null,
      type: form.type, vehicleId: form.vehicleId, driverId: form.driverId,
      routeId: form.routeId || null, scheduledStartTime: form.scheduledStartTime ? new Date(form.scheduledStartTime).toISOString() : null,
      waypoints: wpPayload,
      corridorEnabled: form.corridorEnabled,
      corridorBufferMeters: form.corridorEnabled ? form.corridorBufferMeters : null,
      deviationThresholdMinutes: form.corridorEnabled ? form.deviationThresholdMinutes : null,
    };
    try {
      let id = trip?.id;
      if (trip) {
        await api.put(`/trips/${trip.id}`, payload);
      } else {
        const res = await api.post('/trips', { ...payload, ...(isCrossTenant ? { companyId: targetCompanyId } : {}) });
        id = res.data.data?.id;
        if (!id) { setError('Trip was created but the id could not be read.'); return; }
      }
      if (linksLoaded && id) {
        const body = links.map(l => ({ geofenceId: l.geofenceId, role: l.role, sequenceOrder: l.role === 0 ? (l.sequenceOrder ?? null) : null }));
        await api.put(`/trips/${id}/geofences`, body);
      }
      onSaved();
    } catch (err: any) { setError(err.response?.data?.message ?? 'Failed to save trip'); }
    setSaving(false);
  };

  const changeStatus = async (status: number, reason?: string) => {
    if (!trip) return;
    setError('');
    try {
      await api.post(`/trips/${trip.id}/status`, { status, reason: reason ?? null, source: 'manual' });
      setReasonPrompt(null); setReasonText('');
      refreshViewData();
      onSavedRef.current();
    } catch (err: any) { setError(err.response?.data?.message ?? 'Status change failed'); }
  };
  // Keep a stable reference so refresh + list refresh both happen after status change.
  const onSavedRef = useRef(onSaved);
  onSavedRef.current = onSaved;

  const markArrived = async (wp: TripWaypoint) => {
    if (!trip || !wp.id) return;
    try {
      await api.post(`/trips/${trip.id}/waypoints/${wp.id}/arrive`, {});
      refreshViewData();
    } catch (err: any) { setError(err.response?.data?.message ?? 'Failed to record arrival'); }
  };

  const loadReplay = async () => {
    if (!trip) return;
    try { const r = await api.get(`/trips/${trip.id}/replay`); setReplay(r.data.data ?? []); } catch { setError('Replay data unavailable'); }
  };

  const d = detail ?? trip;
  const fencesForMap = (d?.tripGeofences ?? links).map(l => ({
    geofenceId: l.geofenceId, geofenceName: l.geofenceName, role: l.role,
    geometry: l.geometry, centerLatitude: l.centerLatitude, centerLongitude: l.centerLongitude, radius: l.radius,
  }));
  const mapPath = waypoints.length > 0
    ? waypoints.filter(w => isFinite(w.latitude) && isFinite(w.longitude)).map(w => ({ lat: w.latitude, lng: w.longitude }))
    : (d?.waypoints ?? []).filter(w => isFinite(w.latitude) && isFinite(w.longitude)).map(w => ({ lat: w.latitude, lng: w.longitude }));
  const visibleOptions = options.filter(o =>
    o.name.toLowerCase().includes(optionsSearch.toLowerCase())
    && !links.some(l => l.geofenceId === o.id));

  const nextTransitions: { target: number; label: string; icon: any; color: string; needsReason?: boolean }[] = (() => {
    const s = d?.status ?? 0;
    if (s === 0) return [
      { target: 1, label: 'Schedule', icon: Calendar, color: 'bg-blue-600 hover:bg-blue-700 text-white' },
      { target: 4, label: 'Cancel', icon: Ban, color: 'bg-gray-200 hover:bg-gray-300 text-gray-700', needsReason: true },
    ];
    if (s === 1) return [
      { target: 2, label: 'Start Trip', icon: Play, color: 'bg-green-600 hover:bg-green-700 text-white' },
      { target: 4, label: 'Cancel', icon: Ban, color: 'bg-gray-200 hover:bg-gray-300 text-gray-700', needsReason: true },
    ];
    if (s === 2) return [
      { target: 3, label: 'Complete', icon: CheckCircle2, color: 'bg-purple-600 hover:bg-purple-700 text-white' },
      { target: 5, label: 'Abort', icon: X, color: 'bg-orange-500 hover:bg-orange-600 text-white', needsReason: true },
    ];
    return [];
  })();

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-xl w-full max-w-3xl max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-6 border-b">
          <h2 className="text-lg font-semibold">{isView ? 'Trip Details' : trip ? 'Edit Trip' : 'Create Trip'}</h2>
          <button onClick={onClose} className="p-2 rounded-lg hover:bg-gray-100"><X className="w-4 h-4" /></button>
        </div>

        {error && <div className="mx-6 mt-4 bg-red-50 border border-red-200 text-red-700 px-4 py-2 rounded-lg text-sm">{error}</div>}

        {isView && d ? (
          <div className="p-6 space-y-6">
            {/* Status actions */}
            {nextTransitions.length > 0 && (
              <div className="flex flex-wrap items-center gap-2 bg-gray-50 rounded-lg p-3">
                <span className="text-xs font-medium text-gray-500 mr-1">Trip actions:</span>
                {nextTransitions.map(nt => (
                  <button key={nt.target} onClick={() => nt.needsReason ? setReasonPrompt({ target: nt.target, label: nt.label }) : changeStatus(nt.target)}
                    className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium ${nt.color}`}>
                    <nt.icon className="w-3.5 h-3.5" /> {nt.label}
                  </button>
                ))}
              </div>
            )}

            <div className="grid grid-cols-2 gap-4">
              <div><p className="text-xs text-gray-500">Trip Name</p><p className="font-medium">{d.name}</p></div>
              <div><p className="text-xs text-gray-500">Company</p><p className="font-medium">{d.companyName}</p></div>
              <div><p className="text-xs text-gray-500">Status</p>
                <span className={`px-2 py-1 rounded-full text-xs font-medium ${(STATUS_MAP[d.status] ?? STATUS_MAP[0]).color}`}>{(STATUS_MAP[d.status] ?? STATUS_MAP[0]).label}</span>
                {d.isDelayed && d.status !== 3 && (
                  <span className="ml-2 px-2 py-1 rounded-full text-xs font-medium bg-amber-100 text-amber-700" title={d.delayReason ?? ''}>Delayed</span>
                )}
              </div>
              <div><p className="text-xs text-gray-500">Type</p><span className={`px-2 py-1 rounded-full text-xs font-medium ${(TYPE_MAP[d.type] ?? TYPE_MAP[0]).color}`}>{(TYPE_MAP[d.type] ?? TYPE_MAP[0]).label}</span></div>
              <div><p className="text-xs text-gray-500">Vehicle</p><p className="font-medium">{d.vehicleName}</p></div>
              <div><p className="text-xs text-gray-500">Driver</p><p className="font-medium">{d.driverName}</p></div>
              <div><p className="text-xs text-gray-500">Route</p><p className="font-medium">{d.routeName ?? 'Dynamic route'}</p></div>
              <div><p className="text-xs text-gray-500">Scheduled Start</p><p className="font-medium">{fmtDate(d.scheduledStartTime)}</p></div>
            </div>
            {d.description && <div><p className="text-xs text-gray-500">Description</p><p className="text-sm text-gray-700">{d.description}</p></div>}
            {d.delayReason && d.status !== 3 && (
              <div className="bg-amber-50 border border-amber-200 rounded-lg px-3 py-2 text-xs text-amber-800"><strong>Delayed:</strong> {d.delayReason}</div>
            )}
            {d.cancelReason && (
              <div className="bg-gray-50 border border-gray-200 rounded-lg px-3 py-2 text-xs text-gray-700"><strong>Reason:</strong> {d.cancelReason}</div>
            )}

            {/* Live tracking */}
            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-2 flex items-center gap-1.5"><Radio className="w-4 h-4 text-gray-500" /> Live Position</h4>
              {live?.latitude != null && live.longitude != null ? (
                <div className="grid grid-cols-4 gap-3 text-xs">
                  <div><p className="text-gray-500">Lat</p><p className="font-medium">{live.latitude.toFixed(5)}</p></div>
                  <div><p className="text-gray-500">Lng</p><p className="font-medium">{live.longitude.toFixed(5)}</p></div>
                  <div><p className="text-gray-500">Speed</p><p className="font-medium">{live.speedKmh?.toFixed(0) ?? '—'} km/h</p></div>
                  <div><p className="text-gray-500">Updated</p><p className="font-medium">{fmtDate(live.updatedAt)}</p></div>
                </div>
              ) : <p className="text-xs text-gray-400">No live telemetry for this vehicle yet.</p>}
            </div>

            {/* Map */}
            <button type="button" onClick={() => setShowMap(s => !s)}
              className="flex items-center gap-1.5 text-xs text-blue-600 hover:underline">
              <MapPin className="w-3.5 h-3.5" /> {showMap ? 'Hide map' : 'Show trip on map'}
            </button>
            {showMap && <RouteMapPane waypoints={mapPath} routeGeometry={d.routeGeometry} fences={fencesForMap} />}

            {/* Waypoints */}
            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-2">Waypoints ({d.waypoints?.length ?? 0})</h4>
              {(d.waypoints ?? []).length === 0 ? <p className="text-xs text-gray-400">No waypoints.</p> : (
                <ul className="space-y-1.5">
                  {(d.waypoints ?? []).map(w => (
                    <li key={w.id ?? w.sequenceOrder} className="flex items-center gap-3 bg-white rounded-lg border px-3 py-2">
                      <span className="w-6 h-6 rounded-full bg-blue-100 text-blue-700 text-[10px] font-bold flex items-center justify-center shrink-0">{w.sequenceOrder}</span>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <p className="text-sm font-medium text-gray-800 truncate">{w.name}</p>
                          {d.type === 1 && (
                            <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${LEG_COLORS[w.legType]}`}>{LEG_LABELS[w.legType]}</span>
                          )}
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-gray-100 text-gray-600">{WAYPOINT_TYPE_LABELS[w.waypointType] ?? 'Other'}</span>
                        </div>
                        <p className="text-[10px] text-gray-400">
                          {w.latitude.toFixed(4)}, {w.longitude.toFixed(4)}
                          {w.expectedArrival ? ` · ETA ${fmtDate(w.expectedArrival)}` : ''}
                          {w.actualArrival ? ` · arrived ${fmtDate(w.actualArrival)}` : ''}
                        </p>
                      </div>
                      {d.status === 2 && !w.actualArrival && w.id && (
                        <button onClick={() => markArrived(w)} className="px-2 py-1 text-[10px] font-medium bg-green-100 text-green-700 rounded hover:bg-green-200">
                          Mark arrived
                        </button>
                      )}
                      {w.actualArrival && <CheckCircle2 className="w-4 h-4 text-green-600 shrink-0" />}
                    </li>
                  ))}
                </ul>
              )}
            </div>

            {/* Linked geofences */}
            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-2">Linked Geofences ({d.tripGeofences?.length ?? 0})</h4>
              {(d.tripGeofences ?? []).length === 0 ? <p className="text-xs text-gray-400">No geofences linked.</p> : (
                <ul className="space-y-2">
                  {(d.tripGeofences ?? []).map(l => (
                    <li key={l.id} className="flex items-center justify-between bg-white rounded-lg px-3 py-2 border">
                      <div className="flex items-center gap-2">
                        <span className={`px-2 py-0.5 rounded-full text-[10px] font-medium ${ROLE_COLORS[l.role] ?? ROLE_COLORS[0]}`}>{ROLE_LABELS[l.role] ?? l.roleName}</span>
                        <span className="text-sm font-medium text-gray-800">{l.geofenceName}</span>
                        {l.role === 0 && l.sequenceOrder != null && <span className="text-xs text-gray-500">stop #{l.sequenceOrder}</span>}
                        {l.visited && <span className="text-[10px] text-green-700 font-medium">✓ visited {l.visitedAt ? fmtDate(l.visitedAt) : ''}</span>}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            {/* Metrics */}
            <div className="grid grid-cols-4 gap-3 text-xs">
              <div className="bg-gray-50 rounded-lg p-3"><p className="text-gray-500">Distance</p><p className="font-bold text-base">{d.actualDistance ?? d.plannedDistance ?? '—'} km</p></div>
              <div className="bg-gray-50 rounded-lg p-3"><p className="text-gray-500">Duration</p><p className="font-bold text-base">{fmtDuration(d.actualDuration ?? d.plannedDuration)}</p></div>
              <div className="bg-gray-50 rounded-lg p-3"><p className="text-gray-500">Avg / Max Speed</p><p className="font-bold text-base">{d.averageSpeed ?? '—'} / {d.maxSpeed ?? '—'} km/h</p></div>
              <div className="bg-gray-50 rounded-lg p-3"><p className="text-gray-500">Fuel Used</p><p className="font-bold text-base">{d.fuelUsedLiters ?? '—'} L</p></div>
            </div>

            {/* Replay */}
            <div className="bg-gray-50 rounded-lg p-4">
              <div className="flex items-center justify-between mb-2">
                <h4 className="text-sm font-medium flex items-center gap-1.5"><History className="w-4 h-4 text-gray-500" /> Trip Replay</h4>
                <button onClick={loadReplay} className="text-xs text-blue-600 hover:underline">Load telemetry replay</button>
              </div>
              {replay && (
                replay.length === 0
                  ? <p className="text-xs text-gray-400">No telemetry recorded during this trip window.</p>
                  : <p className="text-xs text-gray-600"><strong>{replay.length}</strong> position samples from {fmtDate(replay[0]?.eventTimeUtc)} to {fmtDate(replay[replay.length - 1]?.eventTimeUtc)} — overlaid on the planned path above.</p>
              )}
            </div>

            {/* Status history */}
            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-2">Status History</h4>
              {(d.statusHistory ?? []).length === 0 ? <p className="text-xs text-gray-400">No history.</p> : (
                <ul className="space-y-1.5">
                  {(d.statusHistory ?? []).map((h, i) => (
                    <li key={i} className="text-xs flex items-center gap-2">
                      <span className="text-gray-400">{fmtDate(h.changedAt)}</span>
                      <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${(STATUS_MAP[h.fromStatus] ?? STATUS_MAP[0]).color}`}>{(STATUS_MAP[h.fromStatus] ?? STATUS_MAP[0]).label}</span>
                      <ArrowDown className="w-3 h-3 text-gray-300" />
                      <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${(STATUS_MAP[h.toStatus] ?? STATUS_MAP[0]).color}`}>{(STATUS_MAP[h.toStatus] ?? STATUS_MAP[0]).label}</span>
                      <span className="text-gray-500 truncate">{h.reason}{h.source !== 'manual' ? ` (${h.source})` : ''}</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        ) : (
          <div className="p-6 space-y-5">
            {!trip && <TargetCompanyField hook={tgt} error={error} />}

            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Trip Name *</label><input className={INPUT} value={form.name} onChange={e => set('name', e.target.value)} /></div>
              <div><label className={LABEL}>Type</label>
                <select className={INPUT} value={form.type} onChange={e => { set('type', Number(e.target.value)); }}>
                  <option value={0}>Single Trip</option>
                  <option value={1}>Round Trip</option>
                </select>
              </div>
            </div>
            <div><label className={LABEL}>Description</label><textarea className={INPUT} rows={2} value={form.description} onChange={e => set('description', e.target.value)} /></div>

            <div className="grid grid-cols-3 gap-4">
              <div><label className={LABEL}>Vehicle *</label>
                <select className={INPUT} value={form.vehicleId} onChange={e => set('vehicleId', e.target.value)}>
                  <option value="">Select vehicle…</option>
                  {vehicles.map(v => <option key={v.id} value={v.id}>{v.registrationNumber}</option>)}
                </select>
              </div>
              <div><label className={LABEL}>Driver *</label>
                <select className={INPUT} value={form.driverId} onChange={e => set('driverId', e.target.value)}>
                  <option value="">Select driver…</option>
                  {drivers.map(dv => <option key={dv.id} value={dv.id}>{dv.firstName} {dv.lastName}</option>)}
                </select>
              </div>
              <div><label className={LABEL}>Route (optional)</label>
                <select className={INPUT} value={form.routeId} onChange={e => set('routeId', e.target.value)}>
                  <option value="">Dynamic route (no template)</option>
                  {routes.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
                </select>
              </div>
            </div>
            <div><label className={LABEL}>Scheduled Start</label>
              <input type="datetime-local" className={INPUT} value={form.scheduledStartTime ? form.scheduledStartTime.slice(0, 16) : ''} onChange={e => set('scheduledStartTime', e.target.value)} />
            </div>

            {/* Waypoints */}
            <div className="bg-gray-50 rounded-lg p-4 space-y-3">
              <div className="flex items-center justify-between">
                <div>
                  <h4 className="text-sm font-medium">Waypoints</h4>
                  {form.type === 1 && <p className="text-[10px] text-gray-400">Round trip — mark the turnaround by switching later stops to <strong>Return</strong> leg.</p>}
                </div>
                <button type="button" onClick={addWaypoint} className="text-xs text-blue-600 hover:underline">+ Add waypoint</button>
              </div>
              {waypoints.length === 0 ? <p className="text-[11px] text-gray-400">Add at least an origin and a destination waypoint.</p> : (
                <div className="space-y-1.5">
                  {waypoints.map((w, i) => (
                    <div key={i} className="flex items-center gap-2 bg-white rounded-lg border px-2 py-1.5">
                      <span className="w-5 text-[10px] font-bold text-gray-400 text-center">{w.sequenceOrder}</span>
                      <input className="flex-1 min-w-[90px] px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Name" value={w.name}
                        onChange={e => patchWaypoint(i, { name: e.target.value })} />
                      {form.type === 1 && (
                        <select className="px-1.5 py-1 border border-gray-300 rounded text-[10px]" value={w.legType} onChange={e => patchWaypoint(i, { legType: Number(e.target.value) })}>
                          <option value={0}>Outbound</option><option value={1}>Return</option>
                        </select>
                      )}
                      <select className="px-1.5 py-1 border border-gray-300 rounded text-[10px]" value={w.waypointType} onChange={e => patchWaypoint(i, { waypointType: Number(e.target.value) })}>
                        {WAYPOINT_TYPE_LABELS.map((l, li) => <option key={li} value={li}>{l}</option>)}
                      </select>
                      <input className="w-20 px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Lat" type="number" step="any" value={w.latitude}
                        onChange={e => patchWaypoint(i, { latitude: Number(e.target.value) })} />
                      <input className="w-20 px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Lng" type="number" step="any" value={w.longitude}
                        onChange={e => patchWaypoint(i, { longitude: Number(e.target.value) })} />
                      <input className="w-36 px-2 py-1 border border-gray-300 rounded text-xs" placeholder="Expected arrival" type="datetime-local"
                        value={w.expectedArrival ? w.expectedArrival.slice(0, 16) : ''}
                        onChange={e => patchWaypoint(i, { expectedArrival: e.target.value ? new Date(e.target.value).toISOString() : null })} />
                      <button type="button" disabled={i === 0} onClick={() => { const n = [...waypoints]; [n[i - 1], n[i]] = [n[i], n[i - 1]]; updateWaypoints(n); }}
                        className="p-1 text-gray-400 hover:text-gray-700 disabled:opacity-30"><ArrowUp className="w-3.5 h-3.5" /></button>
                      <button type="button" disabled={i === waypoints.length - 1} onClick={() => { const n = [...waypoints]; [n[i + 1], n[i]] = [n[i], n[i + 1]]; updateWaypoints(n); }}
                        className="p-1 text-gray-400 hover:text-gray-700 disabled:opacity-30"><ArrowDown className="w-3.5 h-3.5" /></button>
                      <button type="button" onClick={() => updateWaypoints(waypoints.filter((_, j) => j !== i))}
                        className="p-1 text-gray-300 hover:text-red-500"><X className="w-3.5 h-3.5" /></button>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Map */}
            <button type="button" onClick={() => setShowMap(s => !s)}
              className="flex items-center gap-1.5 text-xs text-blue-600 hover:underline">
              <MapPin className="w-3.5 h-3.5" /> {showMap ? 'Hide map' : 'Show trip on map'}
            </button>
            {showMap && <RouteMapPane waypoints={mapPath} routeGeometry={null} fences={fencesForMap} />}

            {/* Geofence linking (mandatory for scheduling) */}
            <div className="bg-gray-50 rounded-lg p-4 space-y-3">
              <div className="flex items-center justify-between">
                <h4 className="text-sm font-medium flex items-center gap-1.5"><Layers className="w-4 h-4 text-gray-500" /> Linked Geofences</h4>
                <span className="text-[10px] text-gray-400">checkpoint / restricted / start / end</span>
              </div>
              {links.length === 0 && (
                <p className="text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded-lg px-3 py-2">
                  At least one geofence is required before this trip can be <strong>scheduled</strong> (directly or via its linked route).
                </p>
              )}
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
            </div>

            {/* Corridor config */}
            <div className="flex items-center gap-2">
              <input type="checkbox" checked={form.corridorEnabled} onChange={e => set('corridorEnabled', e.target.checked)} className="rounded" />
              <label className="text-sm text-gray-700">Corridor deviation alerts</label>
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
          </div>
        )}

        <div className="flex justify-end gap-3 p-6 border-t">
          <button onClick={onClose} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Close</button>
          {!readonly && (
            <button onClick={handleSubmit} disabled={saving} className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
              {saving ? 'Saving...' : trip ? 'Update Trip' : 'Create Trip (draft)'}
            </button>
          )}
        </div>
      </div>

      {/* Reason prompt (cancel / abort) */}
      {reasonPrompt && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-[60]" onClick={() => setReasonPrompt(null)}>
          <div className="bg-white rounded-xl p-6 w-full max-w-md" onClick={e => e.stopPropagation()}>
            <h3 className="text-lg font-semibold">{reasonPrompt.label} Trip</h3>
            <p className="text-gray-600 mt-1 text-sm">A reason is required — dispatchers need to know why this trip did not complete.</p>
            <textarea className={`${INPUT} mt-3`} rows={3} placeholder="Reason…" value={reasonText} onChange={e => setReasonText(e.target.value)} />
            <div className="flex justify-end gap-3 mt-4">
              <button onClick={() => setReasonPrompt(null)} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Cancel</button>
              <button onClick={() => reasonText.trim() ? changeStatus(reasonPrompt.target, reasonText.trim()) : setError('Reason is required')}
                className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700">
                Confirm
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}