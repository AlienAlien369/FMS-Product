import { useEffect, useState } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import { Shield, Users, ChevronRight, Plus, Pencil, Trash2 } from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';
import RoleModal from '../components/RoleModal';

interface Role {
  id: string; name: string; description?: string; isSystemRole: boolean;
  status: number; displayOrder: number; userCount: number; permissionCount: number;
}

interface Perm { id: string; permissionId: string; code: string; name: string; module: string; action: string; }
interface GroupedPerm { module: string; permissions: { id: string; code: string; name: string; action: string; }[]; }

export default function Roles() {
  const { user } = useAuth();
  const { can } = usePermissions();
  const isSuperAdmin = user?.roles?.includes('SuperAdmin') ?? false;
  const canCreate = can('role.create');
  const canEdit = can('role.update');
  const canDelete = can('role.delete');
  const [roles, setRoles] = useState<Role[]>([]);
  const [loading, setLoading] = useState(true);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [rolePerms, setRolePerms] = useState<Perm[]>([]);
  const [allPerms, setAllPerms] = useState<GroupedPerm[]>([]);
  const [permLoading, setPermLoading] = useState(false);
  const [view, setView] = useState<'roles' | 'permissions'>('roles');
  const [modalOpen, setModalOpen] = useState(false);
  const [editRole, setEditRole] = useState<{ id: string; name: string; description?: string } | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const fetchRoles = () => api.get('/roles?pageSize=50').then(r => setRoles(r.data.data?.items || [])).catch(() => {});

  useEffect(() => {
    fetchRoles().finally(() => setLoading(false));
    api.get('/permissions/grouped').then(r => setAllPerms(r.data.data || [])).catch(() => {});
  }, []);

  const expandRole = async (roleId: string) => {
    if (expanded === roleId) { setExpanded(null); return; }
    setExpanded(roleId);
    setPermLoading(true);
    try {
      const res = await api.get(`/roles/${roleId}/permissions`);
      setRolePerms(res.data.data || []);
    } catch { setRolePerms([]); }
    setPermLoading(false);
  };

  const handleDelete = async (id: string) => {
    try { await api.delete(`/roles/${id}`); setConfirmDelete(null); fetchRoles(); } catch {}
  };

  const actionColor = (a: string) => {
    const m: Record<string, string> = {
      Read: 'bg-blue-50 text-blue-700', Create: 'bg-green-50 text-green-700',
      Update: 'bg-yellow-50 text-yellow-700', Delete: 'bg-red-50 text-red-700',
      Export: 'bg-purple-50 text-purple-700', Assign: 'bg-cyan-50 text-cyan-700',
      Execute: 'bg-orange-50 text-orange-700', Manage: 'bg-gray-50 text-gray-700',
    };
    return m[a] || 'bg-gray-50 text-gray-700';
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold text-gray-900">Roles & Permissions</h2>
        {view === 'roles' && canCreate && (
          <button onClick={() => { setEditRole(null); setModalOpen(true); }}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4" /> Add Role
          </button>
        )}
      </div>

      <div className="flex gap-1 bg-gray-100 p-1 rounded-lg w-fit">
        <button onClick={() => setView('roles')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md text-sm font-medium transition-colors ${view === 'roles' ? 'bg-white shadow text-gray-900' : 'text-gray-600 hover:text-gray-900'}`}>
          <Shield className="w-4 h-4" /> Roles
        </button>
        <button onClick={() => setView('permissions')}
          className={`flex items-center gap-2 px-4 py-2 rounded-md text-sm font-medium transition-colors ${view === 'permissions' ? 'bg-white shadow text-gray-900' : 'text-gray-600 hover:text-gray-900'}`}>
          <Users className="w-4 h-4" /> All Permissions
        </button>
      </div>

      {view === 'roles' ? (
        <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
          {loading ? (
            <div className="text-center py-12 text-gray-400">Loading...</div>
          ) : roles.length === 0 ? (
            <div className="text-center py-12 text-gray-400">No roles found. Click "Add Role" to create one.</div>
          ) : (
            roles.map(r => (
              <div key={r.id}>
                <div className="flex items-center gap-4 px-5 py-4 hover:bg-gray-50 transition-colors">
                  <div className="w-10 h-10 bg-purple-50 rounded-lg flex items-center justify-center flex-shrink-0 cursor-pointer" onClick={() => expandRole(r.id)}>
                    <Shield className="w-5 h-5 text-purple-600" />
                  </div>
                  <div className="flex-1 min-w-0 cursor-pointer" onClick={() => expandRole(r.id)}>
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold text-gray-900">{r.name}</span>
                      {r.isSystemRole && <span className="px-1.5 py-0.5 bg-amber-100 text-amber-700 text-xs font-medium rounded">System</span>}
                    </div>
                    <p className="text-xs text-gray-500 mt-0.5">{r.description || 'No description'}</p>
                  </div>
                  <div className="text-right flex-shrink-0 mr-2">
                    <div className="text-xs text-gray-500"><Users className="w-3 h-3 inline mr-1" />{r.userCount} users</div>
                    <div className="text-xs text-gray-400">{r.permissionCount} permissions</div>
                  </div>
                  <div className="flex items-center gap-1 flex-shrink-0">
                    {(isSuperAdmin || !r.isSystemRole) && (
                      <>
                        {canEdit && (
                          <button onClick={(e) => { e.stopPropagation(); setEditRole({ id: r.id, name: r.name, description: r.description }); setModalOpen(true); }}
                            className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit">
                            <Pencil className="w-4 h-4 text-gray-500" />
                          </button>
                        )}
                        {canDelete && (
                          <button onClick={(e) => { e.stopPropagation(); setConfirmDelete(r.id); }}
                            className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete">
                            <Trash2 className="w-4 h-4 text-red-500" />
                          </button>
                        )}
                      </>
                    )}
                  </div>
                  <ChevronRight className={`w-4 h-4 text-gray-400 transition-transform cursor-pointer ${expanded === r.id ? 'rotate-90' : ''}`} onClick={() => expandRole(r.id)} />
                </div>

                {expanded === r.id && (
                  <div className="bg-gray-50 border-t border-gray-100 px-5 py-3">
                    {permLoading ? (
                      <div className="text-sm text-gray-400 py-2">Loading permissions...</div>
                    ) : rolePerms.length === 0 ? (
                      <div className="text-sm text-gray-400 py-2">No permissions assigned to this role</div>
                    ) : (
                      <div className="flex flex-wrap gap-1.5">
                        {rolePerms.map(p => (
                          <span key={p.id} className={`inline-flex items-center px-2 py-1 rounded-md text-xs font-medium ${actionColor(p.action)}`}>
                            {p.code}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                )}
              </div>
            ))
          )}
        </div>
      ) : (
        <div className="space-y-4">
          {allPerms.length === 0 ? (
            <div className="bg-white rounded-xl border border-gray-200 text-center py-12 text-gray-400">No permissions found</div>
          ) : (
            allPerms.map(g => (
              <div key={g.module} className="bg-white rounded-xl border border-gray-200 overflow-hidden">
                <div className="px-5 py-3 bg-gray-50 border-b border-gray-200">
                  <h3 className="text-sm font-semibold text-gray-900 capitalize">{g.module}</h3>
                </div>
                <div className="p-4 flex flex-wrap gap-1.5">
                  {g.permissions.map(p => (
                    <span key={p.id} className={`inline-flex items-center px-2.5 py-1 rounded-md text-xs font-medium ${actionColor(p.action)}`}>
                      {p.name}
                    </span>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>
      )}

      {/* Delete Confirmation */}
      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setConfirmDelete(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Role</h3>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete this role? This action cannot be undone.</p>
            <div className="flex justify-end gap-3">
              <button onClick={() => setConfirmDelete(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={() => handleDelete(confirmDelete)} className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}

      {/* Create/Edit Modal */}
      {modalOpen && (
        <RoleModal
          role={editRole}
          onClose={() => { setModalOpen(false); setEditRole(null); }}
          onSaved={() => { setModalOpen(false); setEditRole(null); fetchRoles(); }}
        />
      )}
    </div>
  );
}
