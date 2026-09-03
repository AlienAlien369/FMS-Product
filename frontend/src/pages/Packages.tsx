import { useEffect, useState, useCallback } from 'react';
import api from '../lib/api';
import { Search, Plus, Pencil, Trash2, ChevronLeft, ChevronRight, Package, X, Check, DollarSign, Users, Truck, Clock, Headphones, Zap, ChevronDown, ChevronUp } from 'lucide-react';

interface PackageItem {
  id: string; name: string; description?: string; shortDescription?: string;
  highlights?: string; termsOfServiceUrl?: string; welcomeMessage?: string;
  price: number; currency: string; billingCycle: string;
  yearlyPrice?: number; setupFee?: number; minCommitment?: number;
  status: number; displayOrder: number; isDefault: boolean; isCustom: boolean;
  trialDays: number; allowTrialExtension: boolean; maxTrialExtensions: number; trialExtensionDays: number;
  maxUsers: number; maxVehicles: number; maxDrivers: number;
  maxTripsPerDay: number; maxRoutes: number; maxReportsPerDay: number;
  storageLimitMb: number; maxApiCallsPerDay: number; maxTrackingDevices: number;
  maxAlertRules: number; maxGeofences: number; maxDocuments: number; maxNotificationsPerDay: number;
  overagePricePerUser: number; overagePricePerVehicle: number; overagePricePerDriver: number;
  overagePricePerTrip: number; overagePricePerApiCall: number; overagePricePerGbStorage: number;
  supportLevel: string; slaUptimePercent: number; supportHours?: string;
  supportContactEmail?: string; supportContactPhone?: string;
  responseTimeHours: number; resolutionTimeHours: number;
  enableLiveTracking: boolean; enableGeofencing: boolean; enableAlerts: boolean; enableReports: boolean;
  enableFuelMonitoring: boolean; enableMaintenance: boolean; enableRouteOptimization: boolean;
  enableProofOfDelivery: boolean; enableCctv: boolean; enableSmsNotifications: boolean;
  enableEmailNotifications: boolean; enableWebhookIntegrations: boolean; enableApiAccess: boolean;
  enableBulkImport: boolean; enableExport: boolean; enableCustomFields: boolean;
  enableMultiCompany: boolean; enableAuditLog: boolean;
  activeSubscriptions: number; createdAt: string;
}

interface PagedData { items: PackageItem[]; totalCount: number; page: number; pageSize: number; totalPages: number; hasPrevious: boolean; hasNext: boolean; }

