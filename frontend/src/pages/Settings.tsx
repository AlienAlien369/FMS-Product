import { useEffect, useState } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import { Settings as SettingsIcon, Building2, Clock, Globe, DollarSign, MapPin, CreditCard } from 'lucide-react';
import { usePermissions } from '../hooks/usePermissions';
import { SUBSCRIPTION_STATUS } from '../lib/constants';

interface CompanySettings {
  id: string; name: string; slug: string; contactEmail?: string; contactPhone?: string;
  website?: string; address?: string; city?: string; state?: string; country?: string; postalCode?: string;
  defaultLanguage: string; defaultTimezone: string; defaultCurrency: string;
  dateFormat?: string; timeFormat?: string; status: number;
}

interface SubscriptionInfo {
  id: string; packageName: string; status: number; startDate: string; endDate?: string;
  currentPrice: number; currency: string; billingCycle: string; effectivePrice: number;
  discountPercentage?: number; taxPercentage?: number;
}

export default function Settings() {
  const { user } = useAuth();
  const { can } = usePermissions();
  const canEdit = can('settings.update');
  const [company, setCompany] = useState<CompanySettings | null>(null);
  const [subscription, setSubscription] = useState<SubscriptionInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');

  useEffect(() => {
    if (!user?.companyId) { setLoading(false); return; }
    Promise.all([
      api.get('/tenant/company').catch(() => null),
      api.get('/tenant/subscription').catch(() => null),
    ]).then(([companyRes, subRes]) => {
      if (companyRes?.data?.data) setCompany(companyRes.data.data);
      if (subRes?.data?.data) setSubscription(subRes.data.data);
    }).finally(() => setLoading(false));
  }, [user?.companyId]);

  const handleSave = async () => {
    if (!company) return;
    setSaving(true);
    setMessage('');
    try {
      await api.put(`/admin/companies/${company.id}/extended`, {
        name: company.name,
        contactEmail: company.contactEmail,
        contactPhone: company.contactPhone,
        website: company.website,
        address: company.address,
        city: company.city,
        state: company.state,
        country: company.country,
        postalCode: company.postalCode,
        defaultLanguage: company.defaultLanguage,
        defaultTimezone: company.defaultTimezone,
        defaultCurrency: company.defaultCurrency,
        dateFormat: company.dateFormat,
        timeFormat: company.timeFormat,
      });
      setMessage('Settings saved successfully');
      setTimeout(() => setMessage(''), 3000);
    } catch (e) { setMessage('Failed to save settings'); }
    setSaving(false);
  };

  if (loading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;

  if (!user?.companyId || user?.roles?.includes('SuperAdmin')) return (
    <div className="space-y-6 max-w-3xl">
      <h2 className="text-xl font-bold text-gray-900">Settings</h2>
      <div className="bg-white rounded-xl border border-gray-200 p-8 text-center">
        <Building2 className="w-12 h-12 mx-auto text-gray-300 mb-4" />
        <p className="text-gray-500">Settings are per-company. Go to <span className="font-medium text-gray-700">Admin &gt; Companies</span> to manage a company&apos;s settings.</p>
      </div>
    </div>
  );

  if (!company) return <div className="text-center py-12 text-gray-500">Could not load company data.</div>;

  const input = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500";
  const label = "block text-sm font-medium text-gray-700 mb-1";

  const subStatusMap = SUBSCRIPTION_STATUS;

  return (
    <div className="space-y-6 max-w-3xl">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold text-gray-900">Settings</h2>
        {canEdit && (
          <button onClick={handleSave} disabled={saving}
            className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700 disabled:bg-blue-400 transition-colors">
            {saving ? 'Saving...' : 'Save Changes'}
          </button>
        )}
      </div>

      {message && (
        <div className={`p-3 rounded-lg text-sm ${message.includes('success') ? 'bg-green-50 border border-green-200 text-green-700' : 'bg-red-50 border border-red-200 text-red-700'}`}>
          {message}
        </div>
      )}

      {/* Company Information */}
      <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-6">
        <div className="flex items-center gap-2 text-gray-900 font-semibold">
          <Building2 className="w-5 h-5" /> Company Information
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className={label}>Company Name</label>
            <input className={input} value={company.name} onChange={e => setCompany({ ...company, name: e.target.value })} />
          </div>
          <div>
            <label className={label}>Slug</label>
            <input className={input + ' bg-gray-50'} value={company.slug} disabled />
          </div>
          <div>
            <label className={label}>Contact Email</label>
            <input className={input} type="email" value={company.contactEmail || ''} onChange={e => setCompany({ ...company, contactEmail: e.target.value })} />
          </div>
          <div>
            <label className={label}>Contact Phone</label>
            <input className={input} value={company.contactPhone || ''} onChange={e => setCompany({ ...company, contactPhone: e.target.value })} />
          </div>
          <div>
            <label className={label}>Website</label>
            <input className={input} value={company.website || ''} onChange={e => setCompany({ ...company, website: e.target.value })} placeholder="https://..." />
          </div>
        </div>
      </div>

      {/* Address */}
      <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-6">
        <div className="flex items-center gap-2 text-gray-900 font-semibold">
          <MapPin className="w-5 h-5" /> Address
        </div>
        <div>
          <label className={label}>Street Address</label>
          <input className={input} value={company.address || ''} onChange={e => setCompany({ ...company, address: e.target.value })} />
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <div>
            <label className={label}>City</label>
            <input className={input} value={company.city || ''} onChange={e => setCompany({ ...company, city: e.target.value })} />
          </div>
          <div>
            <label className={label}>State</label>
            <input className={input} value={company.state || ''} onChange={e => setCompany({ ...company, state: e.target.value })} />
          </div>
          <div>
            <label className={label}>Country</label>
            <input className={input} value={company.country || ''} onChange={e => setCompany({ ...company, country: e.target.value })} />
          </div>
          <div>
            <label className={label}>Postal Code</label>
            <input className={input} value={company.postalCode || ''} onChange={e => setCompany({ ...company, postalCode: e.target.value })} />
          </div>
        </div>
      </div>

      {/* Preferences */}
      <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-6">
        <div className="flex items-center gap-2 text-gray-900 font-semibold">
          <SettingsIcon className="w-5 h-5" /> Preferences
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div>
            <label className={label}><Globe className="w-3.5 h-3.5 inline mr-1" /> Default Language</label>
            <select className={input} value={company.defaultLanguage} onChange={e => setCompany({ ...company, defaultLanguage: e.target.value })}>
              <option value="en">English</option>
              <option value="ar">Arabic</option>
              <option value="hi">Hindi</option>
              <option value="es">Spanish</option>
              <option value="fr">French</option>
            </select>
          </div>
          <div>
            <label className={label}><Clock className="w-3.5 h-3.5 inline mr-1" /> Timezone</label>
            <select className={input} value={company.defaultTimezone} onChange={e => setCompany({ ...company, defaultTimezone: e.target.value })}>
              <option value="UTC">UTC</option>
              <option value="Asia/Kolkata">Asia/Kolkata (IST)</option>
              <option value="America/New_York">America/New_York (EST)</option>
              <option value="Europe/London">Europe/London (GMT)</option>
              <option value="Asia/Dubai">Asia/Dubai (GST)</option>
            </select>
          </div>
          <div>
            <label className={label}><DollarSign className="w-3.5 h-3.5 inline mr-1" /> Currency</label>
            <select className={input} value={company.defaultCurrency} onChange={e => setCompany({ ...company, defaultCurrency: e.target.value })}>
              <option value="USD">USD &ndash; US Dollar</option>
              <option value="EUR">EUR &ndash; Euro</option>
              <option value="GBP">GBP &ndash; British Pound</option>
              <option value="INR">INR &ndash; Indian Rupee</option>
              <option value="AED">AED &ndash; UAE Dirham</option>
            </select>
          </div>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className={label}>Date Format</label>
            <input className={input} value={company.dateFormat || 'yyyy-MM-dd'} onChange={e => setCompany({ ...company, dateFormat: e.target.value })} />
          </div>
          <div>
            <label className={label}>Time Format</label>
            <input className={input} value={company.timeFormat || 'HH:mm'} onChange={e => setCompany({ ...company, timeFormat: e.target.value })} />
          </div>
        </div>
      </div>

      {/* Subscription */}
      <div className="bg-white rounded-xl border border-gray-200 p-6 space-y-4">
        <div className="flex items-center gap-2 text-gray-900 font-semibold">
          <CreditCard className="w-5 h-5" /> Subscription
        </div>
        {subscription ? (
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <div className="text-sm font-medium text-gray-900">{subscription.packageName}</div>
                <div className="text-xs text-gray-500">Assigned on {new Date(subscription.startDate).toLocaleDateString()}</div>
              </div>
              <span className={`inline-flex px-2.5 py-1 rounded-full text-xs font-medium ${subStatusMap[subscription.status]?.color || 'bg-gray-100 text-gray-700'}`}>
                {subStatusMap[subscription.status]?.label || 'Unknown'}
              </span>
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
              <div><div className="text-xs text-gray-500">Monthly Price</div><div className="text-sm font-semibold text-gray-900">${subscription.currentPrice}</div></div>
              <div><div className="text-xs text-gray-500">Effective Price</div><div className="text-sm font-semibold text-gray-900">${subscription.effectivePrice.toFixed(2)}</div></div>
              <div><div className="text-xs text-gray-500">Billing Cycle</div><div className="text-sm font-medium text-gray-900 capitalize">{subscription.billingCycle}</div></div>
              <div><div className="text-xs text-gray-500">End Date</div><div className="text-sm font-medium text-gray-900">{subscription.endDate ? new Date(subscription.endDate).toLocaleDateString() : 'No end date'}</div></div>
            </div>
            {subscription.discountPercentage != null && subscription.discountPercentage > 0 && (
              <div className="text-xs text-green-600">Discount: {subscription.discountPercentage}%{subscription.taxPercentage != null && subscription.taxPercentage > 0 ? ` &bull; Tax: ${subscription.taxPercentage}%` : ''}</div>
            )}
          </div>
        ) : (
          <div className="text-sm text-gray-500">
            No subscription assigned. Contact your platform administrator to set up a subscription.
          </div>
        )}
      </div>
    </div>
  );
}
