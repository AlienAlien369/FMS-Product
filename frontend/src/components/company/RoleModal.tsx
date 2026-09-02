import { useState, useEffect } from 'react';
import api from '../../lib/api';
import { X, Check } from 'lucide-react';

const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

interface GroupedPerm { module: string; permissions: { id: string; code: string; name: string; action: string; }[]; }

export default function RoleModal({ companyId, role, allPerms, onClose, onSaved }: { companyId: string; role?: any; allPerms: GroupedPerm[]; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!role?.id;
  const [name, setName] = useState(role?.name || '');
  const [description, setDescription] = useState(role?.description || '');
  const [selectedPerms, setSelectedPerms] = useState<Set<string>>(new Set(role?.permissions?.map((p: any) => p.id) || []));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (isEdit && role?.id) {
      api.get(`/roles/${role.id}/permissions`).then(r => {
        setSelectedPerms(new Set((r.data.data || []).map((p: any) => p.permissionId)));
      });
    }
  }, [role]);

  const handleSubmit = async () => {
    if (!name.trim()) { setError('Role name required'); return; }
    setSaving(true); setError('');
    try {
      const payload = { name, description, permissionIds: Array.from(selectedPerms) };
      if (isEdit) { await api.put(`/admin/companies/${companyId}/roles/${role.id}`, payload); }
      else { await api.post(`/admin/companies/${companyId}/roles`, payload); }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed'); }
    setSaving(false);
  };

  const actionColor = (a: string) => ({ Read: 'bg-blue-100 text-blue-700 border-blue-200', Create: 'bg-green-100 text-green-700 border-green-200', Update: 'bg-yellow-100 text-yellow-700 border-yellow-200', Delete: 'bg-red-100 text-red-700 border-red-200', Export: 'bg-purple-100 text-purple-700 border-purple-200', Assign: 'bg-cyan-100 text-cyan-700 border-cyan-200', Execute: 'bg-orange-100 text-orange-700 border-orange-200', Manage: 'bg-gray-100 text-gray-700 border-gray-200' } as any)[a] || 'bg-gray-100 text-gray-700 border-gray-200';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200"><h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Role' : 'Create Role'}</h2><button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button></div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          <div className="grid grid-cols-2 gap-4"><div><label className={LABEL}>Role Name *</label><input className={INPUT} value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Fleet Manager" /></div><div><label className={LABEL}>Description</label><input className={INPUT} value={description} onChange={e => setDescription(e.target.value)} placeholder="Optional" /></div></div>
          <div>
            <div className="flex items-center justify-between mb-2"><label className="text-sm font-medium text-gray-700">Permissions</label><span className="text-xs text-gray-400">{selectedPerms.size} selected</span></div>
            <div className="space-y-3">{allPerms.map(g => {
              const allChecked = g.permissions.every(p => selectedPerms.has(p.id));
              const someChecked = g.permissions.some(p => selectedPerms.has(p.id));
              const toggleModule = () => { const n = new Set(selectedPerms); g.permissions.forEach(p => { if (allChecked) n.delete(p.id); else n.add(p.id); }); setSelectedPerms(n); };
              return (
                <div key={g.module} className="border border-gray-200 rounded-lg overflow-hidden">
                  <div className="flex items-center gap-3 px-4 py-2.5 bg-gray-50 border-b border-gray-200">
                    <button onClick={toggleModule} className={`w-5 h-5 rounded border-2 flex items-center justify-center ${allChecked ? 'bg-blue-600 border-blue-600' : someChecked ? 'bg-blue-100 border-blue-400' : 'border-gray-300'}`}>{(allChecked || someChecked) && <Check className="w-3 h-3 text-white" />}</button>
                    <span className="text-sm font-semibold text-gray-800 capitalize">{g.module}</span><span className="text-xs text-gray-400">{g.permissions.length}</span>
                  </div>
                  <div className="p-3 flex flex-wrap gap-1.5">{g.permissions.map(p => (
                    <button key={p.id} onClick={() => { const n = new Set(selectedPerms); if (n.has(p.id)) n.delete(p.id); else n.add(p.id); setSelectedPerms(n); }}
                      className={`inline-flex items-center gap-1 px-2.5 py-1.5 rounded-md text-xs font-medium border transition-colors ${selectedPerms.has(p.id) ? actionColor(p.action) : 'bg-white text-gray-500 border-gray-200 hover:bg-gray-50'}`}>{selectedPerms.has(p.id) && <Check className="w-3 h-3" />}{p.name}</button>
                  ))}</div>
                </div>
              );
            })}</div>
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
