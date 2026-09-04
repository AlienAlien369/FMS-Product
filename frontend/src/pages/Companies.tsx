import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import api from '../lib/api';
import { Search, Plus, Pencil, Trash2, ChevronLeft, ChevronRight, Eye, Building2, X, CheckCircle, AlertCircle, Globe, Clock, DollarSign, Users, Package } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { usePermissions } from '../hooks/usePermissions';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import CompanyEditModal from '../components/company/CompanyEditModal';
import CreateCompanyModal from '../components/company/CreateCompanyModal';

interface CompanyRow {
  id: string; name: string; slug?: string; contactEmail?: string; contactPhone?: string;
  country?: string; city?: string; status: number; createdAt: string;
  defaultLanguage: string; defaultTimezone: string; defaultCurrency: string;
  userCount?: number; vehicleCount?: number; driverCount?: number; roleCount?: number; moduleCount?: number;
  packageName?: string;
}
interface PagedData { items: CompanyRow[]; totalCount: number; page: number; pageSize: number; totalPages: number; hasPrevious: boolean; hasNext: boolean; }

function Toast({ kind, text }: { kind: 'success' | 'error'; text: string }) {
  return (
    <div className={`fixed top-4 right-4 z-[60] flex items-center gap-2 px-4 py-3 rounded-lg shadow-lg text-sm font-medium ${kind === 'success' ? 'bg-green-600 text-white' : 'bg-red-600 text-white'}`}>
      {kind === 'success' ? <CheckCircle className="w-4 h-4" /> : <AlertCircle className="w-4 h-4" />}
      {text}
    </div>
  );
}

