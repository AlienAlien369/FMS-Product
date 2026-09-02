import { useState } from 'react';
import api from '../../lib/api';
import { X, Check } from 'lucide-react';

const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

interface RoleOption { id: string; name: string; description?: string; }

export default function UserModal({ companyId, user, roles, onClose, onSaved }: { companyId: string; user?: any; roles: RoleOption[]; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!user?.id;
  const [firstName, setFirstName] = useState(user?.firstName || '');
  const [lastName, setLastName] = useState(user?.lastName || '');
  const [email, setEmail] = useState(user?.email || '');
  const [phone, setPhone] = useState(user?.phoneNumber || '');
  const [password, setPassword] = useState('');
  const [selectedRoles, setSelectedRoles] = useState<Set<string>>(new Set(user?.roleIds || []));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async () => {
    if (!firstName.trim() || !lastName.trim() || !email.trim()) { setError('Name and email required'); return; }
    if (!isEdit && (!password || password.length < 6)) { setError('Password min 6 chars'); return; }
    setSaving(true); setError('');
    try {
      if (isEdit) { await api.put(`/admin/companies/${companyId}/users/${user.id}`, { firstName, lastName, phoneNumber: phone || null, roleIds: Array.from(selectedRoles) }); }
      else { await api.post(`/admin/companies/${companyId}/users`, { email, password, firstName, lastName, phoneNumber: phone || null, roleIds: Array.from(selectedRoles) }); }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed'); }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-lg max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200"><h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit User' : 'Create User'}</h2><button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button></div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          <div className="grid grid-cols-2 gap-4"><div><label className={LABEL}>First Name *</label><input className={INPUT} value={firstName} onChange={e => setFirstName(e.target.value)} /></div><div><label className={LABEL}>Last Name *</label><input className={INPUT} value={lastName} onChange={e => setLastName(e.target.value)} /></div></div>
          <div><label className={LABEL}>Email *</label><input className={INPUT + (isEdit ? ' bg-gray-50' : '')} type="email" value={email} onChange={e => setEmail(e.target.value)} disabled={isEdit} /></div>
          <div><label className={LABEL}>Phone</label><input className={INPUT} value={phone} onChange={e => setPhone(e.target.value)} /></div>
          {!isEdit && <div><label className={LABEL}>Password *</label><input className={INPUT} type="password" value={password} onChange={e => setPassword(e.target.value)} placeholder="Min 6 characters" /></div>}
          <div>
            <div className="flex items-center justify-between mb-2"><label className="text-sm font-medium text-gray-700">Roles</label><span className="text-xs text-gray-400">{selectedRoles.size} selected</span></div>
            <div className="space-y-1.5">{roles.map(r => (
              <button key={r.id} onClick={() => { const n = new Set(selectedRoles); if (n.has(r.id)) n.delete(r.id); else n.add(r.id); setSelectedRoles(n); }}
                className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg border text-left transition-colors ${selectedRoles.has(r.id) ? 'bg-blue-50 border-blue-200' : 'bg-white border-gray-200 hover:bg-gray-50'}`}>
                <div className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${selectedRoles.has(r.id) ? 'bg-blue-600 border-blue-600' : 'border-gray-300'}`}>{selectedRoles.has(r.id) && <Check className="w-3 h-3 text-white" />}</div>
                <div className="min-w-0"><div className="text-sm font-medium">{r.name}</div>{r.description && <div className="text-xs text-gray-400 truncate">{r.description}</div>}</div>
              </button>
            ))}</div>
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
