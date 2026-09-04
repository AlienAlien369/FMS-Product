import { useCallback, useEffect, useState } from 'react';
import api from '../lib/api';
import { useNavigate } from 'react-router-dom';
import { Search, Building2, Users, Truck, Shield, Package, ChevronLeft, ChevronRight, Eye, Plus, MapPin } from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { SUBSCRIPTION_STATUS } from '../lib/constants';
import CreateCompanyModal from '../components/company/CreateCompanyModal';

interface Company {
  id: string; name: string; slug: string; logoUrl?: string; contactEmail?: string; contactPhone?: string;
  country?: string; city?: string; website?: string; address?: string;
  status: number; createdAt: string; defaultLanguage: string; defaultTimezone: string; defaultCurrency: string;
  userCount: number; vehicleCount: number; driverCount: number; roleCount: number; moduleCount: number;
  subscriptionStatus?: number; packageName?: string; packagePrice?: number;
  subscriptionEndDate?: string; isSubscriptionExpired?: boolean;
}

interface PagedData { items: Company[]; totalCount: number; page: number; pageSize: number; totalPages: number; hasPrevious: boolean; hasNext: boolean; }

const subStatusMap = SUBSCRIPTION_STATUS;

export default function AdminCompanies() {
  const { can } = usePermissions();
  const canCreate = can('company.create');
  const { version: scopeVersion } = useCompanyScope();
  const [data, setData] = useState<PagedData | null>(null);
  const [overview, setOverview] = useState<any>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [createModal, setCreateModal] = useState(false);
  const navigate = useNavigate();

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '10', search });
      const [companiesRes, overviewRes] = await Promise.all([
        api.get(`/admin/companies?${params}`),
        api.get('/admin/overview'),
      ]);
      setData(companiesRes.data.data);
      setOverview(overviewRes.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search, scopeVersion]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const platformStats = overview ? [
    { label: 'Companies', value: overview.companies, icon: Building2, color: 'text-blue-600 bg-blue-50' },
    { label: 'Users', value: overview.users, icon: Users, color: 'text-green-600 bg-green-50' },
    { label: 'Vehicles', value: overview.vehicles, icon: Truck, color: 'text-purple-600 bg-purple-50' },
    { label: 'Drivers', value: overview.drivers, icon: Users, color: 'text-cyan-600 bg-cyan-50' },
    { label: 'Roles', value: overview.roles, icon: Shield, color: 'text-orange-600 bg-orange-50' },
    { label: 'Modules', value: overview.modules, icon: Package, color: 'text-indigo-600 bg-indigo-50' },
  ] : [];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">Platform Administration</h2>
          <p className="text-gray-500 text-sm mt-1">Cross-company management and oversight</p>
        </div>
        {canCreate && (
          <button onClick={() => setCreateModal(true)}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4" /> Create Company
          </button>
        )}
      </div>

      {/* Platform Overview Stats */}
      {overview && (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
          {platformStats.map(s => {
            const Icon = s.icon;
            return (
              <div key={s.label} className="bg-white rounded-xl border border-gray-200 p-4 hover:shadow-md transition-shadow">
                <div className={`w-9 h-9 ${s.color} rounded-lg flex items-center justify-center mb-2`}>
                  <Icon className="w-4.5 h-4.5" />
                </div>
                <p className="text-2xl font-bold text-gray-900">{s.value}</p>
                <p className="text-xs text-gray-500">{s.label}</p>
              </div>
            );
          })}
        </div>
      )}

      {/* Companies Table */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search by name, slug, email, country..." />
        </div>
      </div>

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-50 border-b border-gray-200">
              <tr>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Company</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Location</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Contact</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Subscription</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Users</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Vehicles</th>
                <th className="text-center px-4 py-3 text-xs font-medium text-gray-500 uppercase">Modules</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">Loading...</td></tr>
              ) : data?.items?.length === 0 ? (
                <tr><td colSpan={9} className="text-center py-12 text-gray-400">No companies found</td></tr>
              ) : (
                data?.items?.map(c => (
                  <tr key={c.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <div className="w-9 h-9 bg-blue-100 rounded-lg flex items-center justify-center text-blue-700 text-sm font-bold flex-shrink-0">
                          {c.logoUrl ? <img src={c.logoUrl} alt="" className="w-9 h-9 rounded-lg object-cover" /> : c.name.substring(0, 2).toUpperCase()}
                        </div>
                        <div>
                          <div className="text-sm font-medium text-gray-900">{c.name}</div>
                          <div className="text-xs text-gray-400">{c.slug || '—'}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1 text-sm text-gray-600">
                        <MapPin className="w-3 h-3 text-gray-400 flex-shrink-0" />
                        {[c.city, c.country].filter(Boolean).join(', ') || '—'}
                      </div>
                    </td>
                    <td className="px-4 py-3">
                      <div className="text-sm text-gray-600">{c.contactEmail || '—'}</div>
                      {c.contactPhone && <div className="text-xs text-gray-400">{c.contactPhone}</div>}
                    </td>
                    <td className="px-4 py-3">
                      {c.subscriptionStatus != null ? (
                        <div>
                          <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${subStatusMap[c.subscriptionStatus]?.color || 'bg-gray-100 text-gray-700'}`}>
                            {subStatusMap[c.subscriptionStatus]?.label || 'Unknown'}
                          </span>
                          <div className="text-xs text-gray-400 mt-0.5">{c.packageName || '—'} {c.packagePrice != null ? `($${c.packagePrice})` : ''}</div>
                          {c.isSubscriptionExpired && <div className="text-xs text-red-600 font-medium">Expired</div>}
                        </div>
                      ) : (
                        <span className="text-xs text-gray-400 italic">No subscription</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-center">
                      <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-blue-50 text-blue-700 text-xs font-semibold">{c.userCount}</span>
                    </td>
                    <td className="px-4 py-3 text-center">
                      <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-purple-50 text-purple-700 text-xs font-semibold">{c.vehicleCount}</span>
                    </td>
                    <td className="px-4 py-3 text-center">
                      <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-indigo-50 text-indigo-700 text-xs font-semibold">{c.moduleCount}</span>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${c.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>
                        {c.status === 0 ? 'Active' : c.status === 2 ? 'Pending' : c.status === 3 ? 'Suspended' : 'Inactive'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <button onClick={() => navigate(`/admin/companies/${c.id}`)}
                        className="flex items-center gap-1.5 px-3 py-1.5 bg-blue-50 text-blue-700 rounded-lg text-xs font-medium hover:bg-blue-100 transition-colors">
                        <Eye className="w-3.5 h-3.5" /> View Details
                      </button>
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
              <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)}
                className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
              <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
              <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)}
                className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
            </div>
          </div>
        )}
      </div>

      {/* Create Company Modal */}
      {createModal && <CreateCompanyModal onClose={() => setCreateModal(false)} onSaved={() => { setCreateModal(false); fetchData(); }} />}
    </div>
  );
}