// ── SuperAdmin: platform company administration with full CRUD ─────────────
function CompanyAdminList() {
  const { can } = usePermissions();
  const canCreate = can('company.create');
  const canEdit = can('company.update');
  const canDelete = can('company.delete');
  const navigate = useNavigate();
  const { version: scopeVersion } = useCompanyScope();

  const [data, setData] = useState<PagedData | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [createOpen, setCreateOpen] = useState(false);
  const [editCompany, setEditCompany] = useState<CompanyRow | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<CompanyRow | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [deleteError, setDeleteError] = useState('');
  const [toast, setToast] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const notify = (kind: 'success' | 'error', text: string) => {
    setToast({ kind, text });
    setTimeout(() => setToast(null), 4000);
  };

  const fetchData = async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search });
      const res = await api.get(`/admin/companies?${params}`);
      setData(res.data.data);
    } catch (e: any) {
      notify('error', e.response?.data?.message || 'Failed to load companies');
    }
    setLoading(false);
  };

  useEffect(() => { fetchData(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [page, search, scopeVersion]);

  const handleDelete = async () => {
    if (!confirmDelete) return;
    setDeleting(true); setDeleteError('');
    try {
      await api.delete(`/admin/companies/${confirmDelete.id}`);
      setDeleting(false);
      setConfirmDelete(null);
      notify('success', `"${confirmDelete.name}" deleted`);
      fetchData();
    } catch (e: any) {
      setDeleting(false);
      setDeleteError(e.response?.data?.message || 'Failed to delete company');
    }
  };

  return (
    <div className="space-y-4">
      {toast && <Toast kind={toast.kind} text={toast.text} />}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search companies..." />
        </div>
        {canCreate && (
          <button onClick={() => setCreateOpen(true)}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">
            <Plus className="w-4 h-4" /> Add Company
          </button>
        )}
      </div>

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Name</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Contact</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Country</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Users</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Vehicles</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr><td colSpan={7} className="text-center py-12 text-gray-400">Loading...</td></tr>
              ) : data?.items?.length === 0 ? (
                <tr><td colSpan={7} className="text-center py-12 text-gray-400">No companies found</td></tr>
              ) : (
                data?.items?.map(c => (
                  <tr key={c.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-8 h-8 bg-blue-100 rounded-lg flex items-center justify-center text-blue-700 text-xs font-bold">{c.name.substring(0, 2).toUpperCase()}</div>
                        <div>
                          <div className="text-sm font-medium text-gray-900">{c.name}</div>
                          <div className="text-xs text-gray-400">{c.slug || '—'}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-600">{c.contactEmail || '—'}</td>
                    <td className="px-4 py-3 text-sm text-gray-600">{c.country || '—'}</td>
                    <td className="px-4 py-3 text-center text-sm text-gray-600">{c.userCount ?? 0}</td>
                    <td className="px-4 py-3 text-center text-sm text-gray-600">{c.vehicleCount ?? 0}</td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${c.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>
                        {c.status === 0 ? 'Active' : 'Inactive'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1">
                        <button onClick={() => navigate(`/admin/companies/${c.id}`)}
                          className="p-1.5 hover:bg-gray-100 rounded-lg" title="View details">
                          <Eye className="w-4 h-4 text-blue-500" />
                        </button>
                        {canEdit && (
                          <button onClick={() => setEditCompany(c)} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit">
                            <Pencil className="w-4 h-4 text-gray-500" />
                          </button>
                        )}
                        {canDelete && (
                          <button onClick={() => { setDeleteError(''); setConfirmDelete(c); }} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete">
                            <Trash2 className="w-4 h-4 text-red-500" />
                          </button>
                        )}
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
            <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} companies</span>
            <div className="flex items-center gap-2">
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Delete confirmation with server-side guard feedback */}
      {confirmDelete && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setConfirmDelete(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Company</h3>
            <p className="text-sm text-gray-600 mb-3">
              Are you sure you want to delete <strong>{confirmDelete.name}</strong>? This cannot be undone.
            </p>
            {deleteError && <div className="p-3 mb-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{deleteError}</div>}
            <div className="flex justify-end gap-3">
              <button onClick={() => setConfirmDelete(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={handleDelete} disabled={deleting} className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700 disabled:bg-red-400">
                {deleting ? 'Deleting...' : 'Delete'}
              </button>
            </div>
          </div>
        </div>
      )}

      {createOpen && <CreateCompanyModal onClose={() => setCreateOpen(false)} onSaved={() => { setCreateOpen(false); notify('success', 'Company created'); fetchData(); }} />}
      {editCompany && (
        <CompanyEditModal company={editCompany as any}
          onClose={() => setEditCompany(null)}
          onSaved={() => { setEditCompany(null); notify('success', 'Company updated'); fetchData(); }} />
      )}
    </div>
  );
}

// ── Company admin: their own company's card (they have no cross-company view) ──
function MyCompanyCard() {
  const [company, setCompany] = useState<any>(null);
  const [modules, setModules] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    Promise.all([
      api.get('/tenant/company').catch(() => null),
      api.get('/tenant/company/modules').catch(() => null),
    ]).then(([c, m]) => {
      if (c?.data?.data) setCompany(c.data.data);
      if (m?.data?.data) setModules(m.data.data);
    }).finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;
  if (!company) return <div className="text-center py-12 text-gray-400">Could not load your company.</div>;

  return (
    <div className="max-w-3xl space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold text-gray-900">My Company</h2>
        <span className="text-sm text-gray-500">Language, currency and format preferences are managed in <span className="font-medium">Settings</span>.</span>
      </div>
      <div className="bg-white rounded-xl border border-gray-200 p-6">
        <div className="flex items-center gap-4 mb-6">
          <div className="w-12 h-12 bg-blue-600 rounded-xl flex items-center justify-center text-white font-bold text-lg">{company.name?.substring(0, 1)?.toUpperCase() || 'C'}</div>
          <div>
            <div className="text-lg font-semibold text-gray-900">{company.name}</div>
            <div className="text-sm text-gray-500">{company.contactEmail || company.contactPhone || '—'}</div>
          </div>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="flex items-center gap-3 bg-gray-50 rounded-lg p-3">
            <Users className="w-4 h-4 text-gray-400" />
            <div><div className="text-xs text-gray-500">Slug</div><div className="text-sm font-medium text-gray-900">{company.slug || '—'}</div></div>
          </div>
          <div className="flex items-center gap-3 bg-gray-50 rounded-lg p-3">
            <Globe className="w-4 h-4 text-gray-400" />
            <div><div className="text-xs text-gray-500">Language</div><div className="text-sm font-medium text-gray-900 uppercase">{company.defaultLanguage}</div></div>
          </div>
          <div className="flex items-center gap-3 bg-gray-50 rounded-lg p-3">
            <DollarSign className="w-4 h-4 text-gray-400" />
            <div><div className="text-xs text-gray-500">Currency</div><div className="text-sm font-medium text-gray-900">{company.defaultCurrency}</div></div>
          </div>
          <div className="flex items-center gap-3 bg-gray-50 rounded-lg p-3">
            <Clock className="w-4 h-4 text-gray-400" />
            <div><div className="text-xs text-gray-500">Timezone</div><div className="text-sm font-medium text-gray-900">{company.defaultTimezone}</div></div>
          </div>
        </div>
      </div>
      {/* Modules the company can use — live only (the same set the sidebar shows) */}
      <div className="bg-white rounded-xl border border-gray-200 p-6">
        <div className="flex items-center gap-2 mb-4">
          <Package className="w-4 h-4 text-blue-600" />
          <h3 className="text-sm font-semibold text-gray-900">Modules Your Company Can Use</h3>
          {modules?.packageName && <span className="text-xs text-gray-400">· {modules.packageName} package</span>}
        </div>
        {(modules?.modules?.length || 0) > 0 ? (
          <div className="space-y-3">
            {modules.modules.map((m: any) => (
              <div key={m.id} className="flex items-start justify-between gap-4 rounded-lg border border-gray-100 p-3">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-gray-900">{m.name}</span>
                    {m.isCore && <span className="px-1.5 py-0.5 bg-purple-100 text-purple-700 text-xs font-medium rounded">Core</span>}
                  </div>
                  {m.pages?.length > 0 && (
                    <div className="flex flex-wrap gap-1.5 mt-1.5">
                      {m.pages.map((p: any) => (
                        <span key={p.key} className="inline-flex items-center px-2 py-0.5 rounded-md text-xs font-medium bg-blue-50 text-blue-700 border border-blue-100">
                          {p.label}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
                <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-700 flex-shrink-0">Included</span>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-sm text-gray-500">No package assigned yet — contact the platform administrator.</p>
        )}
        <p className="text-xs text-gray-400 mt-4 flex items-start gap-1.5">
          <Building2 className="w-3.5 h-3.5 mt-0.5 flex-shrink-0" />
          These come from the package assigned by the platform administrator — package changes apply to all your users on their next request.
        </p>
      </div>
    </div>
  );
}

export default function Companies() {
  const { user } = useAuth();
  const isSuperAdmin = user?.roles?.includes('SuperAdmin') ?? false;
  return isSuperAdmin ? <CompanyAdminList /> : <MyCompanyCard />;
}
