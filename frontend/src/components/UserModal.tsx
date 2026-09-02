import { useEffect, useState } from 'react';
import api from '../lib/api';
import { X, UserPlus, Check } from 'lucide-react';

interface RoleOption { id: string; name: string; description?: string; }

interface UserData {
  id?: string; firstName?: string; lastName?: string; email?: string;
  phoneNumber?: string; roleIds?: string[];
}

interface Props {
  user: UserData | null; // null = create mode
  onClose: () => void;
  onSaved: () => void;
}

export default function UserModal({ user, onClose, onSaved }: Props) {
  const isEdit = !!user?.id;
  const [firstName, setFirstName] = useState(user?.firstName || '');
  const [lastName, setLastName] = useState(user?.lastName || '');
  const [email, setEmail] = useState(user?.email || '');
  const [phone, setPhone] = useState(user?.phoneNumber || '');
  const [password, setPassword] = useState('');
  const [roles, setRoles] = useState<RoleOption[]>([]);
  const [selectedRoles, setSelectedRoles] = useState<Set<string>>(new Set(user?.roleIds || []));
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    const p1 = api.get('/roles?pageSize=50').then(r => {
      setRoles((r.data.data?.items || []).map((r: any) => ({ id: r.id, name: r.name, description: r.description })));
    });
    const p2 = isEdit ? api.get(`/users/${user!.id}`).then(r => {
      const data = r.data.data;
      setSelectedRoles(new Set(data.roleIds || []));
    }) : Promise.resolve();
    Promise.all([p1, p2]).catch(() => {}).finally(() => setLoading(false));
  }, [user]);

  const toggleRole = (id: string) => {
    setSelectedRoles(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const handleSubmit = async () => {
    if (!firstName.trim() || !lastName.trim()) { setError('First and last name are required'); return; }
    if (!email.trim()) { setError('Email is required'); return; }
    if (!isEdit && !password.trim()) { setError('Password is required for new users'); return; }
    if (!isEdit && password.length < 6) { setError('Password must be at least 6 characters'); return; }

    setSaving(true);
    setError('');
    try {
      if (isEdit) {
        await api.put(`/users/${user!.id}`, {
          firstName: firstName.trim(), lastName: lastName.trim(),
          phoneNumber: phone.trim() || null,
          roleIds: Array.from(selectedRoles)
        });
      } else {
        await api.post('/users', {
          email: email.trim(), password,
          firstName: firstName.trim(), lastName: lastName.trim(),
          phoneNumber: phone.trim() || null,
          roleIds: Array.from(selectedRoles)
        });
      }
      onSaved();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Failed to save user');
    }
    setSaving(false);
  };

  const input = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500";
  const label = "block text-sm font-medium text-gray-700 mb-1";

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-lg max-h-[85vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div className="flex items-center gap-2">
            <UserPlus className="w-5 h-5 text-blue-600" />
            <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit User' : 'Create User'}</h2>
          </div>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {error && (
            <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className={label}>First Name *</label>
              <input value={firstName} onChange={e => setFirstName(e.target.value)} placeholder="John"
                className={input} />
            </div>
            <div>
              <label className={label}>Last Name *</label>
              <input value={lastName} onChange={e => setLastName(e.target.value)} placeholder="Doe"
                className={input} />
            </div>
          </div>

          <div>
            <label className={label}>Email *</label>
            <input value={email} onChange={e => setEmail(e.target.value)} placeholder="john@example.com"
              type="email" disabled={isEdit}
              className={input + (isEdit ? ' bg-gray-50 cursor-not-allowed' : '')} />
          </div>

          <div>
            <label className={label}>Phone</label>
            <input value={phone} onChange={e => setPhone(e.target.value)} placeholder="+1 555 0123"
              className={input} />
          </div>

          {!isEdit && (
            <div>
              <label className={label}>Password *</label>
              <input value={password} onChange={e => setPassword(e.target.value)} placeholder="Min 6 characters"
                type="password" className={input} />
            </div>
          )}

          <div>
            <div className="flex items-center justify-between mb-2">
              <label className={label + ' mb-0'}>Roles</label>
              <span className="text-xs text-gray-400">{selectedRoles.size} selected</span>
            </div>
            {loading ? (
              <div className="text-sm text-gray-400 py-2">Loading roles...</div>
            ) : roles.length === 0 ? (
              <div className="text-sm text-gray-400 py-2">No roles available. Create roles first.</div>
            ) : (
              <div className="space-y-1.5">
                {roles.map(r => (
                  <button key={r.id} onClick={() => toggleRole(r.id)}
                    className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg border text-left transition-colors ${selectedRoles.has(r.id) ? 'bg-blue-50 border-blue-200 text-blue-800' : 'bg-white border-gray-200 hover:bg-gray-50 text-gray-700'}`}>
                    <div className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${selectedRoles.has(r.id) ? 'bg-blue-600 border-blue-600' : 'border-gray-300'}`}>
                      {selectedRoles.has(r.id) && <Check className="w-3 h-3 text-white" />}
                    </div>
                    <div className="min-w-0">
                      <div className="text-sm font-medium">{r.name}</div>
                      {r.description && <div className="text-xs text-gray-400 truncate">{r.description}</div>}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg transition-colors">Cancel</button>
          <button onClick={handleSubmit} disabled={saving}
            className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400 transition-colors">
            {saving ? 'Saving...' : isEdit ? 'Update User' : 'Create User'}
          </button>
        </div>
      </div>
    </div>
  );
}