export default function Packages() {
  const [data, setData] = useState<PagedData | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [modal, setModal] = useState<{ open: boolean; edit?: PackageItem }>({ open: false });
  const [deleteConfirm, setDeleteConfirm] = useState<PackageItem | null>(null);
  const [detailView, setDetailView] = useState<PackageItem | null>(null);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api.get(`/admin/packages?page=${page}&pageSize=10&search=${search}`);
      setData(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, search]);

  useEffect(() => { fetchData(); }, [fetchData]);

  const handleDelete = async (id: string) => {
    try { await api.delete(`/admin/packages/${id}`); setDeleteConfirm(null); fetchData(); }
    catch (e: any) { alert(e.response?.data?.message || 'Failed to delete package'); }
  };

  const supportColors: Record<string, string> = { basic: 'bg-gray-100 text-gray-700', standard: 'bg-blue-100 text-blue-700', premium: 'bg-purple-100 text-purple-700', enterprise: 'bg-amber-100 text-amber-700' };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-gray-900">Packages</h2>
        <p className="text-gray-500 text-sm mt-1">Manage subscription packages, pricing, limits, and features</p>
      </div>

      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="relative flex-1 max-w-md">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input type="text" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500"
            placeholder="Search packages..." />
        </div>
        <button onClick={() => setModal({ open: true })}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 transition-colors">
          <Plus className="w-4 h-4" /> Create Package
        </button>
      </div>

      {/* Package Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        {loading ? (
          <div className="col-span-full text-center py-12 text-gray-400">Loading...</div>
        ) : data?.items?.length === 0 ? (
          <div className="col-span-full text-center py-12 text-gray-400">No packages found</div>
        ) : (
          data?.items?.map(p => (
            <div key={p.id} className="bg-white rounded-xl border border-gray-200 p-5 hover:shadow-md transition-shadow cursor-pointer" onClick={() => setDetailView(p)}>
              <div className="flex items-start justify-between mb-3">
                <div className="flex items-center gap-3">
                  <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${p.isDefault ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-600'}`}>
                    <Package className="w-5 h-5" />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold text-gray-900">{p.name}</span>
                      {p.isDefault && <span className="px-1.5 py-0.5 bg-blue-100 text-blue-700 text-xs font-medium rounded">Default</span>}
                    </div>
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-xs font-medium ${supportColors[p.supportLevel] || 'bg-gray-100 text-gray-700'}`}>{p.supportLevel}</span>
                  </div>
                </div>
                <div className="flex items-center gap-1" onClick={e => e.stopPropagation()}>
                  <button onClick={() => setModal({ open: true, edit: p })} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Edit"><Pencil className="w-4 h-4 text-gray-500" /></button>
                  <button onClick={() => setDeleteConfirm(p)} className="p-1.5 hover:bg-gray-100 rounded-lg" title="Delete"><Trash2 className="w-4 h-4 text-red-500" /></button>
                </div>
              </div>
              <p className="text-xs text-gray-500 mb-3 line-clamp-2">{p.description || 'No description'}</p>
              <div className="grid grid-cols-3 gap-2 text-center mb-3">
                <div className="bg-gray-50 rounded-lg p-2"><div className="text-lg font-bold text-gray-900">${p.price}</div><div className="text-xs text-gray-400">/{p.billingCycle === 'monthly' ? 'mo' : 'yr'}</div></div>
                <div className="bg-gray-50 rounded-lg p-2"><div className="text-lg font-bold text-gray-900">{p.maxUsers === -1 ? '∞' : p.maxUsers}</div><div className="text-xs text-gray-400">Users</div></div>
                <div className="bg-gray-50 rounded-lg p-2"><div className="text-lg font-bold text-gray-900">{p.maxVehicles === -1 ? '∞' : p.maxVehicles}</div><div className="text-xs text-gray-400">Vehicles</div></div>
              </div>
              <div className="flex items-center justify-between text-xs text-gray-400">
                <span>{p.trialDays > 0 ? `${p.trialDays}-day trial` : 'No trial'}</span>
                <span>{p.activeSubscriptions} active subscriptions</span>
              </div>
            </div>
          ))
        )}
      </div>

      {data && (
        <div className="flex items-center justify-between">
          <span className="text-sm text-gray-500">Showing {data.items.length} of {data.totalCount} packages</span>
          <div className="flex items-center gap-2">
            <button disabled={!data.hasPrevious} onClick={() => setPage(p => p - 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronLeft className="w-4 h-4" /></button>
            <span className="text-sm text-gray-600">Page {data.page} of {data.totalPages}</span>
            <button disabled={!data.hasNext} onClick={() => setPage(p => p + 1)} className="p-1 rounded hover:bg-gray-100 disabled:opacity-30"><ChevronRight className="w-4 h-4" /></button>
          </div>
        </div>
      )}

      {/* Detail View Modal */}
      {detailView && (
        <PackageDetailModal pkg={detailView} onClose={() => setDetailView(null)} onEdit={() => { setModal({ open: true, edit: detailView }); setDetailView(null); }} />
      )}

      {modal.open && <PackageModal edit={modal.edit} onClose={() => setModal({ open: false })} onSaved={() => { setModal({ open: false }); fetchData(); }} />}

      {deleteConfirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <div className="fixed inset-0 bg-black/50" onClick={() => setDeleteConfirm(null)} />
          <div className="relative bg-white rounded-xl shadow-2xl p-6 w-full max-w-sm">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Delete Package</h3>
            <p className="text-sm text-gray-600 mb-4">Are you sure you want to delete <strong>{deleteConfirm.name}</strong>? {deleteConfirm.activeSubscriptions > 0 && <span className="text-red-600">This package has {deleteConfirm.activeSubscriptions} active subscriptions.</span>}</p>
            <div className="flex justify-end gap-3">
              <button onClick={() => setDeleteConfirm(null)} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
              <button onClick={() => handleDelete(deleteConfirm.id)} className="px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-lg hover:bg-red-700">Delete</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── Detail View Modal ───────────────────────────────────
function PackageDetailModal({ pkg, onClose, onEdit }: { pkg: PackageItem; onClose: () => void; onEdit: () => void }) {
  const [section, setSection] = useState<'overview' | 'limits' | 'pricing' | 'features'>('overview');
  const featureFlags = [
    { key: 'enableLiveTracking', label: 'Live Tracking' }, { key: 'enableGeofencing', label: 'Geofencing' },
    { key: 'enableAlerts', label: 'Alerts' }, { key: 'enableReports', label: 'Reports' },
    { key: 'enableFuelMonitoring', label: 'Fuel Monitoring' }, { key: 'enableMaintenance', label: 'Maintenance' },
    { key: 'enableRouteOptimization', label: 'Route Optimization' }, { key: 'enableProofOfDelivery', label: 'Proof of Delivery' },
    { key: 'enableCctv', label: 'CCTV / Video' }, { key: 'enableSmsNotifications', label: 'SMS Notifications' },
    { key: 'enableEmailNotifications', label: 'Email Notifications' }, { key: 'enableWebhookIntegrations', label: 'Webhooks' },
    { key: 'enableApiAccess', label: 'API Access' }, { key: 'enableBulkImport', label: 'Bulk Import' },
    { key: 'enableExport', label: 'Export' }, { key: 'enableCustomFields', label: 'Custom Fields' },
    { key: 'enableMultiCompany', label: 'Multi-Company' }, { key: 'enableAuditLog', label: 'Audit Log' },
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-3xl max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">{pkg.name}</h2>
            <p className="text-xs text-gray-400">{pkg.billingCycle} &bull; {pkg.supportLevel} support &bull; SLA {pkg.slaUptimePercent}%</p>
          </div>
          <div className="flex items-center gap-2">
            <button onClick={onEdit} className="px-3 py-1.5 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm font-medium text-gray-700"><Pencil className="w-4 h-4 inline mr-1" />Edit</button>
            <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
          </div>
        </div>
        <div className="flex gap-1 px-6 pt-3 border-b border-gray-200">
          {(['overview', 'limits', 'pricing', 'features'] as const).map(s => (
            <button key={s} onClick={() => setSection(s)} className={`px-4 py-2 text-sm font-medium rounded-t-lg ${section === s ? 'bg-white border border-gray-200 border-b-white -mb-px text-gray-900' : 'text-gray-500 hover:text-gray-700'}`}>{s.charAt(0).toUpperCase() + s.slice(1)}</button>
          ))}
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4">
          {section === 'overview' && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 sm:grid-cols-3 gap-4">
                <div><div className="text-xs text-gray-500">Price</div><div className="text-sm font-medium text-gray-900">${pkg.price}/{pkg.billingCycle === 'monthly' ? 'mo' : 'yr'}</div></div>
                {pkg.yearlyPrice && <div><div className="text-xs text-gray-500">Yearly Price</div><div className="text-sm font-medium text-gray-900">${pkg.yearlyPrice}/yr</div></div>}
                {pkg.setupFee && <div><div className="text-xs text-gray-500">Setup Fee</div><div className="text-sm font-medium text-gray-900">${pkg.setupFee}</div></div>}
                {pkg.minCommitment && <div><div className="text-xs text-gray-500">Min Commitment</div><div className="text-sm font-medium text-gray-900">${pkg.minCommitment}/mo</div></div>}
                <div><div className="text-xs text-gray-500">Currency</div><div className="text-sm font-medium text-gray-900">{pkg.currency}</div></div>
                <div><div className="text-xs text-gray-500">Trial</div><div className="text-sm font-medium text-gray-900">{pkg.trialDays > 0 ? `${pkg.trialDays} days` : 'None'}</div></div>
              </div>
              {pkg.description && <div><div className="text-xs text-gray-500 mb-1">Description</div><p className="text-sm text-gray-700">{pkg.description}</p></div>}
              {pkg.welcomeMessage && <div><div className="text-xs text-gray-500 mb-1">Welcome Message</div><p className="text-sm text-gray-700">{pkg.welcomeMessage}</p></div>}
            </div>
          )}
          {section === 'limits' && (
            <div className="space-y-4">
              <div className="text-sm font-semibold text-gray-900">Resource Limits</div>
              <div className="grid grid-cols-3 gap-3">
                {[['Max Users', pkg.maxUsers], ['Max Vehicles', pkg.maxVehicles], ['Max Drivers', pkg.maxDrivers], ['Trips/Day', pkg.maxTripsPerDay], ['Max Routes', pkg.maxRoutes], ['Reports/Day', pkg.maxReportsPerDay], ['Storage', `${pkg.storageLimitMb}MB`], ['API Calls/Day', pkg.maxApiCallsPerDay], ['Tracking Devices', pkg.maxTrackingDevices], ['Alert Rules', pkg.maxAlertRules], ['Geofences', pkg.maxGeofences], ['Documents', pkg.maxDocuments], ['Notifications/Day', pkg.maxNotificationsPerDay]].map(([label, val]) => (
                  <div key={String(label)} className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">{label}</div><div className="text-sm font-medium text-gray-900">{val === -1 || val === Infinity ? '∞' : String(val)}</div></div>
                ))}
              </div>
              <div className="text-sm font-semibold text-gray-900 pt-2">Support & SLA</div>
              <div className="grid grid-cols-3 gap-3">
                <div className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">Support Level</div><div className="text-sm font-medium text-gray-900 capitalize">{pkg.supportLevel}</div></div>
                <div className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">SLA Uptime</div><div className="text-sm font-medium text-gray-900">{pkg.slaUptimePercent}%</div></div>
                <div className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">Support Hours</div><div className="text-sm font-medium text-gray-900">{pkg.supportHours || '—'}</div></div>
                <div className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">Response Time</div><div className="text-sm font-medium text-gray-900">{pkg.responseTimeHours}h</div></div>
                <div className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">Resolution Time</div><div className="text-sm font-medium text-gray-900">{pkg.resolutionTimeHours}h</div></div>
              </div>
            </div>
          )}
          {section === 'pricing' && (
            <div className="space-y-4">
              <div className="text-sm font-semibold text-gray-900">Overage Pricing</div>
              <div className="grid grid-cols-3 gap-3">
                {[['Per User', pkg.overagePricePerUser], ['Per Vehicle', pkg.overagePricePerVehicle], ['Per Driver', pkg.overagePricePerDriver], ['Per Trip', pkg.overagePricePerTrip], ['Per API Call', pkg.overagePricePerApiCall], ['Per GB Storage', pkg.overagePricePerGbStorage]].map(([label, val]) => (
                  <div key={String(label)} className="bg-gray-50 rounded-lg p-3"><div className="text-xs text-gray-500">{label}</div><div className="text-sm font-medium text-gray-900">{Number(val) > 0 ? `$${val}` : 'Included'}</div></div>
                ))}
              </div>
            </div>
          )}
          {section === 'features' && (
            <div className="space-y-3">
              <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
                {featureFlags.map(f => (
                  <div key={f.key} className={`flex items-center gap-2 p-2 rounded-lg ${(pkg as any)[f.key] ? 'bg-green-50' : 'bg-gray-50'}`}>
                    <div className={`w-4 h-4 rounded flex items-center justify-center ${(pkg as any)[f.key] ? 'bg-green-500 text-white' : 'bg-gray-300'}`}>
                      {(pkg as any)[f.key] && <Check className="w-3 h-3" />}
                    </div>
                    <span className={`text-xs font-medium ${(pkg as any)[f.key] ? 'text-green-700' : 'text-gray-500'}`}>{f.label}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Create/Edit Modal ───────────────────────────────────
function PackageModal({ edit, onClose, onSaved }: { edit?: PackageItem; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!edit?.id;
  const [section, setSection] = useState<'basic' | 'pricing' | 'trial' | 'limits' | 'overage' | 'support' | 'features'>('basic');
  const [form, setForm] = useState({
    name: edit?.name || '', description: edit?.description || '', shortDescription: edit?.shortDescription || '',
    highlights: edit?.highlights || '', termsOfServiceUrl: edit?.termsOfServiceUrl || '', welcomeMessage: edit?.welcomeMessage || '',
    price: edit?.price ?? 0, currency: edit?.currency || 'USD', billingCycle: edit?.billingCycle || 'monthly',
    yearlyPrice: edit?.yearlyPrice ?? 0, setupFee: edit?.setupFee ?? 0, minCommitment: edit?.minCommitment ?? 0,
    displayOrder: edit?.displayOrder ?? 0, isDefault: edit?.isDefault || false,
    trialDays: edit?.trialDays ?? 0, allowTrialExtension: edit?.allowTrialExtension || false,
    maxTrialExtensions: edit?.maxTrialExtensions ?? 1, trialExtensionDays: edit?.trialExtensionDays ?? 7,
    maxUsers: edit?.maxUsers ?? 5, maxVehicles: edit?.maxVehicles ?? 10, maxDrivers: edit?.maxDrivers ?? 10,
    maxTripsPerDay: edit?.maxTripsPerDay ?? 50, maxRoutes: edit?.maxRoutes ?? 10, maxReportsPerDay: edit?.maxReportsPerDay ?? 20,
    storageLimitMb: edit?.storageLimitMb ?? 1024, maxApiCallsPerDay: edit?.maxApiCallsPerDay ?? 1000,
    maxTrackingDevices: edit?.maxTrackingDevices ?? 10, maxAlertRules: edit?.maxAlertRules ?? 20,
    maxGeofences: edit?.maxGeofences ?? 10, maxDocuments: edit?.maxDocuments ?? 100, maxNotificationsPerDay: edit?.maxNotificationsPerDay ?? 500,
    overagePricePerUser: edit?.overagePricePerUser ?? 0, overagePricePerVehicle: edit?.overagePricePerVehicle ?? 0,
    overagePricePerDriver: edit?.overagePricePerDriver ?? 0, overagePricePerTrip: edit?.overagePricePerTrip ?? 0,
    overagePricePerApiCall: edit?.overagePricePerApiCall ?? 0, overagePricePerGbStorage: edit?.overagePricePerGbStorage ?? 0,
    supportLevel: edit?.supportLevel || 'basic', slaUptimePercent: edit?.slaUptimePercent ?? 99,
    supportHours: edit?.supportHours || '', supportContactEmail: edit?.supportContactEmail || '',
    supportContactPhone: edit?.supportContactPhone || '', responseTimeHours: edit?.responseTimeHours ?? 48,
    resolutionTimeHours: edit?.resolutionTimeHours ?? 72,
    enableLiveTracking: edit?.enableLiveTracking ?? true, enableGeofencing: edit?.enableGeofencing ?? true,
    enableAlerts: edit?.enableAlerts ?? true, enableReports: edit?.enableReports ?? true,
    enableFuelMonitoring: edit?.enableFuelMonitoring ?? false, enableMaintenance: edit?.enableMaintenance ?? false,
    enableRouteOptimization: edit?.enableRouteOptimization ?? false, enableProofOfDelivery: edit?.enableProofOfDelivery ?? false,
    enableCctv: edit?.enableCctv ?? false, enableSmsNotifications: edit?.enableSmsNotifications ?? false,
    enableEmailNotifications: edit?.enableEmailNotifications ?? true, enableWebhookIntegrations: edit?.enableWebhookIntegrations ?? false,
    enableApiAccess: edit?.enableApiAccess ?? true, enableBulkImport: edit?.enableBulkImport ?? false,
    enableExport: edit?.enableExport ?? true, enableCustomFields: edit?.enableCustomFields ?? false,
    enableMultiCompany: edit?.enableMultiCompany ?? false, enableAuditLog: edit?.enableAuditLog ?? true,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }));
  const input = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
  const label = "block text-sm font-medium text-gray-700 mb-1";
  const numInput = input + " [appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none";

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Package name required'); return; }
    setSaving(true); setError('');
    try {
      if (isEdit) { await api.put(`/admin/packages/${edit!.id}`, form); }
      else { await api.post('/admin/packages', form); }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed'); }
    setSaving(false);
  };

  const featureFlags = ['enableLiveTracking', 'enableGeofencing', 'enableAlerts', 'enableReports', 'enableFuelMonitoring', 'enableMaintenance', 'enableRouteOptimization', 'enableProofOfDelivery', 'enableCctv', 'enableSmsNotifications', 'enableEmailNotifications', 'enableWebhookIntegrations', 'enableApiAccess', 'enableBulkImport', 'enableExport', 'enableCustomFields', 'enableMultiCompany', 'enableAuditLog'];

  const sections = [
    { key: 'basic' as const, label: 'Basic', icon: Package }, { key: 'pricing' as const, label: 'Pricing', icon: DollarSign },
    { key: 'trial' as const, label: 'Trial', icon: Clock }, { key: 'limits' as const, label: 'Limits', icon: Users },
    { key: 'overage' as const, label: 'Overage', icon: DollarSign }, { key: 'support' as const, label: 'Support', icon: Headphones },
    { key: 'features' as const, label: 'Features', icon: Zap },
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-3xl max-h-[90vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">{isEdit ? 'Edit Package' : 'Create Package'}</h2>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="flex gap-1 px-6 pt-3 border-b border-gray-200 overflow-x-auto">
          {sections.map(s => {
            const Icon = s.icon;
            return <button key={s.key} onClick={() => setSection(s.key)} className={`flex items-center gap-1.5 px-3 py-2 text-sm font-medium rounded-t-lg whitespace-nowrap ${section === s.key ? 'bg-white border border-gray-200 border-b-white -mb-px text-gray-900' : 'text-gray-500 hover:text-gray-700'}`}><Icon className="w-3.5 h-3.5" />{s.label}</button>;
          })}
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}

          {section === 'basic' && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div><label className={label}>Name *</label><input className={input} value={form.name} onChange={e => set('name', e.target.value)} placeholder="e.g. Professional" /></div>
                <div><label className={label}>Short Description</label><input className={input} value={form.shortDescription} onChange={e => set('shortDescription', e.target.value)} placeholder="One-liner" /></div>
              </div>
              <div><label className={label}>Description</label><textarea className={input + " h-20 resize-none"} value={form.description} onChange={e => set('description', e.target.value)} placeholder="Full description" /></div>
              <div><label className={label}>Welcome Message</label><textarea className={input + " h-16 resize-none"} value={form.welcomeMessage} onChange={e => set('welcomeMessage', e.target.value)} placeholder="Shown after subscription" /></div>
              <div className="grid grid-cols-2 gap-4">
                <div><label className={label}>Display Order</label><input className={numInput} type="number" min="0" value={form.displayOrder} onChange={e => set('displayOrder', parseInt(e.target.value) || 0)} /></div>
                <div className="flex items-center gap-2 pt-6"><input type="checkbox" checked={form.isDefault} onChange={e => set('isDefault', e.target.checked)} className="w-4 h-4 text-blue-600 rounded" /><span className="text-sm font-medium text-gray-700">Default Package</span></div>
              </div>
            </div>
          )}

          {section === 'pricing' && (
            <div className="space-y-4">
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Price ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.price} onChange={e => set('price', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Currency</label><select className={input} value={form.currency} onChange={e => set('currency', e.target.value)}><option value="USD">USD</option><option value="EUR">EUR</option><option value="GBP">GBP</option><option value="INR">INR</option><option value="AED">AED</option></select></div>
                <div><label className={label}>Billing Cycle</label><select className={input} value={form.billingCycle} onChange={e => set('billingCycle', e.target.value)}><option value="monthly">Monthly</option><option value="yearly">Yearly</option><option value="custom">Custom</option></select></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Yearly Price ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.yearlyPrice} onChange={e => set('yearlyPrice', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Setup Fee ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.setupFee} onChange={e => set('setupFee', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Min Commitment ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.minCommitment} onChange={e => set('minCommitment', parseFloat(e.target.value) || 0)} /></div>
              </div>
            </div>
          )}

          {section === 'trial' && (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div><label className={label}>Trial Days</label><input className={numInput} type="number" min="0" value={form.trialDays} onChange={e => set('trialDays', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Trial Extension Days</label><input className={numInput} type="number" min="0" value={form.trialExtensionDays} onChange={e => set('trialExtensionDays', parseInt(e.target.value) || 0)} /></div>
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex items-center gap-2 pt-6"><input type="checkbox" checked={form.allowTrialExtension} onChange={e => set('allowTrialExtension', e.target.checked)} className="w-4 h-4 text-blue-600 rounded" /><span className="text-sm font-medium text-gray-700">Allow Trial Extension</span></div>
                <div><label className={label}>Max Trial Extensions</label><input className={numInput} type="number" min="0" value={form.maxTrialExtensions} onChange={e => set('maxTrialExtensions', parseInt(e.target.value) || 0)} /></div>
              </div>
            </div>
          )}

          {section === 'limits' && (
            <div className="space-y-4">
              <div className="text-sm font-semibold text-gray-900">Resource Limits</div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Max Users</label><input className={numInput} type="number" min="-1" value={form.maxUsers} onChange={e => set('maxUsers', parseInt(e.target.value) || 0)} /><p className="text-xs text-gray-400 mt-0.5">-1 = unlimited</p></div>
                <div><label className={label}>Max Vehicles</label><input className={numInput} type="number" min="-1" value={form.maxVehicles} onChange={e => set('maxVehicles', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Max Drivers</label><input className={numInput} type="number" min="-1" value={form.maxDrivers} onChange={e => set('maxDrivers', parseInt(e.target.value) || 0)} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Trips / Day</label><input className={numInput} type="number" min="0" value={form.maxTripsPerDay} onChange={e => set('maxTripsPerDay', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Max Routes</label><input className={numInput} type="number" min="0" value={form.maxRoutes} onChange={e => set('maxRoutes', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Reports / Day</label><input className={numInput} type="number" min="0" value={form.maxReportsPerDay} onChange={e => set('maxReportsPerDay', parseInt(e.target.value) || 0)} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Storage (MB)</label><input className={numInput} type="number" min="0" value={form.storageLimitMb} onChange={e => set('storageLimitMb', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>API Calls / Day</label><input className={numInput} type="number" min="0" value={form.maxApiCallsPerDay} onChange={e => set('maxApiCallsPerDay', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Tracking Devices</label><input className={numInput} type="number" min="0" value={form.maxTrackingDevices} onChange={e => set('maxTrackingDevices', parseInt(e.target.value) || 0)} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Alert Rules</label><input className={numInput} type="number" min="0" value={form.maxAlertRules} onChange={e => set('maxAlertRules', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Geofences</label><input className={numInput} type="number" min="0" value={form.maxGeofences} onChange={e => set('maxGeofences', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Documents</label><input className={numInput} type="number" min="0" value={form.maxDocuments} onChange={e => set('maxDocuments', parseInt(e.target.value) || 0)} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Notifications / Day</label><input className={numInput} type="number" min="0" value={form.maxNotificationsPerDay} onChange={e => set('maxNotificationsPerDay', parseInt(e.target.value) || 0)} /></div>
              </div>
            </div>
          )}

          {section === 'overage' && (
            <div className="space-y-4">
              <div className="text-sm text-gray-500">Price charged when company exceeds package limits (-1 = unlimited items never incur overage)</div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Per User ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.overagePricePerUser} onChange={e => set('overagePricePerUser', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Per Vehicle ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.overagePricePerVehicle} onChange={e => set('overagePricePerVehicle', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Per Driver ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.overagePricePerDriver} onChange={e => set('overagePricePerDriver', parseFloat(e.target.value) || 0)} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Per Trip ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.overagePricePerTrip} onChange={e => set('overagePricePerTrip', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Per API Call ($)</label><input className={numInput} type="number" min="0" step="0.0001" value={form.overagePricePerApiCall} onChange={e => set('overagePricePerApiCall', parseFloat(e.target.value) || 0)} /></div>
                <div><label className={label}>Per GB Storage ($)</label><input className={numInput} type="number" min="0" step="0.01" value={form.overagePricePerGbStorage} onChange={e => set('overagePricePerGbStorage', parseFloat(e.target.value) || 0)} /></div>
              </div>
            </div>
          )}

          {section === 'support' && (
            <div className="space-y-4">
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Support Level</label><select className={input} value={form.supportLevel} onChange={e => set('supportLevel', e.target.value)}><option value="basic">Basic</option><option value="standard">Standard</option><option value="premium">Premium</option><option value="enterprise">Enterprise</option></select></div>
                <div><label className={label}>SLA Uptime %</label><select className={input} value={form.slaUptimePercent} onChange={e => set('slaUptimePercent', parseInt(e.target.value))}><option value={99}>99%</option><option value={995}>99.5%</option><option value={999}>99.9%</option><option value={9999}>99.99%</option></select></div>
                <div><label className={label}>Support Hours</label><select className={input} value={form.supportHours} onChange={e => set('supportHours', e.target.value)}><option value="">—</option><option value="Business hours">Business hours</option><option value="Extended hours">Extended hours</option><option value="24/7">24/7</option></select></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={label}>Response Time (hrs)</label><input className={numInput} type="number" min="0" value={form.responseTimeHours} onChange={e => set('responseTimeHours', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Resolution Time (hrs)</label><input className={numInput} type="number" min="0" value={form.resolutionTimeHours} onChange={e => set('resolutionTimeHours', parseInt(e.target.value) || 0)} /></div>
                <div><label className={label}>Support Email</label><input className={input} type="email" value={form.supportContactEmail} onChange={e => set('supportContactEmail', e.target.value)} placeholder="support@example.com" /></div>
              </div>
              <div><label className={label}>Support Phone</label><input className={input} value={form.supportContactPhone} onChange={e => set('supportContactPhone', e.target.value)} placeholder="+1 (555) 123-4567" /></div>
            </div>
          )}

          {section === 'features' && (
            <div className="space-y-4">
              <div className="text-sm text-gray-500">Toggle which features are included in this package</div>
              <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
                {featureFlags.map(f => (
                  <label key={f} className="flex items-center gap-2 p-2 rounded-lg border border-gray-200 hover:border-blue-300 cursor-pointer transition-colors">
                    <input type="checkbox" checked={(form as any)[f]} onChange={e => set(f, e.target.checked)} className="w-4 h-4 text-blue-600 rounded" />
                    <span className="text-xs font-medium text-gray-700">{f.replace('enable', '').replace(/([A-Z])/g, ' $1').trim()}</span>
                  </label>
                ))}
              </div>
            </div>
          )}
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">{saving ? 'Saving...' : isEdit ? 'Update Package' : 'Create Package'}</button>
        </div>
      </div>
    </div>
  );
}
