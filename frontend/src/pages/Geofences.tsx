import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import type { PagedResult } from '../lib/api';
import {
  Search, Plus, Edit, Trash2, ChevronLeft, ChevronRight, ChevronUp, ChevronDown,
  Eye, X, MapPin, Circle, Square, Hexagon, AlertTriangle, Shield, Target,
} from 'lucide-react';

// ── Types ────────────────────────────────────────────────
interface GeofenceDetail {
  id: string; name: string; description?: string;
  type: number; typeName: string; status: number; companyName: string;
  coordinates: string; centerLatitude?: number; centerLongitude?: number; radius?: number;
  fillColor?: string; borderColor?: string; borderWidth?: number;
  alertOnEntry: boolean; alertOnExit: boolean; alertOnDwell: boolean; dwellTimeMinutes?: number;
  assignedVehicleCount: number; violationCount: number; createdAt: string;
}

interface GeofenceStats {
  total: number; active: number; inactive: number;
  circles: number; rectangles: number; polygons: number;
  totalAssignments: number;
}

// ── Constants ────────────────────────────────────────────
const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

const STATUS_MAP: Record<number, { label: string; color: string }> = {
  0: { label: 'Active', color: 'bg-green-100 text-green-700' },
  1: { label: 'Inactive', color: 'bg-gray-100 text-gray-700' },
  2: { label: 'Pending', color: 'bg-yellow-100 text-yellow-700' },
  3: { label: 'Suspended', color: 'bg-red-100 text-red-700' },
  4: { label: 'Archived', color: 'bg-gray-100 text-gray-500' },
};

const TYPE_MAP: Record<number, { label: string; color: string; icon: any }> = {
  0: { label: 'Circle', color: 'bg-blue-100 text-blue-700', icon: Circle },
  1: { label: 'Rectangle', color: 'bg-purple-100 text-purple-700', icon: Square },
  2: { label: 'Polygon', color: 'bg-orange-100 text-orange-700', icon: Hexagon },
};

type SortField = 'name' | 'type' | 'status' | 'assignedVehicleCount';
interface SortState { field: SortField; desc: boolean; }

const STATUS_FILTERS: { key: string; label: string; value?: number; color: string; statKey?: string }[] = [
  { key: 'all', label: 'All', color: 'bg-blue-100 text-blue-700', statKey: 'total' },
  { key: '0', label: 'Active', value: 0, color: 'bg-green-100 text-green-700', statKey: 'active' },
  { key: '1', label: 'Inactive', value: 1, color: 'bg-gray-100 text-gray-700', statKey: 'inactive' },
];

