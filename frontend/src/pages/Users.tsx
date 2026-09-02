import { useEffect, useState } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import { Search, Plus, Pencil, Trash2, ChevronLeft, ChevronRight, UserX } from 'lucide-react';
import UserModal from '../components/UserModal';

interface User {
  id: string; email: string; firstName: string; lastName: string;
  phoneNumber?: string; companyId?: string; status: number; lastLoginAt?: string; createdAt: string;
  roles: string[]; roleIds: string[];
}

interface PagedData { items: User[]; totalCount: number; page: number; pageSize: number; totalPages: number; hasPrevious: boolean; hasNext: boolean; }

export default function Users() {
  const { user } = useAuth();
  const isSuperAdmin = user?.roles?.includes('SuperAdmin');
  const [data, setData] = useState<PagedData | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [editUser, setEditUser] = useState<{ id: string; firstName: string; lastName: string; email: string; phoneNumber?: string; roleIds: string[] } | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const fetchData = async () => {
    setLoading(true);
    try {
      const res = await api.get(`/users?page=${page}&pageSize=10&search=${search}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  };

  useEffect(() => { fetchData(); }, [page, search]);

  const handleDelete = async (id: string) => {
    try { await api.delete(`/users/${id}`); setConfirmDelete(null); fetchData(); } catch {}
  };

  const formatDate = (d?: string) => d ? new Date(d).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
  const formatTime = (d?: string) => d ? new Date(d).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' }) : '—';

  const statusMap: Record<number, { label: string; color: string }> = {
    0: { label: 'Active', color: 'bg-green-100 text-green-700' },
    1: { label: 'Inactive', color: 'bg-gray-100 text-gray-700' },
    2: { label: 'Locked', color: 'bg-red-100 text-red-700' },
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search by name or email..." />
        </div>
        <button onClick={() => { setEditUser(null); setModalOpen(true); }}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
          <Plus className="w-4 h-4" /> Add User
        </button>
      </div>

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">User</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Email</th>
                {isSuperAdmin && <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Company</th>}
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Phone</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Roles</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Last Login</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr><td colSpan={isSuperAdmin ? 8 : 7} className="text-center py-12 text-gray-400">Loading...</td></tr>
              ) : data?.items?.length === 0 ? (
                <tr><td colSpan={isSuperAdmin ? 8 : 7} className="text-center py-12 text-gray-400">No users found</td></tr>
              ) : (
                data?.items?.map(u => (
                  <tr key={u.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 bg-blue-600 rounded-full flex items-center justify-center text-white text-xs font-medium flex-shrink-0">
                          {u.firstName?.[0]}{u.lastName?.[0]}
                        </div>
                        <div>
                          <div className="text-sm font-medium text-gray-900">{u.firstName} {u.lastName}</div>
                          <div className="text-xs text-gray-400">Joined {formatDate(u.createdAt)}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">{u.email}</td>
                    {isSuperAdmin && <td className="px-4 py-3 text-sm text-gray-500 font-mono text-xs">{u.companyId ? u.companyId.slice(0, 8) : '—'}</td>}
                    <td className="px-4 py-3 text-sm text-gray-600">{u.phoneNumber || '—'}</td>
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap gap-1">
                        {u.roles?.length > 0 ? u.roles.map((r, i) => (
                          <span key={i} className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-purple-100 text-purple-700">{r}</span>
                        )) : <span className="text-xs text-gray-400">No roles</span>}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">
                      {u.lastLoginAt ? (
                        <div>
                          <div>{formatDate(u.lastLoginAt)}</div>
                          <div className="text-xs text-gray-400">{formatTime(u.lastLoginAt)}</div>
                        </div>
                      ) : 'Never'}
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${statusMap[u.status]?.color || 'bg-gray-100 text-gray-700'}`}>
                        {statusMap[u.status]?.label || 'Unknown'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button onClick={() => {
                          setEditUser({ id: u.id, firstName: u.firstName, lastName: u.lastName, email: u.email, phoneNumber: u.phoneNumber, roleIds: u.roleIds });
                          setModalOpen(true);
                        }} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit">
                          <Pencil className="w-4 h-4 text-gray-500" />
                        </button>
                        <button onClick={() => setConfirmDelete(u.id)} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete">
                          <Trash2 className="w-4 h-4 text-red-500" />
                        </button>
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
            <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} users</span>
            <div className="flex items-center gap-2">
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)}
                className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)}
                className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Delete Confirmation */}
      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setConfirmDelete(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <div className="flex items-center gap-2 mb-2">
              <UserX className="w-5 h-5 text-red-600" />
              <h3 className="text-lg font-semibold text-gray-900">Delete User</h3>
            </div>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete this user? They will no longer be able to log in.</p>
            <div className="flex justify-end gap-3">
              <button onClick={() => setConfirmDelete(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={() => handleDelete(confirmDelete)} className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {/* Create/Edit Modal */}
      {modalOpen && (
        <UserModal
          user={editUser}
          onClose={() => { setModalOpen(false); setEditUser(null); }}
          onSaved={() => { setModalOpen(false); setEditUser(null); fetchData(); }}
        />
      )}
    </div>
  );
}
