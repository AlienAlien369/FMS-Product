import { useEffect, useState } from 'react';
import api from '../lib/api';
import { X, Shield, Check } from 'lucide-react';
import { PAGES, pageByKey } from '../config/pages';

interface GroupedPerm { module: string; permissions: { id: string; code: string; name: string; action: string; }[]; }

interface RoleData {
  id?: string; name?: string; description?: string;
  assignedPermissionIds?: string[];
}

interface Props {
  role: RoleData | null; // null = create mode
  onClose: () => void;
  onSaved: () => void;
}

export default function RoleModal({ role, onClose, onSaved }: Props) {
  const isEdit = !!role?.id;
  const [name, setName] = useState(role?.name || '');
  const [description, setDescription] = useState(role?.description || '');
  const [allPerms, setAllPerms] = useState<GroupedPerm[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set(role?.assignedPermissionIds || []));
  const [myPermIds, setMyPermIds] = useState<Set<string>>(new Set());
  const [isSuperAdmin, setIsSuperAdmin] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    // Load all permissions grouped, my permissions for hierarchy, and existing role perms
    const p1 = api.get('/permissions/grouped').then(r => setAllPerms(r.data.data || []));
    const pMy = api.get('/roles/my-permissions').then(r => {
      const data = r.data.data;
      if (data?.permissionIds) setMyPermIds(new Set(data.permissionIds.map(String)));
      if (data?.isSuperAdmin) setIsSuperAdmin(true);
    });
    const p2 = isEdit ? api.get(`/roles/${role!.id}/permissions`).then(r => {
      const permIds = (r.data.data || []).map((p: any) => p.permissionId);
      setSelected(new Set(permIds));
    }) : Promise.resolve();
    Promise.all([p1, pMy, p2]).catch(() => {}).finally(() => setLoading(false));
  }, [role]);

  const togglePerm = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const toggleModule = (modulePerms: { id: string }[]) => {
    const allSelected = modulePerms.every(p => selected.has(p.id));
    setSelected(prev => {
      const next = new Set(prev);
      modulePerms.forEach(p => { if (allSelected) next.delete(p.id); else next.add(p.id); });
      return next;
    });
  };

  const handleSubmit = async () => {
    if (!name.trim()) { setError('Role name is required'); return; }
    setSaving(true);
    setError('');
    try {
      const payload = { name: name.trim(), description: description.trim(), permissionIds: Array.from(selected) };
      if (isEdit) {
        await api.put(`/roles/${role!.id}`, payload);
      } else {
        await api.post('/roles', payload);
      }
      onSaved();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Failed to save role');
    }
    setSaving(false);
  };

  const actionColor = (a: string) => {
    const m: Record<string, string> = {
      Read:    'bg-blue-100 text-blue-700 border-blue-200',
      Create:  'bg-green-100 text-green-700 border-green-200',
      Update:  'bg-yellow-100 text-yellow-700 border-yellow-200',
      Delete:  'bg-red-100 text-red-700 border-red-200',
      Export:  'bg-purple-100 text-purple-700 border-purple-200',
      Import:  'bg-cyan-100 text-cyan-700 border-cyan-200',
    };
    return m[a] || 'bg-gray-100 text-gray-700 border-gray-200';
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div className="flex items-center gap-2">
            <Shield className="w-5 h-5 text-purple-600" />
            <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Role' : 'Create Role'}</h2>
          </div>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && (
            <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Role Name *</label>
              <input value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Fleet Manager"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
              <input value={description} onChange={e => setDescription(e.target.value)} placeholder="Optional description"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500" />
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-3">
              <label className="text-sm font-medium text-gray-700">Permissions</label>
              <span className="text-xs text-gray-400">{selected.size} selected</span>
            </div>

            {loading ? (
              <div className="text-sm text-gray-400 py-4">Loading permissions...</div>
            ) : (
              <div className="space-y-3">
                {!isSuperAdmin && myPermIds.size === 0 && (
                  <div className="text-xs text-amber-600 bg-amber-50 border border-amber-200 rounded-lg p-2">
                    You have no permissions assigned. Contact your administrator.
                  </div>
                )}
                {allPerms
                  // Render groups in canonical registry order; skip anything not in the registry
                  .map(g => ({ group: g, page: pageByKey(g.module) }))
                  .filter(({ page }) => !!page)
                  .sort((a, b) => a.page!.order - b.page!.order)
                  .map(({ group: g, page }) => {
                  // Filter: only show permissions the caller has
                  const available = isSuperAdmin ? g.permissions : g.permissions.filter(p => myPermIds.has(p.id));
                  if (available.length === 0) return null;
                  const allChecked = available.every(p => selected.has(p.id));
                  const someChecked = available.some(p => selected.has(p.id));
                  return (
                    <div key={g.module} className="border border-gray-200 rounded-lg overflow-hidden">
                      <div className="flex items-center gap-3 px-4 py-2.5 bg-gray-50 border-b border-gray-200">
                        <button onClick={() => toggleModule(available)}
                          className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-colors ${allChecked ? 'bg-blue-600 border-blue-600' : someChecked ? 'bg-blue-100 border-blue-400' : 'border-gray-300 hover:border-gray-400'}`}>
                          {(allChecked || someChecked) && <Check className="w-3 h-3 text-white" />}
                        </button>
                        <span className="text-sm font-semibold text-gray-800">{page!.label}</span>
                        {page!.planned && <span className="px-1.5 py-0.5 bg-amber-100 text-amber-700 text-[10px] font-medium rounded">Planned</span>}
                        <span className="text-xs text-gray-400">{available.length} permissions</span>
                      </div>
                      <div className="p-3 flex flex-wrap gap-1.5">
                        {available.map(p => (
                          <button key={p.id} onClick={() => togglePerm(p.id)}
                            className={`inline-flex items-center gap-1 px-2.5 py-1.5 rounded-md text-xs font-medium border transition-colors ${selected.has(p.id) ? actionColor(p.action) : 'bg-white text-gray-500 border-gray-200 hover:bg-gray-50'}`}>
                            {selected.has(p.id) && <Check className="w-3 h-3" />}
                            {p.name}
                          </button>
                        ))}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg transition-colors">Cancel</button>
          <button onClick={handleSubmit} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400 transition-colors">
            {saving ? 'Saving...' : isEdit ? 'Update Role' : 'Create Role'}
          </button>
        </div>
      </div>
    </div>
  );
}
