import { useState, useEffect, useCallback } from 'react';
import { Bell, Plus, Pencil, Trash2, Search } from 'lucide-react';
import api from '../lib/api';
import SeverityChip, { SEVERITY_LABELS } from '../components/SeverityChip';
import { ResponsiveCards, DetailField } from '../components/ui';

interface NotificationEventType {
  id: string;
  code: string;
  name: string;
  description: string | null;
  category: string;
  defaultSeverity: number;
  displayOrder: number;
  status: number;
}

const CATEGORIES = ['Company', 'Permission', 'Fleet', 'User', 'Device', 'System'];

export default function NotificationSettings() {
  const [types, setTypes] = useState<NotificationEventType[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [showCreate, setShowCreate] = useState(false);
  const [editType, setEditType] = useState<NotificationEventType | null>(null);
  const [form, setForm] = useState({ code: '', name: '', description: '', category: 'Company', defaultSeverity: 2, displayOrder: 0 });

  const fetchTypes = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams();
      if (categoryFilter) params.set('category', categoryFilter);
      const q = params.toString();
      const r = await api.get(`/notifications/event-types${q ? `?${q}` : ''}`);
      setTypes((r.data.data || []).filter((t: NotificationEventType) =>
        !search || t.name.toLowerCase().includes(search.toLowerCase()) || t.code.toLowerCase().includes(search.toLowerCase())));
    } catch { /* ignore */ }
    setLoading(false);
  }, [search, categoryFilter]);

  useEffect(() => { fetchTypes(); }, [fetchTypes]);

  const handleCreate = async () => {
    try {
      await api.post('/notifications/event-types', form);
      setShowCreate(false);
      setForm({ code: '', name: '', description: '', category: 'Company', defaultSeverity: 2, displayOrder: 0 });
      fetchTypes();
    } catch { /* ignore */ }
  };

  const handleUpdate = async () => {
    if (!editType) return;
    try {
      await api.put(`/notifications/event-types/${editType.id}`, {
        name: form.name, description: form.description, category: form.category,
        defaultSeverity: form.defaultSeverity, displayOrder: form.displayOrder
      });
      setEditType(null);
      fetchTypes();
    } catch { /* ignore */ }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Delete this notification event type?')) return;
    try {
      await api.delete(`/notifications/event-types/${id}`);
      fetchTypes();
    } catch { /* ignore */ }
  };

  const handleToggleStatus = async (t: NotificationEventType) => {
    try {
      await api.put(`/notifications/event-types/${t.id}`, { status: t.status === 0 ? 1 : 0 });
      fetchTypes();
    } catch { /* ignore */ }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Notification Settings</h2>
          <p className="text-sm text-gray-500 mt-0.5">
            Master registry of notification-worthy events. Adding a new source is a row here — users mute events
            individually from their My Notifications page; email/SMS delivery is a follow-up phase.
          </p>
        </div>
        <button onClick={() => { setShowCreate(true); setEditType(null); setForm({ code: '', name: '', description: '', category: 'Company', defaultSeverity: 2, displayOrder: 0 }); }}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors">
          <Plus className="w-4 h-4" /> Add Event Type
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div className="relative">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search…"
            className="pl-9 pr-3 py-2 border border-gray-200 rounded-lg text-sm w-64" />
        </div>
        <select value={categoryFilter} onChange={e => setCategoryFilter(e.target.value)}
          className="px-3 py-2 border border-gray-200 rounded-lg text-sm bg-white">
          <option value="">All categories</option>
          {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>

      <div className="hidden md:block bg-white rounded-xl border border-gray-200 overflow-hidden">
        {loading ? (
          <div className="text-center py-12 text-gray-400">Loading…</div>
        ) : types.length === 0 ? (
          <div className="py-12 text-center text-gray-400"><Bell className="w-10 h-10 mx-auto mb-2 text-gray-300" /><p className="text-sm">No event types</p></div>
        ) : (
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Code</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Category</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Severity</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {types.map(t => (
                <tr key={t.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3 text-sm font-mono text-gray-700">{t.code}</td>
                  <td className="px-4 py-3">
                    <div className="text-sm font-medium text-gray-900">{t.name}</div>
                    {t.description && <div className="text-xs text-gray-500">{t.description}</div>}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-600">{t.category}</td>
                  <td className="px-4 py-3"><SeverityChip severity={t.defaultSeverity} size="md" /></td>
                  <td className="px-4 py-3">
                    <button onClick={() => handleToggleStatus(t)}
                      className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${t.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                      {t.status === 0 ? 'Active' : 'Inactive'}
                    </button>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1">
                      <button onClick={() => { setEditType(t); setShowCreate(true); setForm({ code: t.code, name: t.name, description: t.description ?? '', category: t.category, defaultSeverity: t.defaultSeverity, displayOrder: t.displayOrder }); }}
                        className="p-1.5 hover:bg-gray-100 rounded-lg"><Pencil className="w-4 h-4 text-gray-500" /></button>
                      <button onClick={() => handleDelete(t.id)} className="p-1.5 hover:bg-gray-100 rounded-lg"><Trash2 className="w-4 h-4 text-red-500" /></button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Mobile stacked cards — same data, same actions as the table */}
      <ResponsiveCards
        items={types}
        loading={loading}
        empty="No event types"
        keyOf={t => t.id}
        title={t => t.name}
        subtitle={t => <code className="font-mono text-[13px]">{t.code}</code>}
        statusChip={t => (
          <button onClick={() => handleToggleStatus(t)}
            className={`inline-flex px-2.5 py-1 rounded-full text-xs font-medium shrink-0 ${t.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
            {t.status === 0 ? 'Active' : 'Inactive'}
          </button>
        )}
        primary={t => [
          { label: 'Category', value: t.category },
          { label: 'Severity', value: <SeverityChip severity={t.defaultSeverity} size="md" /> },
        ]}
        details={t => (
          <div className="pt-2">
            {t.description && <DetailField label="Description" value={t.description} />}
          </div>
        )}
        actions={t => [
          { key: 'edit', label: 'Edit', icon: <Pencil className="w-4 h-4" />, onClick: () => { setEditType(t); setShowCreate(true); setForm({ code: t.code, name: t.name, description: t.description ?? '', category: t.category, defaultSeverity: t.defaultSeverity, displayOrder: t.displayOrder }); } },
          { key: 'delete', label: 'Delete', icon: <Trash2 className="w-4 h-4" />, danger: true, onClick: () => handleDelete(t.id) },
        ]}
      />

      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setShowCreate(false)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-lg">
            <h3 className="text-lg font-semibold text-gray-900 mb-4">{editType ? 'Edit Event Type' : 'Add Event Type'}</h3>
            <div className="space-y-3">
              {!editType && (
                <div>
                  <label className="block text-xs font-medium text-gray-500 mb-1">Code</label>
                  <input value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} placeholder="company.package_changed"
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm" />
                </div>
              )}
              <div>
                <label className="block text-xs font-medium text-gray-500 mb-1">Name</label>
                <input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })}
                  className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm" />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-500 mb-1">Description</label>
                <input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })}
                  className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm" />
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="block text-xs font-medium text-gray-500 mb-1">Category</label>
                  <select value={form.category} onChange={e => setForm({ ...form, category: e.target.value })}
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm bg-white">
                    {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-medium text-gray-500 mb-1">Severity</label>
                  <select value={form.defaultSeverity} onChange={e => setForm({ ...form, defaultSeverity: Number(e.target.value) })}
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm bg-white">
                    {[0, 1, 2, 3, 4].map(s => <option key={s} value={s}>{SEVERITY_LABELS[s]}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-medium text-gray-500 mb-1">Order</label>
                  <input type="number" value={form.displayOrder} onChange={e => setForm({ ...form, displayOrder: Number(e.target.value) || 0 })}
                    className="w-full px-3 py-2 border border-gray-200 rounded-lg text-sm" />
                </div>
              </div>
            </div>
            <div className="flex justify-end gap-3 mt-6">
              <button onClick={() => setShowCreate(false)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={editType ? handleUpdate : handleCreate}
                className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700">
                {editType ? 'Save Changes' : 'Create'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}