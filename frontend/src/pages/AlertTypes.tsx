import { useState, useEffect, useCallback } from 'react';
import { Bell, Plus, Pencil, Trash2, Search, Filter } from 'lucide-react';
import api from '../lib/api';
import SeverityChip, { SEVERITY_LABELS } from '../components/SeverityChip';

interface AlertType {
  id: string;
  code: string;
  name: string;
  description: string | null;
  category: string;
  defaultSeverity: number;
  displayOrder: number;
  status: number;
}

const CATEGORIES = ['Geofence', 'Route', 'Trip', 'Vehicle', 'Driver', 'Device', 'System'];

export default function AlertTypes() {
  const [types, setTypes] = useState<AlertType[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [editType, setEditType] = useState<AlertType | null>(null);
  const [form, setForm] = useState({ code: '', name: '', description: '', category: 'Geofence', defaultSeverity: 2, displayOrder: 0 });

  const fetchTypes = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams();
      if (search) params.set('search', search);
      if (categoryFilter) params.set('category', categoryFilter);
      const r = await api.get(`/alert-types?${params}`);
      setTypes(r.data.data || []);
    } catch { /* ignore */ }
    setLoading(false);
  }, [search, categoryFilter]);

  useEffect(() => { fetchTypes(); }, [fetchTypes]);

  const handleCreate = async () => {
    try {
      await api.post('/alert-types', form);
      setShowCreate(false);
      setForm({ code: '', name: '', description: '', category: 'Geofence', defaultSeverity: 2, displayOrder: 0 });
      fetchTypes();
    } catch { /* ignore */ }
  };

  const handleUpdate = async () => {
    if (!editType) return;
    try {
      await api.put(`/alert-types/${editType.id}`, {
        name: form.name, description: form.description, category: form.category,
        defaultSeverity: form.defaultSeverity, displayOrder: form.displayOrder
      });
      setEditType(null);
      fetchTypes();
    } catch { /* ignore */ }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this alert type?')) return;
    try {
      await api.delete(`/alert-types/${id}`);
      fetchTypes();
    } catch { /* ignore */ }
  };

  const handleToggleStatus = async (t: AlertType) => {
    try {
      await api.put(`/alert-types/${t.id}`, { status: t.status === 0 ? 1 : 0 });
      fetchTypes();
    } catch { /* ignore */ }
  };

  const categories = [...new Set(types.map(t => t.category))];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-2">
            <Bell className="h-6 w-6" /> Alert Types
          </h1>
          <p className="text-sm text-gray-500 mt-1">Platform alert type catalog — controls which alerts the platform can generate</p>
        </div>
        <button onClick={() => { setShowCreate(true); setForm({ code: '', name: '', description: '', category: 'Geofence', defaultSeverity: 2, displayOrder: types.length }); }}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">
          <Plus className="h-4 w-4" /> Add Alert Type
        </button>
      </div>

      {/* Filters */}
      <div className="flex gap-3 items-center">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-2.5 h-4 w-4 text-gray-400" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search alert types..."
            className="pl-10 pr-4 py-2 border rounded-lg w-full text-sm" />
        </div>
        <select value={categoryFilter} onChange={e => setCategoryFilter(e.target.value)}
          className="border rounded-lg px-3 py-2 text-sm">
          <option value="">All Categories</option>
          {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>

      {/* Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500">Code</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500">Name</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500">Category</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500">Severity</th>
              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500">Status</th>
              <th className="px-4 py-3 text-right text-xs font-medium text-gray-500">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200">
            {loading ? (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500">Loading...</td></tr>
            ) : types.length === 0 ? (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500">No alert types found</td></tr>
            ) : types.map(t => (
              <tr key={t.id} className="hover:bg-gray-50">
                <td className="px-4 py-3 text-sm font-mono text-gray-700">{t.code}</td>
                <td className="px-4 py-3 text-sm font-medium text-gray-900">{t.name}</td>
                <td className="px-4 py-3">
                  <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-800">
                    {t.category}
                  </span>
                </td>
                <td className="px-4 py-3">
                  <SeverityChip severity={t.defaultSeverity} size="md" />
                </td>
                <td className="px-4 py-3">
                  <button onClick={() => handleToggleStatus(t)}
                    className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium cursor-pointer ${t.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                    {t.status === 0 ? 'Active' : 'Inactive'}
                  </button>
                </td>
                <td className="px-4 py-3 text-right space-x-2">
                  <button onClick={() => { setEditType(t); setForm({ code: t.code, name: t.name, description: t.description || '', category: t.category, defaultSeverity: t.defaultSeverity, displayOrder: t.displayOrder }); }}
                    className="text-gray-400 hover:text-blue-600"><Pencil className="h-4 w-4" /></button>
                  <button onClick={() => handleDelete(t.id)} className="text-gray-400 hover:text-red-600"><Trash2 className="h-4 w-4" /></button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Create/Edit Modal */}
      {(showCreate || editType) && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-xl shadow-xl w-full max-w-md p-6 space-y-4">
            <h3 className="text-lg font-semibold">{editType ? 'Edit Alert Type' : 'Create Alert Type'}</h3>
            <div className="space-y-3">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Code *</label>
                <input value={form.code} onChange={e => setForm(f => ({ ...f, code: e.target.value }))}
                  disabled={!!editType} className="w-full border rounded-lg px-3 py-2 text-sm disabled:bg-gray-100"
                  placeholder="e.g. geofence.entry" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Name *</label>
                <input value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                  className="w-full border rounded-lg px-3 py-2 text-sm" placeholder="Display name" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
                <textarea value={form.description} onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                  className="w-full border rounded-lg px-3 py-2 text-sm" rows={2} />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Category *</label>
                  <select value={form.category} onChange={e => setForm(f => ({ ...f, category: e.target.value }))}
                    className="w-full border rounded-lg px-3 py-2 text-sm">
                    {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Default Severity</label>
                  <select value={form.defaultSeverity} onChange={e => setForm(f => ({ ...f, defaultSeverity: +e.target.value }))}
                    className="w-full border rounded-lg px-3 py-2 text-sm">
                    {[0, 1, 2, 3, 4].map(s => <option key={s} value={s}>{SEVERITY_LABELS[s]}</option>)}
                  </select>
                </div>
              </div>
            </div>
            <div className="flex justify-end gap-3 pt-2">
              <button onClick={() => { setShowCreate(false); setEditType(null); }} className="px-4 py-2 text-sm text-gray-600 hover:text-gray-900">Cancel</button>
              <button onClick={editType ? handleUpdate : handleCreate}
                className="px-4 py-2 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700">
                {editType ? 'Update' : 'Create'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