// ── Main Component ───────────────────────────────────────
export default function GeofencesPage() {
  const { hasPermission } = useAuth();
  const canCreate = hasPermission('geofence.create');
  const canEdit = hasPermission('geofence.edit');
  const canDelete = hasPermission('geofence.delete');

  const [data, setData] = useState<PagedResult<GeofenceDetail> | null>(null);
  const [stats, setStats] = useState<GeofenceStats | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState('all');
  const [sort, setSort] = useState<SortState>({ field: 'name', desc: false });
  const [modal, setModal] = useState<{ open: boolean; edit?: GeofenceDetail; view?: GeofenceDetail }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<GeofenceDetail | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search });
      if (statusFilter !== 'all') params.set('status', statusFilter);
      const res = await api.get(`/geofences?${params}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, statusFilter]);

  const fetchStats = useCallback(async () => {
    try { const res = await api.get('/geofences/stats'); setStats(res.data.data); } catch { /* ignore */ }
  }, []);

  useEffect(() => { fetchData(); }, [fetchData]);
  useEffect(() => { fetchStats(); }, [fetchStats]);

  const handleDelete = async (g: GeofenceDetail) => {
    try { await api.delete(`/geofences/${g.id}`); setDeleteConfirm(null); fetchData(); fetchStats(); } catch (err) { console.error(err); }
  };

  const items = data?.items ?? [];
  const sorted = [...items].sort((a, b) => {
    const mul = sort.desc ? -1 : 1;
    switch (sort.field) {
      case 'name': return mul * a.name.localeCompare(b.name);
      case 'type': return mul * (a.type - b.type);
      case 'status': return mul * (a.status - b.status);
      case 'assignedVehicleCount': return mul * (a.assignedVehicleCount - b.assignedVehicleCount);
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
        <div><h1 className="text-2xl font-bold text-gray-900">Geofences</h1><p className="text-gray-500 text-sm mt-1">Define and manage geographic boundaries for your fleet</p></div>
        {canCreate && (
          <button onClick={() => setModal({ open: true })} className="flex items-center gap-2 bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 text-sm">
            <Plus className="w-4 h-4" /> Add Geofence
          </button>
        )}
      </div>

      {/* Stats */}
      {stats && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          {[
            { label: 'Total Geofences', value: stats.total, icon: MapPin, color: 'text-blue-600 bg-blue-100' },
            { label: 'Active', value: stats.active, icon: Shield, color: 'text-green-600 bg-green-100' },
            { label: 'Vehicle Assignments', value: stats.totalAssignments, icon: Target, color: 'text-purple-600 bg-purple-100' },
            { label: 'Inactive', value: stats.inactive, icon: AlertTriangle, color: 'text-red-600 bg-red-100' },
          ].map(s => (
            <div key={s.label} className="bg-white rounded-xl border p-4 flex items-center gap-3">
              <div className={`p-2 rounded-lg ${s.color}`}><s.icon className="w-5 h-5" /></div>
              <div><p className="text-xs text-gray-500">{s.label}</p><p className="text-lg font-bold text-gray-900">{s.value}</p></div>
            </div>
          ))}
        </div>
      )}

      {/* Type breakdown */}
      {stats && (
        <div className="flex gap-4">
          {[
            { label: 'Circles', count: stats.circles, icon: Circle, color: 'text-blue-600' },
            { label: 'Rectangles', count: stats.rectangles, icon: Square, color: 'text-purple-600' },
            { label: 'Polygons', count: stats.polygons, icon: Hexagon, color: 'text-orange-600' },
          ].map(t => (
            <div key={t.label} className="flex items-center gap-2 text-sm">
              <t.icon className={`w-4 h-4 ${t.color}`} />
              <span className="font-medium">{t.count}</span>
              <span className="text-gray-500">{t.label}</span>
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
            placeholder="Search geofences..." className={`${INPUT} pl-10`} />
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-xl border overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                {[
                  { label: 'Geofence', field: 'name' as SortField },
                  { label: 'Type', field: 'type' as SortField },
                  { label: 'Location', field: null },
                  { label: 'Alerts', field: null },
                  { label: 'Vehicles', field: 'assignedVehicleCount' as SortField },
                  { label: 'Status', field: 'status' as SortField },
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
                <tr><td colSpan={7} className="px-4 py-12 text-center text-gray-500">Loading geofences...</td></tr>
              ) : sorted.length === 0 ? (
                <tr><td colSpan={7} className="px-4 py-12 text-center text-gray-500">No geofences found</td></tr>
              ) : sorted.map(g => {
                const typeInfo = TYPE_MAP[g.type] ?? TYPE_MAP[0];
                const Icon = typeInfo.icon;
                return (
                  <tr key={g.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-10 h-10 rounded-lg flex items-center justify-center" style={{ backgroundColor: g.fillColor ?? '#f3f4f6', borderColor: g.borderColor ?? '#d1d5db' }}>
                          <Icon className="w-5 h-5" style={{ color: g.borderColor ?? '#6b7280' }} />
                        </div>
                        <div>
                          <p className="font-medium text-gray-900">{g.name}</p>
                          {g.description && <p className="text-xs text-gray-500 truncate max-w-[200px]">{g.description}</p>}
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`px-2 py-1 rounded-full text-xs font-medium ${typeInfo.color}`}>{typeInfo.label}</span>
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-600">
                      {g.centerLatitude != null && g.centerLongitude != null ? (
                        <span>{g.centerLatitude.toFixed(3)}, {g.centerLongitude.toFixed(3)}</span>
                      ) : '—'}
                      {g.radius && <span className="ml-1 text-gray-400">(R: {g.radius}m)</span>}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex gap-1">
                        {g.alertOnEntry && <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-red-100 text-red-700">Entry</span>}
                        {g.alertOnExit && <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-orange-100 text-orange-700">Exit</span>}
                        {g.alertOnDwell && <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-yellow-100 text-yellow-700">Dwell</span>}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-xs font-medium text-gray-900">{g.assignedVehicleCount}</td>
                    <td className="px-4 py-3">
                      <span className={`px-2 py-1 rounded-full text-xs font-medium ${(STATUS_MAP[g.status] ?? STATUS_MAP[0]).color}`}>
                        {(STATUS_MAP[g.status] ?? STATUS_MAP[0]).label}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button onClick={() => setModal({ open: true, view: g })} className="p-1.5 rounded-lg text-gray-400 hover:text-blue-600 hover:bg-blue-50" title="View">
                          <Eye className="w-4 h-4" />
                        </button>
                        {canEdit && (
                          <button onClick={() => setModal({ open: true, edit: g })} className="p-1.5 rounded-lg text-gray-400 hover:text-amber-600 hover:bg-amber-50" title="Edit">
                            <Edit className="w-4 h-4" />
                          </button>
                        )}
                        {canDelete && (
                          <button onClick={() => setDeleteConfirm(g)} className="p-1.5 rounded-lg text-gray-400 hover:text-red-600 hover:bg-red-50" title="Delete">
                            <Trash2 className="w-4 h-4" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
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
            <h3 className="text-lg font-semibold">Delete Geofence</h3>
            <p className="text-gray-600 mt-2">Are you sure you want to delete <strong>{deleteConfirm.name}</strong>? This will also remove all vehicle assignments.</p>
            <div className="flex justify-end gap-3 mt-6">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm)} className="px-4 py-2 text-sm bg-red-600 text-white rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {/* View / Create / Edit Modal */}
      {modal.open && <GeofenceModal geofence={modal.edit || modal.view} isView={!!modal.view && !modal.edit} onClose={() => setModal({ open: false })}
        onSaved={() => { setModal({ open: false }); fetchData(); fetchStats(); }} canEdit={canEdit} />}
    </div>
  );
}

// ── Geofence Modal ───────────────────────────────────────
function GeofenceModal({ geofence, isView, onClose, onSaved, canEdit }: {
  geofence?: GeofenceDetail; isView: boolean; onClose: () => void; onSaved: () => void; canEdit: boolean;
}) {
  const [form, setForm] = useState({
    name: geofence?.name ?? '', description: geofence?.description ?? '',
    type: geofence?.type ?? 0,
    coordinates: geofence?.coordinates ?? '[]', centerLatitude: geofence?.centerLatitude ?? 0, centerLongitude: geofence?.centerLongitude ?? 0,
    radius: geofence?.radius ?? 500, fillColor: geofence?.fillColor ?? '#4CAF5033', borderColor: geofence?.borderColor ?? '#4CAF50', borderWidth: geofence?.borderWidth ?? 2,
    alertOnEntry: geofence?.alertOnEntry ?? true, alertOnExit: geofence?.alertOnExit ?? true, alertOnDwell: geofence?.alertOnDwell ?? false,
    dwellTimeMinutes: geofence?.dwellTimeMinutes ?? 10,
    status: geofence?.status ?? 0,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const set = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }));

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Name is required'); return; }
    setSaving(true); setError('');
    try {
      if (geofence) { await api.put(`/geofences/${geofence.id}`, form); }
      else { await api.post('/geofences', form); }
      onSaved();
    } catch (err: any) { setError(err.response?.data?.message ?? 'Failed to save geofence'); }
    setSaving(false);
  };

  const readonly = isView || !canEdit;

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div className="bg-white rounded-xl w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-6 border-b">
          <h2 className="text-lg font-semibold">{isView ? 'Geofence Details' : geofence ? 'Edit Geofence' : 'Create Geofence'}</h2>
          <button onClick={onClose} className="p-2 rounded-lg hover:bg-gray-100"><X className="w-4 h-4" /></button>
        </div>

        {isView && geofence ? (
          <div className="p-6 space-y-6">
            <div className="grid grid-cols-2 gap-4">
              <div><p className="text-xs text-gray-500">Name</p><p className="font-medium">{geofence.name}</p></div>
              <div><p className="text-xs text-gray-500">Company</p><p className="font-medium">{geofence.companyName}</p></div>
              <div><p className="text-xs text-gray-500">Type</p><span className={`px-2 py-1 rounded-full text-xs font-medium ${(TYPE_MAP[geofence.type] ?? TYPE_MAP[0]).color}`}>{(TYPE_MAP[geofence.type] ?? TYPE_MAP[0]).label}</span></div>
              <div><p className="text-xs text-gray-500">Status</p><span className={`px-2 py-1 rounded-full text-xs font-medium ${(STATUS_MAP[geofence.status] ?? STATUS_MAP[0]).color}`}>{(STATUS_MAP[geofence.status] ?? STATUS_MAP[0]).label}</span></div>
              <div><p className="text-xs text-gray-500">Center</p><p className="font-medium">{geofence.centerLatitude?.toFixed(4)}, {geofence.centerLongitude?.toFixed(4)}</p></div>
              {geofence.radius && <div><p className="text-xs text-gray-500">Radius</p><p className="font-medium">{geofence.radius}m</p></div>}
            </div>
            {geofence.description && <div><p className="text-xs text-gray-500">Description</p><p className="text-sm text-gray-700">{geofence.description}</p></div>}

            <div className="bg-gray-50 rounded-lg p-4">
              <h4 className="text-sm font-medium mb-3">Alert Configuration</h4>
              <div className="grid grid-cols-3 gap-4">
                <div className="flex items-center gap-2"><div className={`w-3 h-3 rounded-full ${geofence.alertOnEntry ? 'bg-red-500' : 'bg-gray-300'}`} /><span className="text-xs">Entry Alert</span></div>
                <div className="flex items-center gap-2"><div className={`w-3 h-3 rounded-full ${geofence.alertOnExit ? 'bg-orange-500' : 'bg-gray-300'}`} /><span className="text-xs">Exit Alert</span></div>
                <div className="flex items-center gap-2"><div className={`w-3 h-3 rounded-full ${geofence.alertOnDwell ? 'bg-yellow-500' : 'bg-gray-300'}`} /><span className="text-xs">Dwell Alert {geofence.dwellTimeMinutes && `(${geofence.dwellTimeMinutes}m)`}</span></div>
              </div>
            </div>

            <div className="grid grid-cols-3 gap-4 text-xs">
              <div><p className="text-gray-500">Assigned Vehicles</p><p className="font-bold text-lg">{geofence.assignedVehicleCount}</p></div>
              <div><p className="text-gray-500">Violations</p><p className="font-bold text-lg">{geofence.violationCount}</p></div>
              <div><p className="text-gray-500">Border</p><div className="flex items-center gap-2"><div className="w-6 h-4 rounded border-2" style={{ backgroundColor: geofence.fillColor, borderColor: geofence.borderColor, borderWidth: geofence.borderWidth }} /><span>{geofence.borderWidth}px</span></div></div>
            </div>
          </div>
        ) : (
          <div className="p-6 space-y-4">
            {error && <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-2 rounded-lg text-sm">{error}</div>}

            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Name *</label><input className={INPUT} value={form.name} onChange={e => set('name', e.target.value)} /></div>
              <div><label className={LABEL}>Type</label>
                <select className={INPUT} value={form.type} onChange={e => set('type', Number(e.target.value))}>
                  <option value={0}>Circle</option><option value={1}>Rectangle</option><option value={2}>Polygon</option>
                </select>
              </div>
            </div>
            <div><label className={LABEL}>Description</label><textarea className={INPUT} rows={2} value={form.description} onChange={e => set('description', e.target.value)} /></div>

            <div className="grid grid-cols-3 gap-4">
              <div><label className={LABEL}>Center Latitude</label><input type="number" step="any" className={INPUT} value={form.centerLatitude} onChange={e => set('centerLatitude', Number(e.target.value))} /></div>
              <div><label className={LABEL}>Center Longitude</label><input type="number" step="any" className={INPUT} value={form.centerLongitude} onChange={e => set('centerLongitude', Number(e.target.value))} /></div>
              <div><label className={LABEL}>Radius (m)</label><input type="number" className={INPUT} value={form.radius} onChange={e => set('radius', Number(e.target.value))} /></div>
            </div>

            <div className="grid grid-cols-3 gap-4">
              <div><label className={LABEL}>Fill Color</label><input type="color" className={INPUT} value={form.fillColor?.slice(0, 7) ?? '#4CAF50'} onChange={e => set('fillColor', `${e.target.value}33`)} /></div>
              <div><label className={LABEL}>Border Color</label><input type="color" className={INPUT} value={form.borderColor ?? '#4CAF50'} onChange={e => set('borderColor', e.target.value)} /></div>
              <div><label className={LABEL}>Border Width</label><input type="number" min={1} max={10} className={INPUT} value={form.borderWidth} onChange={e => set('borderWidth', Number(e.target.value))} /></div>
            </div>

            <div className="bg-gray-50 rounded-lg p-4 space-y-3">
              <h4 className="text-sm font-medium">Alert Settings</h4>
              <div className="flex items-center gap-6">
                <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.alertOnEntry} onChange={e => set('alertOnEntry', e.target.checked)} className="rounded" />On Entry</label>
                <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.alertOnExit} onChange={e => set('alertOnExit', e.target.checked)} className="rounded" />On Exit</label>
                <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={form.alertOnDwell} onChange={e => set('alertOnDwell', e.target.checked)} className="rounded" />On Dwell</label>
                {form.alertOnDwell && <input type="number" min={1} className="w-20 px-2 py-1 border rounded text-sm" value={form.dwellTimeMinutes} onChange={e => set('dwellTimeMinutes', Number(e.target.value))} placeholder="min" />}
              </div>
            </div>
          </div>
        )}

        <div className="flex justify-end gap-3 p-6 border-t">
          <button onClick={onClose} className="px-4 py-2 text-sm border rounded-lg hover:bg-gray-50">Close</button>
          {!readonly && (
            <button onClick={handleSubmit} disabled={saving} className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
              {saving ? 'Saving...' : geofence ? 'Update Geofence' : 'Create Geofence'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
