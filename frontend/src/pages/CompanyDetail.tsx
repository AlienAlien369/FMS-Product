import { useEffect, useState, useCallback, Component, type ReactNode } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import api from '../lib/api';
import { ArrowLeft, Users, Shield, Package, FileText, Globe, Clock, DollarSign, Mail, Pencil, Plus, Trash2, ToggleLeft, ToggleRight, MapPin, Building2, CreditCard, Languages, Settings } from 'lucide-react';
import { SUBSCRIPTION_STATUS } from '../lib/constants';
import CompanyEditModal from '../components/company/CompanyEditModal';
import SubscriptionModal from '../components/company/SubscriptionModal';
import UserModal from '../components/company/UserModal';
import RoleModal from '../components/company/RoleModal';

// Types
// ── Error Boundary ──────────────────────────────────────
interface BoundaryProps { children: ReactNode; }
interface BoundaryState { error: Error | null; }

class CompanyDetailBoundary extends Component<BoundaryProps, BoundaryState> {
  state: BoundaryState = { error: null };
  static getDerivedStateFromError(error: Error) { return { error }; }
  render() {
    if (this.state.error) {
      const err = this.state.error;
      return (
        <div className="flex flex-col items-center justify-center h-96 text-center px-6">
          <div className="w-16 h-16 bg-red-100 rounded-full flex items-center justify-center mb-4">
            <svg className="w-8 h-8 text-red-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
          </div>
          <h2 className="text-lg font-semibold text-gray-900 mb-2">Something went wrong</h2>
          <p className="text-sm text-gray-500 mb-4 max-w-md">{err.message || 'An unexpected error occurred while loading the company details.'}</p>
          <button onClick={() => { this.setState({ error: null }); window.location.reload(); }}
            className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 transition-colors">
            Reload Page
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}

// ── Types ────────────────────────────────────────────────
interface CompanyInfo {
  id: string; name: string; slug: string; contactEmail?: string; contactPhone?: string;
  website?: string; address?: string; city?: string; state?: string; country?: string; postalCode?: string;
  status: number; createdAt: string; defaultLanguage: string; defaultTimezone: string; defaultCurrency: string;
  dateFormat?: string; timeFormat?: string; numberFormat?: string;
  logoUrl?: string; faviconUrl?: string;
}
interface RoleOption { id: string; name: string; description?: string; }
interface GroupedPerm { module: string; permissions: { id: string; code: string; name: string; action: string; }[]; }
interface SubscriptionInfo {
  id: string; companyId: string; packageId: string; packageName: string;
  status: number; startDate: string; endDate?: string; canceledAt?: string;
  currentPrice: number; currency: string; billingCycle: string;
  discountPercentage?: number; taxPercentage?: number; effectivePrice: number;
  maxUsers?: number; maxVehicles?: number; maxDrivers?: number; createdAt: string;
}
interface PackageOption { id: string; name: string; price: number; billingCycle: string; maxUsers: number; maxVehicles: number; maxDrivers: number; }

function CompanyDetailInner() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [company, setCompany] = useState<CompanyInfo | null>(null);
  const [tab, setTab] = useState<'overview' | 'users' | 'roles' | 'modules' | 'documents' | 'subscription'>('overview');
  const [users, setUsers] = useState<any[]>([]);
  const [roles, setRoles] = useState<any[]>([]);
  const [modules, setModules] = useState<any>(null);
  const [documents, setDocuments] = useState<any[]>([]);
  const [subscription, setSubscription] = useState<SubscriptionInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [tabLoading, setTabLoading] = useState(false);
  const [rolesList, setRolesList] = useState<RoleOption[]>([]);
  const [allPerms, setAllPerms] = useState<GroupedPerm[]>([]);
  const [packages, setPackages] = useState<PackageOption[]>([]);
  const [languages, setLanguages] = useState<any[]>([]);
  const [currencies, setCurrencies] = useState<any[]>([]);
  const [allModules, setAllModules] = useState<any[]>([]);
  const [settings, setSettings] = useState<any>(null);

  const [editCompany, setEditCompany] = useState(false);
  const [userModal, setUserModal] = useState<{ open: boolean; edit?: any }>({ open: false });
  const [roleModal, setRoleModal] = useState<{ open: boolean; edit?: any }>({ open: false });
  const [subModal, setSubModal] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<{ type: 'user' | 'role'; id: string; name: string } | null>(null);

  const fetchCompany = useCallback(() => { if (id) return api.get(`/admin/companies/${id}`).then(r => setCompany(r.data.data)); }, [id]);

  const fetchTab = useCallback(async () => {
    if (!id) return;
    setTabLoading(true);
    try {
      if (tab === 'users') { const r = await api.get(`/admin/companies/${id}/users?pageSize=50`); setUsers(r.data.data?.items || []); }
      else if (tab === 'roles') { const r = await api.get(`/admin/companies/${id}/roles`); setRoles(r.data.data || []); }
      else if (tab === 'modules') { const r = await api.get(`/admin/companies/${id}/modules`); setModules(r.data.data); }
      else if (tab === 'documents') { const r = await api.get(`/admin/companies/${id}/documents?pageSize=50`); setDocuments(r.data.data?.items || []); }
      else if (tab === 'subscription') { const r = await api.get(`/admin/companies/${id}/subscription`); setSubscription(r.data.data); }
      else if (tab === 'packages') { const r = await api.get(`/admin/companies/${id}/subscription`); setSubscription(r.data.data); }
      else if (tab === 'localization') { setSettings(company); }
      else if (tab === 'settings') {
        const r = await api.get(`/admin/companies/${id}/features`); 
        setSettings(r.data.data);
      }
    } catch (err) { void err; }
    setTabLoading(false);
  }, [id, tab]);

  useEffect(() => { const p = fetchCompany(); if (p) p.finally(() => setLoading(false)); else setLoading(false); }, [fetchCompany]);
  useEffect(() => { fetchTab(); }, [fetchTab]);
  useEffect(() => {
    if (id) {
      api.get(`/admin/companies/${id}/roles`).then(r => setRolesList((r.data.data || []).map((r: any) => ({ id: r.id, name: r.name, description: r.description })))).catch(() => {});
      api.get('/admin/packages?pageSize=100').then(r => setPackages((r.data.data?.items || []).map((p: any) => ({ id: p.id, name: p.name, price: p.price, billingCycle: p.billingCycle, maxUsers: p.maxUsers, maxVehicles: p.maxVehicles, maxDrivers: p.maxDrivers })))).catch(() => {});
      api.get('/languages').then(r => setLanguages(r.data.data || [])).catch(() => {});
      api.get('/currencies').then(r => setCurrencies(r.data.data || [])).catch(() => {});
      api.get('/admin/modules').then(r => setAllModules(r.data.data || [])).catch(() => {});
    }
    api.get('/permissions/grouped').then(r => setAllPerms(r.data.data || []) as any).catch(() => {});
  }, [id]);

  const handleDelete = async (type: 'user' | 'role', delId: string) => {
    if (!id) return;
    await api.delete(`/admin/companies/${id}/${type}s/${delId}`);
    setDeleteConfirm(null);
    fetchTab();
  };

  if (loading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;
  if (!company) return null;

  const subStatusMap = SUBSCRIPTION_STATUS;

  const tabs = [
    { key: 'overview' as const, label: 'Overview', icon: Building2 },
    { key: 'subscription' as const, label: 'Subscription', icon: CreditCard },
    { key: 'packages' as const, label: 'Packages', icon: Package },
    { key: 'users' as const, label: 'Users', icon: Users },
    { key: 'roles' as const, label: 'Roles', icon: Shield },
    { key: 'modules' as const, label: 'Modules', icon: Package },
    { key: 'localization' as const, label: 'Localization', icon: Languages },
    { key: 'settings' as const, label: 'Settings', icon: Settings },
    { key: 'documents' as const, label: 'Documents', icon: FileText },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-4">
        <button onClick={() => navigate('/admin/companies')} className="p-2 hover:bg-gray-100 rounded-lg"><ArrowLeft className="w-5 h-5 text-gray-600" /></button>
        <div className="flex-1">
          <div className="flex items-center gap-1.5 text-sm text-gray-500 mb-1">
            <button onClick={() => navigate('/admin/companies')} className="hover:text-blue-600 transition-colors">Platform Admin</button>
            <span>/</span>
            <button onClick={() => navigate('/admin/companies')} className="hover:text-blue-600 transition-colors">Companies</button>
            <span>/</span>
            <span className="text-gray-900 font-medium">{company.name}</span>
          </div>
          <h2 className="text-2xl font-bold text-gray-900">{company.name}</h2>
          <p className="text-gray-500 text-sm">{company.slug} &bull; Created {new Date(company.createdAt).toLocaleDateString()}</p>
        </div>
        <button onClick={() => setEditCompany(true)} className="flex items-center gap-2 px-3 py-1.5 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm font-medium text-gray-700 transition-colors">
          <Pencil className="w-4 h-4" /> Edit Company
        </button>
      </div>

      {/* Subscription Expiry Warning */}
      {subscription && (subscription.status === 4 || subscription.status === 3 || (subscription.endDate && new Date(subscription.endDate) < new Date())) && (
        <div className="p-4 bg-red-50 border border-red-200 rounded-xl flex items-center gap-3">
          <div className="w-10 h-10 bg-red-100 rounded-full flex items-center justify-center flex-shrink-0">
            <CreditCard className="w-5 h-5 text-red-600" />
          </div>
          <div className="flex-1">
            <p className="text-sm font-medium text-red-800">
              {subscription.status === 3 ? 'Subscription Canceled' : 'Subscription Expired'}
            </p>
            <p className="text-xs text-red-600 mt-0.5">
              All users from this company are blocked from logging in. {subscription.endDate && `Expired on ${new Date(subscription.endDate).toLocaleDateString()}.`}
            </p>
          </div>
          <button onClick={() => { setTab('subscription'); setSubModal(true); }}
            className="px-3 py-1.5 bg-red-600 text-white text-xs font-medium rounded-lg hover:bg-red-700 whitespace-nowrap">
            Renew Now
          </button>
        </div>
      )}
      {subscription && subscription.endDate && !subscription.canceledAt && new Date(subscription.endDate) < new Date(Date.now() + 30 * 24 * 60 * 60 * 1000) && subscription.status === 0 && (
        <div className="p-4 bg-yellow-50 border border-yellow-200 rounded-xl flex items-center gap-3">
          <div className="w-10 h-10 bg-yellow-100 rounded-full flex items-center justify-center flex-shrink-0">
            <CreditCard className="w-5 h-5 text-yellow-600" />
          </div>
          <div className="flex-1">
            <p className="text-sm font-medium text-yellow-800">Subscription Expiring Soon</p>
            <p className="text-xs text-yellow-600 mt-0.5">
              Expires on {new Date(subscription.endDate).toLocaleDateString()} &mdash; {Math.ceil((new Date(subscription.endDate).getTime() - Date.now()) / (1000 * 60 * 60 * 24))} days remaining
            </p>
          </div>
          <button onClick={() => { setTab('subscription'); setSubModal(true); }}
            className="px-3 py-1.5 bg-yellow-600 text-white text-xs font-medium rounded-lg hover:bg-yellow-700 whitespace-nowrap">
            Renew
          </button>
        </div>
      )}

      {/* Info Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white rounded-xl border border-gray-200 p-4 flex items-center gap-3">
          <div className="w-10 h-10 bg-blue-50 rounded-lg flex items-center justify-center"><Mail className="w-5 h-5 text-blue-600" /></div>
          <div><div className="text-xs text-gray-500">Contact</div><div className="text-sm font-medium text-gray-900">{company.contactEmail || '\u2014'}</div></div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4 flex items-center gap-3">
          <div className="w-10 h-10 bg-green-50 rounded-lg flex items-center justify-center"><Globe className="w-5 h-5 text-green-600" /></div>
          <div><div className="text-xs text-gray-500">Language</div><div className="text-sm font-medium text-gray-900 uppercase">{company.defaultLanguage}</div></div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4 flex items-center gap-3">
          <div className="w-10 h-10 bg-purple-50 rounded-lg flex items-center justify-center"><Clock className="w-5 h-5 text-purple-600" /></div>
          <div><div className="text-xs text-gray-500">Timezone</div><div className="text-sm font-medium text-gray-900">{company.defaultTimezone}</div></div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4 flex items-center gap-3">
          <div className="w-10 h-10 bg-orange-50 rounded-lg flex items-center justify-center"><DollarSign className="w-5 h-5 text-orange-600" /></div>
          <div><div className="text-xs text-gray-500">Currency</div><div className="text-sm font-medium text-gray-900">{company.defaultCurrency}</div></div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center justify-between">
        <div className="flex gap-1 bg-gray-100 p-1 rounded-lg w-fit flex-wrap">
          {tabs.map(t => {
            const Icon = t.icon;
            return (
              <button key={t.key} onClick={() => setTab(t.key)} className={`flex items-center gap-2 px-4 py-2 rounded-md text-sm font-medium transition-colors ${tab === t.key ? 'bg-white shadow text-gray-900' : 'text-gray-600 hover:text-gray-900'}`}>
                <Icon className="w-4 h-4" /> {t.label}
              </button>
            );
          })}
        </div>
        {(tab === 'users' || tab === 'roles') && (
          <button onClick={() => tab === 'users' ? setUserModal({ open: true }) : setRoleModal({ open: true })}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4" /> Add {tab === 'users' ? 'User' : 'Role'}
          </button>
        )}
        {tab === 'subscription' && (
          <button onClick={() => setSubModal(true)}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
            <CreditCard className="w-4 h-4" /> {subscription ? 'Manage Subscription' : 'Assign Subscription'}
          </button>
        )}
      </div>

      {/* Tab Content */}
      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        {tabLoading ? (
          <div className="text-center py-12 text-gray-400">Loading...</div>
        ) : (
          <>
            {/* Overview */}
            {tab === 'overview' && (
              <div className="p-6 space-y-6">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Building2 className="w-4 h-4" /> Company Information</div>
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                  <div><div className="text-xs text-gray-500">Company Name</div><div className="text-sm font-medium text-gray-900">{company.name}</div></div>
                  <div><div className="text-xs text-gray-500">Slug</div><div className="text-sm font-medium text-gray-900">{company.slug || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">Status</div><div><span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${company.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>{company.status === 0 ? 'Active' : 'Inactive'}</span></div></div>
                  <div><div className="text-xs text-gray-500">Contact Email</div><div className="text-sm font-medium text-gray-900">{company.contactEmail || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">Contact Phone</div><div className="text-sm font-medium text-gray-900">{company.contactPhone || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">Website</div><div className="text-sm font-medium text-gray-900">{company.website || '\u2014'}</div></div>
                </div>

                <div className="flex items-center gap-2 text-gray-900 font-semibold pt-4"><MapPin className="w-4 h-4" /> Address</div>
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                  <div className="sm:col-span-2"><div className="text-xs text-gray-500">Street Address</div><div className="text-sm font-medium text-gray-900">{company.address || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">City</div><div className="text-sm font-medium text-gray-900">{company.city || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">State / Province</div><div className="text-sm font-medium text-gray-900">{company.state || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">Country</div><div className="text-sm font-medium text-gray-900">{company.country || '\u2014'}</div></div>
                  <div><div className="text-xs text-gray-500">Postal Code</div><div className="text-sm font-medium text-gray-900">{company.postalCode || '\u2014'}</div></div>
                </div>

                <div className="flex items-center gap-2 text-gray-900 font-semibold pt-4"><Globe className="w-4 h-4" /> Preferences</div>
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                  <div><div className="text-xs text-gray-500">Default Language</div><div className="text-sm font-medium text-gray-900 uppercase">{company.defaultLanguage}</div></div>
                  <div><div className="text-xs text-gray-500">Default Timezone</div><div className="text-sm font-medium text-gray-900">{company.defaultTimezone}</div></div>
                  <div><div className="text-xs text-gray-500">Default Currency</div><div className="text-sm font-medium text-gray-900">{company.defaultCurrency}</div></div>
                  <div><div className="text-xs text-gray-500">Date Format</div><div className="text-sm font-medium text-gray-900">{company.dateFormat || 'yyyy-MM-dd'}</div></div>
                  <div><div className="text-xs text-gray-500">Time Format</div><div className="text-sm font-medium text-gray-900">{company.timeFormat || 'HH:mm'}</div></div>
                  <div><div className="text-xs text-gray-500">Number Format</div><div className="text-sm font-medium text-gray-900">{company.numberFormat || 'en-US'}</div></div>
                </div>
              </div>
            )}

            {/* Subscription */}
            {tab === 'subscription' && (
              <div className="p-6 space-y-4">
                {subscription ? (
                  <>
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2 text-gray-900 font-semibold"><CreditCard className="w-4 h-4" /> Current Subscription</div>
                      <span className={`inline-flex px-2.5 py-1 rounded-full text-xs font-medium ${subStatusMap[subscription.status]?.color || 'bg-gray-100 text-gray-700'}`}>
                        {subStatusMap[subscription.status]?.label || 'Unknown'}
                      </span>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                      <div><div className="text-xs text-gray-500">Package</div><div className="text-sm font-medium text-gray-900">{subscription.packageName}</div></div>
                      <div><div className="text-xs text-gray-500">Price</div><div className="text-sm font-medium text-gray-900">${subscription.currentPrice}/{subscription.billingCycle === 'monthly' ? 'mo' : 'yr'}</div></div>
                      <div><div className="text-xs text-gray-500">Effective Price</div><div className="text-sm font-medium text-gray-900">${subscription.effectivePrice.toFixed(2)}</div></div>
                      <div><div className="text-xs text-gray-500">Start Date</div><div className="text-sm font-medium text-gray-900">{new Date(subscription.startDate).toLocaleDateString()}</div></div>
                      <div><div className="text-xs text-gray-500">End Date</div><div className="text-sm font-medium text-gray-900">{subscription.endDate ? new Date(subscription.endDate).toLocaleDateString() : 'No end date'}</div></div>
                      <div><div className="text-xs text-gray-500">Billing Cycle</div><div className="text-sm font-medium text-gray-900 capitalize">{subscription.billingCycle}</div></div>
                      {subscription.discountPercentage != null && subscription.discountPercentage > 0 && <div><div className="text-xs text-gray-500">Discount</div><div className="text-sm font-medium text-green-600">{subscription.discountPercentage}%</div></div>}
                      {subscription.taxPercentage != null && subscription.taxPercentage > 0 && <div><div className="text-xs text-gray-500">Tax</div><div className="text-sm font-medium text-gray-900">{subscription.taxPercentage}%</div></div>}
                    </div>
                    <div className="flex items-center gap-2 text-gray-900 font-semibold pt-4"><Users className="w-4 h-4" /> Limits Override</div>
                    <div className="grid grid-cols-3 gap-4">
                      <div><div className="text-xs text-gray-500">Max Users</div><div className="text-sm font-medium text-gray-900">{subscription.maxUsers ?? 'Use package default'}</div></div>
                      <div><div className="text-xs text-gray-500">Max Vehicles</div><div className="text-sm font-medium text-gray-900">{subscription.maxVehicles ?? 'Use package default'}</div></div>
                      <div><div className="text-xs text-gray-500">Max Drivers</div><div className="text-sm font-medium text-gray-900">{subscription.maxDrivers ?? 'Use package default'}</div></div>
                    </div>
                    {subscription.canceledAt && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">Canceled on {new Date(subscription.canceledAt).toLocaleDateString()}</div>}
                  </>
                ) : (
                  <div className="text-center py-12">
                    <CreditCard className="w-12 h-12 text-gray-300 mx-auto mb-3" />
                    <p className="text-gray-500 mb-4">No subscription assigned to this company</p>
                    <button onClick={() => setSubModal(true)} className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">Assign Subscription</button>
                  </div>
                )}
              </div>
            )}

            {/* Users */}
            {tab === 'users' && (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-gray-50 border-b border-gray-200">
                    <tr>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">User</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Email</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Roles</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Last Login</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Status</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {users.length === 0 ? (
                      <tr><td colSpan={6} className="text-center py-8 text-gray-400">No users</td></tr>
                    ) : users.map((u: any) => (
                      <tr key={u.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            <div className="w-8 h-8 bg-blue-600 rounded-full flex items-center justify-center text-white text-xs font-medium">{u.firstName?.[0]}{u.lastName?.[0]}</div>
                            <span className="text-sm font-medium text-gray-900">{u.firstName} {u.lastName}</span>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-600">{u.email}</td>
                        <td className="px-4 py-3">
                          <div className="flex flex-wrap gap-1">{u.roles?.map((r: string, i: number) => <span key={i} className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-purple-100 text-purple-700">{r}</span>)}</div>
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-500">{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleDateString() : 'Never'}</td>
                        <td className="px-4 py-3">
                          <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${u.status === 0 ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-700'}`}>{u.status === 0 ? 'Active' : 'Inactive'}</span>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-1">
                            <button onClick={() => setUserModal({ open: true, edit: u })} className="p-1.5 hover:bg-gray-100 rounded-lg"><Pencil className="w-4 h-4 text-gray-500" /></button>
                            <button onClick={() => setDeleteConfirm({ type: 'user', id: u.id, name: `${u.firstName} ${u.lastName}` })} className="p-1.5 hover:bg-gray-100 rounded-lg"><Trash2 className="w-4 h-4 text-red-500" /></button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {/* Roles */}
            {tab === 'roles' && (
              <div className="divide-y divide-gray-100">
                {roles.length === 0 ? (
                  <div className="text-center py-8 text-gray-400">No roles</div>
                ) : roles.map((r: any) => (
                  <div key={r.id} className="px-5 py-4 hover:bg-gray-50">
                    <div className="flex items-center justify-between mb-1">
                      <div className="flex items-center gap-2">
                        <Shield className="w-4 h-4 text-purple-600" />
                        <span className="text-sm font-semibold text-gray-900">{r.name}</span>
                        {r.isSystemRole && <span className="px-1.5 py-0.5 bg-amber-100 text-amber-700 text-xs font-medium rounded">System</span>}
                      </div>
                      <div className="flex items-center gap-3">
                        <span className="text-xs text-gray-400">{r.userCount} users &bull; {r.permissionCount} perms</span>
                        {!r.isSystemRole && (
                          <div className="flex items-center gap-1">
                            <button onClick={() => setRoleModal({ open: true, edit: r })} className="p-1.5 hover:bg-gray-100 rounded-lg"><Pencil className="w-4 h-4 text-gray-500" /></button>
                            <button onClick={() => setDeleteConfirm({ type: 'role', id: r.id, name: r.name })} className="p-1.5 hover:bg-gray-100 rounded-lg"><Trash2 className="w-4 h-4 text-red-500" /></button>
                          </div>
                        )}
                      </div>
                    </div>
                    <p className="text-xs text-gray-500 mb-2">{r.description || 'No description'}</p>
                    {r.permissions?.length > 0 && (
                      <div className="flex flex-wrap gap-1">
                        {r.permissions.slice(0, 12).map((p: any, i: number) => <span key={i} className="inline-flex px-1.5 py-0.5 rounded text-xs font-medium bg-blue-50 text-blue-700">{p.code}</span>)}
                        {r.permissions.length > 12 && <span className="text-xs text-gray-400">+{r.permissions.length - 12} more</span>}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}

            {/* Modules */}
            {tab === 'modules' && modules && (
              <div className="divide-y divide-gray-100">
                {modules.allModules?.map((m: any) => {
                  const enabled = modules.enabledModuleIds?.includes(m.id);
                  return (
                    <div key={m.id} className="px-5 py-4 flex items-center justify-between hover:bg-gray-50">
                      <div className="flex items-center gap-3">
                        <div className={`w-2.5 h-2.5 rounded-full ${enabled ? 'bg-green-500' : 'bg-gray-300'}`} />
                        <div>
                          <div className="flex items-center gap-2">
                            <span className="text-sm font-semibold text-gray-900">{m.name}</span>
                            {m.isCore && <span className="px-1.5 py-0.5 bg-purple-100 text-purple-700 text-xs font-medium rounded">Core</span>}
                          </div>
                          <p className="text-xs text-gray-500">{m.description || m.code} &bull; v{m.moduleVersion}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-4">
                        <span className="text-xs text-gray-400">{m.featureCount} features</span>
                        <button onClick={async () => {
                          if (enabled) { await api.delete(`/admin/companies/${id}/modules/${m.id}`); } else { await api.post(`/admin/companies/${id}/modules/${m.id}/enable`); }
                          fetchTab();
                        }} className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${enabled ? 'bg-green-50 text-green-700 hover:bg-green-100' : 'bg-gray-100 text-gray-500 hover:bg-gray-200'}`}>
                          {enabled ? (<><ToggleRight className="w-4 h-4" /> Enabled</>) : (<><ToggleLeft className="w-4 h-4" /> Enable</>)}
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}

            {/* Packages */}
            {tab === 'packages' && (
              <div className="p-6 space-y-6">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Package className="w-4 h-4" /> Assigned Package</div>
                {subscription ? (
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                    <div><div className="text-xs text-gray-500">Package</div><div className="text-sm font-medium text-gray-900">{subscription.packageName}</div></div>
                    <div><div className="text-xs text-gray-500">Price</div><div className="text-sm font-medium text-gray-900">${subscription.currentPrice}/{subscription.billingCycle === 'monthly' ? 'mo' : 'yr'}</div></div>
                    <div><div className="text-xs text-gray-500">Status</div><div><span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${subscription.status === 0 ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>{subscription.status === 0 ? 'Active' : 'Inactive'}</span></div></div>
                    <div><div className="text-xs text-gray-500">Start Date</div><div className="text-sm font-medium text-gray-900">{new Date(subscription.startDate).toLocaleDateString()}</div></div>
                    <div><div className="text-xs text-gray-500">End Date</div><div className="text-sm font-medium text-gray-900">{subscription.endDate ? new Date(subscription.endDate).toLocaleDateString() : 'No end date'}</div></div>
                    <div><div className="text-xs text-gray-500">Billing Cycle</div><div className="text-sm font-medium text-gray-900 capitalize">{subscription.billingCycle}</div></div>
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <Package className="w-12 h-12 text-gray-300 mx-auto mb-3" />
                    <p className="text-gray-500 mb-4">No package assigned</p>
                    <button onClick={() => setSubModal(true)} className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">Assign Package</button>
                  </div>
                )}
                {packages.length > 0 && (
                  <>
                    <div className="flex items-center gap-2 text-gray-900 font-semibold pt-4"><Package className="w-4 h-4" /> Available Packages</div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                      {packages.map(p => (
                        <div key={p.id} className="border border-gray-200 rounded-xl p-4 hover:border-blue-300 transition-colors">
                          <div className="flex items-center justify-between mb-2">
                            <span className="text-sm font-semibold text-gray-900">{p.name}</span>
                            <span className="text-sm font-bold text-blue-600">${p.price}</span>
                          </div>
                          <p className="text-xs text-gray-500 mb-3">{p.billingCycle} &bull; {p.maxUsers} users &bull; {p.maxVehicles} vehicles</p>
                          <button onClick={() => setSubModal(true)} className="w-full px-3 py-1.5 bg-gray-100 hover:bg-gray-200 rounded-lg text-xs font-medium text-gray-700 transition-colors">Assign</button>
                        </div>
                      ))}
                    </div>
                  </>
                )}
              </div>
            )}

            {/* Localization */}
            {tab === 'localization' && (
              <div className="p-6 space-y-6">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Globe className="w-4 h-4" /> Language & Region</div>
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                  <div><div className="text-xs text-gray-500 mb-1">Default Language</div><div className="text-sm font-medium text-gray-900 uppercase">{company.defaultLanguage}</div></div>
                  <div><div className="text-xs text-gray-500 mb-1">Default Timezone</div><div className="text-sm font-medium text-gray-900">{company.defaultTimezone}</div></div>
                  <div><div className="text-xs text-gray-500 mb-1">Default Currency</div><div className="text-sm font-medium text-gray-900">{company.defaultCurrency}</div></div>
                  <div><div className="text-xs text-gray-500 mb-1">Date Format</div><div className="text-sm font-medium text-gray-900">{company.dateFormat || 'yyyy-MM-dd'}</div></div>
                  <div><div className="text-xs text-gray-500 mb-1">Time Format</div><div className="text-sm font-medium text-gray-900">{company.timeFormat || 'HH:mm'}</div></div>
                  <div><div className="text-xs text-gray-500 mb-1">Number Format</div><div className="text-sm font-medium text-gray-900">{company.numberFormat || 'en-US'}</div></div>
                </div>
                <button onClick={() => setEditCompany(true)} className="flex items-center gap-2 px-4 py-2 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm font-medium text-gray-700 transition-colors"><Pencil className="w-4 h-4" /> Edit Localization Settings</button>
                {languages.length > 0 && (
                  <>
                    <div className="flex items-center gap-2 text-gray-900 font-semibold pt-4"><Languages className="w-4 h-4" /> Available Languages</div>
                    <div className="flex flex-wrap gap-2">
                      {languages.map((l: any) => (
                        <span key={l.code || l.id} className={`inline-flex px-3 py-1.5 rounded-lg text-xs font-medium border ${l.code === company.defaultLanguage ? 'bg-blue-50 border-blue-200 text-blue-700' : 'bg-gray-50 border-gray-200 text-gray-600'}`}>{l.name || l.code}</span>
                      ))}
                    </div>
                  </>
                )}
                {currencies.length > 0 && (
                  <>
                    <div className="flex items-center gap-2 text-gray-900 font-semibold pt-4"><DollarSign className="w-4 h-4" /> Available Currencies</div>
                    <div className="flex flex-wrap gap-2">
                      {currencies.map((c: any) => (
                        <span key={c.code || c.id} className={`inline-flex px-3 py-1.5 rounded-lg text-xs font-medium border ${c.code === company.defaultCurrency ? 'bg-blue-50 border-blue-200 text-blue-700' : 'bg-gray-50 border-gray-200 text-gray-600'}`}>{c.code} — {c.name || c.symbol}</span>
                      ))}
                    </div>
                  </>
                )}
              </div>
            )}

            {/* Settings */}
            {tab === 'settings' && (
              <div className="p-6 space-y-6">
                <div className="flex items-center gap-2 text-gray-900 font-semibold"><Settings className="w-4 h-4" /> Company Settings</div>
                {allModules.length > 0 ? (
                  <div className="space-y-4">
                    <div className="text-sm text-gray-500">Configure which modules and features are enabled for this company.</div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      {allModules.map((m: any) => (
                        <div key={m.id} className="border border-gray-200 rounded-xl p-4">
                          <div className="flex items-center gap-2 mb-2">
                            <span className="text-sm font-semibold text-gray-900">{m.name}</span>
                            {m.isCore && <span className="px-1.5 py-0.5 bg-purple-100 text-purple-700 text-xs font-medium rounded">Core</span>}
                          </div>
                          <p className="text-xs text-gray-500 mb-3">{m.description || m.code}</p>
                          <div className="text-xs text-gray-400">v{m.moduleVersion} &bull; {m.featureCount} features</div>
                        </div>
                      ))}
                    </div>
                  </div>
                ) : (
                  <div className="text-center py-8">
                    <Settings className="w-12 h-12 text-gray-300 mx-auto mb-3" />
                    <p className="text-gray-500">No modules configured</p>
                  </div>
                )}
              </div>
            )}

            {/* Documents */}
            {tab === 'documents' && (
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead className="bg-gray-50 border-b border-gray-200">
                    <tr>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">File</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Category</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Type</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Size</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Expiry</th>
                      <th className="text-left px-4 py-3 text-xs font-medium text-gray-500 uppercase">Uploaded</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {documents.length === 0 ? (
                      <tr><td colSpan={6} className="text-center py-8 text-gray-400">No documents uploaded</td></tr>
                    ) : documents.map((d: any) => (
                      <tr key={d.id} className="hover:bg-gray-50">
                        <td className="px-4 py-3 text-sm font-medium text-gray-900">{d.fileName}</td>
                        <td className="px-4 py-3 text-sm text-gray-600">{d.category || '\u2014'}</td>
                        <td className="px-4 py-3 text-sm text-gray-500">{d.contentType}</td>
                        <td className="px-4 py-3 text-sm text-gray-500">{(d.fileSize / 1024).toFixed(1)} KB</td>
                        <td className="px-4 py-3 text-sm text-gray-500">{d.expiryDate ? new Date(d.expiryDate).toLocaleDateString() : '\u2014'}</td>
                        <td className="px-4 py-3 text-sm text-gray-500">{new Date(d.createdAt).toLocaleDateString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </div>

      {/* Modals */}
      {editCompany && <CompanyEditModal company={company} onClose={() => setEditCompany(false)} onSaved={() => { setEditCompany(false); fetchCompany(); }} />}
      {userModal.open && <UserModal companyId={id!} user={userModal.edit} roles={rolesList} onClose={() => setUserModal({ open: false })} onSaved={() => { setUserModal({ open: false }); fetchTab(); }} />}
      {roleModal.open && <RoleModal companyId={id!} role={roleModal.edit} allPerms={allPerms} onClose={() => setRoleModal({ open: false })} onSaved={() => { setRoleModal({ open: false }); fetchTab(); }} />}
      {subModal && <SubscriptionModal companyId={id!} subscription={subscription} packages={packages} onClose={() => setSubModal(false)} onSaved={() => { setSubModal(false); fetchTab(); }} />}

      {/* Delete Confirmation */}
      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setDeleteConfirm(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete {deleteConfirm.type}</h3>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete <strong>{deleteConfirm.name}</strong>?</p>
            <div className="flex justify-end gap-3">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm.type, deleteConfirm.id)} className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default function CompanyDetail() {
  return (
    <CompanyDetailBoundary>
      <CompanyDetailInner />
    </CompanyDetailBoundary>
  );
}
