import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { Search, Plus, Pencil, Trash2, ChevronLeft, ChevronRight, Package, X, Check, DollarSign, Users, Truck } from 'lucide-react';

interface PackageItem {
  id: string; name: string; description?: string; price: number; currency: string; billingCycle: string;
  status: number; displayOrder: number; isDefault: boolean; isCustom: boolean;
  maxUsers: number; maxVehicles: number; maxDrivers: number;
  storageLimitMb: number; maxApiCallsPerDay: number; maxTrackingDevices: number;
  maxAlertRules: number; maxGeofences: number; activeSubscriptions: number; createdAt: string;
}

interface PagedData { items: PackageItem[]; totalCount: number; page: number; pageSize: number; totalPages: number; hasPrevious: boolean; hasNext: boolean; }

export default function Packages() {
  const [data, setData] = useState<PagedData | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState<{ open: boolean; edit?: PackageItem }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<PackageItem | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api.get(`/admin/packages?page=${page}&pageSize=10&search=${search}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const handleDelete = async (id: string) => {
    try { await api.delete(`/admin/packages/${id}`); setDeleteConfirm(null); fetchData(); }
    catch (e: any) { alert(e.response?.data?.message || 'Failed to delete package'); }
  };

  const input = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
  const label = "block text-sm font-medium text-gray-700 mb-1";

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-gray-900">Packages</h2>
        <p className="text-gray-500 text-sm mt-1">Manage subscription packages and pricing</p>
      </div>

      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search packages..." />
        </div>
        <button onClick={() => setModal({ open: true })}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
          <Plus className="w-4 h-4" /> Create Package
        </button>
      </div>

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Package</th>
                <th className="text-right px-4 py-3 text-xs font-medium text-gray-500 uppercase">Price</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Cycle</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Users</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Vehicles</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Drivers</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Active Subs</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">Loading...</td></tr>
              ) : data?.items?.length === 0 ? (
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">No packages found</td></tr>
              ) : (
                data?.items?.map(p => (
                  <tr key={p.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className={`w-9 h-9 rounded-lg flex items-center justify-center text-sm font-bold ${p.isDefault ? 'bg-blue-100 text-blue-700' : p.price > 200 ? 'bg-purple-100 text-purple-700' : 'bg-green-100 text-green-700'}`}>
                          <Package className="w-4 h-4" />
                        </div>
                        <div>
                          <div className="text-sm font-medium text-gray-900 flex items-center gap-2">
                            {p.name}
                            {p.isDefault && <span className="px-1.5 py-0.5 bg-blue-100 text-blue-700 text-xs font-medium rounded">Default</span>}
                          </div>
                          <div className="text-xs text-gray-400 max-w-[200px] truncate">{p.description || '—'}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-right">
                      <span className="text-sm font-semibold text-gray-900">${p.price}</span>
                      <span className="text-xs text-gray-400">/{p.billingCycle === 'monthly' ? 'mo' : p.billingCycle === 'yearly' ? 'yr' : 'cycle'}</span>
                    </td>
                    <td className="px-4 py-3 text-center text-sm text-gray-600 capitalize">{p.billingCycle}</td>
                    <td className="px-4 py-3 text-center text-sm text-gray-600">{p.maxUsers === -1 ? '∞' : p.maxUsers}</td>
                    <td className="px-4 py-3 text-center text-sm text-gray-600">{p.maxVehicles === -1 ? '∞' : p.maxVehicles}</td>
                    <td className="px-4 py-3 text-center text-sm text-gray-600">{p.maxDrivers === -1 ? '∞' : p.maxDrivers}</td>
                    <td className="px-4 py-3 text-center">
                      <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-indigo-50 text-indigo-700 text-xs font-semibold">{p.activeSubscriptions}</span>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${p.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>
                        {p.status === 0 ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button onClick={() => setModal({ open: true, edit: p })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit"><Pencil className="w-4 h-4 text-gray-500" /></button>
                        <button onClick={() => setDeleteConfirm(p)} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete"><Trash2 className="w-4 h-4 text-red-500" /></button>
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
            <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} packages</span>
            <div className="flex items-center gap-2">
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Package Modal */}
      {modal.open && (
        <PackageModal edit={modal.edit} onClose={() => setModal({ open: false })} onSaved={() => { setModal({ open: false }); fetchData(); }} />
      )}

      {/* Delete Confirmation */}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setDeleteConfirm(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Package</h3>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete <strong>{deleteConfirm.name}</strong>?</p>
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

function PackageModal({ edit, onClose, onSaved }: { edit?: PackageItem; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!edit?.id;
  const [form, setForm] = useState({
    name: edit?.name || '', description: edit?.description || '', price: edit?.price ?? 0,
    currency: edit?.currency || 'USD', billingCycle: edit?.billingCycle || 'monthly',
    displayOrder: edit?.displayOrder ?? 0, isDefault: edit?.isDefault || false,
    maxUsers: edit?.maxUsers ?? 5, maxVehicles: edit?.maxVehicles ?? 10, maxDrivers: edit?.maxDrivers ?? 10,
    storageLimitMb: edit?.storageLimitMb ?? 1024, maxApiCallsPerDay: edit?.maxApiCallsPerDay ?? 1000,
    maxTrackingDevices: edit?.maxTrackingDevices ?? 10, maxAlertRules: edit?.maxAlertRules ?? 20,
    maxGeofences: edit?.maxGeofences ?? 10,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const input = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
  const label = "block text-sm font-medium text-gray-700 mb-1";
  const numInput = input + " [appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none";

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Package name required'); return; }
    if (form.price < 0) { setError('Price must be positive'); return; }
    setSaving(true); setError('');
    try {
      if (isEdit) { await api.put(`/admin/packages/${edit!.id}`, form); }
      else { await api.post('/admin/packages', form); }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed'); }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Package' : 'Create Package'}</h2>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}

          {/* Basic Info */}
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><Package className="w-4 h-4" /> Basic Information</div>
            <div className="grid grid-cols-2 gap-4">
              <div><label className={label}>Name *</label><input className={input} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="e.g. Professional" /></div>
              <div><label className={label}>Description</label><input className={input} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} placeholder="Optional" /></div>
            </div>
          </div>

          {/* Pricing */}
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><DollarSign className="w-4 h-4" /> Pricing</div>
            <div className="grid grid-cols-3 gap-4">
              <div><label className={label}>Price ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.price} onChange={e => setForm({ ...form, price: parseFloat(e.target.value) || 0 })} /></div>
              <div><label className={label}>Currency</label><select className={input} value={form.currency} onChange={e => setForm({ ...form, currency: e.target.value })}><option value="USD">USD</option><option value="EUR">EUR</option><option value="GBP">GBP</option><option value="INR">INR</option><option value="AED">AED</option></select></div>
              <div><label className={label}>Billing Cycle</label><select className={input} value={form.billingCycle} onChange={e => setForm({ ...form, billingCycle: e.target.value })}><option value="monthly">Monthly</option><option value="yearly">Yearly</option><option value="custom">Custom</option></select></div>
            </div>
          </div>

          {/* Limits */}
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><Users className="w-4 h-4" /> Limits</div>
            <div className="grid grid-cols-3 gap-4">
              <div><label className={label}>Max Users</label><input className={numInput} type="number" min="-1" value={form.maxUsers} onChange={e => setForm({ ...form, maxUsers: parseInt(e.target.value) || 0 })} /><p className="text-xs text-gray-400 mt-0.5">-1 = unlimited</p></div>
              <div><label className={label}>Max Vehicles</label><input className={numInput} type="number" min="-1" value={form.maxVehicles} onChange={e => setForm({ ...form, maxVehicles: parseInt(e.target.value) || 0 })} /></div>
              <div><label className={label}>Max Drivers</label><input className={numInput} type="number" min="-1" value={form.maxDrivers} onChange={e => setForm({ ...form, maxDrivers: parseInt(e.target.value) || 0 })} /></div>
            </div>
            <div className="grid grid-cols-3 gap-4 mt-4">
              <div><label className={label}>Storage (MB)</label><input className={numInput} type="number" min="0" value={form.storageLimitMb} onChange={e => setForm({ ...form, storageLimitMb: parseInt(e.target.value) || 0 })} /></div>
              <div><label className={label}>API Calls / Day</label><input className={numInput} type="number" min="0" value={form.maxApiCallsPerDay} onChange={e => setForm({ ...form, maxApiCallsPerDay: parseInt(e.target.value) || 0 })} /></div>
              <div><label className={label}>Tracking Devices</label><input className={numInput} type="number" min="0" value={form.maxTrackingDevices} onChange={e => setForm({ ...form, maxTrackingDevices: parseInt(e.target.value) || 0 })} /></div>
            </div>
            <div className="grid grid-cols-3 gap-4 mt-4">
              <div><label className={label}>Alert Rules</label><input className={numInput} type="number" min="-1" value={form.maxAlertRules} onChange={e => setForm({ ...form, maxAlertRules: parseInt(e.target.value) || 0 })} /></div>
              <div><label className={label}>Geofences</label><input className={numInput} type="number" min="-1" value={form.maxGeofences} onChange={e => setForm({ ...form, maxGeofences: parseInt(e.target.value) || 0 })} /></div>
              <div><label className={label}>Display Order</label><input className={numInput} type="number" min="0" value={form.displayOrder} onChange={e => setForm({ ...form, displayOrder: parseInt(e.target.value) || 0 })} /></div>
            </div>
          </div>

          {/* Flags */}
          <div className="flex items-center gap-6">
            <label className="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" checked={form.isDefault} onChange={e => setForm({ ...form, isDefault: e.target.checked })} className="w-4 h-4 text-blue-600 rounded" />
              <span className="text-sm font-medium text-gray-700">Default Package</span>
            </label>
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
