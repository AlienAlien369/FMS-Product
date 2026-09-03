import { useEffect, useState } from 'react';
import api from '../../lib/api';
import { Building2, MapPin, Globe, X } from 'lucide-react';
import { useLocalizationOptions } from '../../hooks/useLocalizationOptions';

interface Props {
  onClose: () => void;
  onSaved: () => void;
}

const input = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const label = "block text-sm font-medium text-gray-700 mb-1";

/** SuperAdmin company creation. Posts to /admin/companies (this endpoint owns company CRUD). */
export default function CreateCompanyModal({ onClose, onSaved }: Props) {
  const { languages, currencies, loading: localeLoading } = useLocalizationOptions();
  const [packages, setPackages] = useState<{ id: string; name: string }[]>([]);
  const [form, setForm] = useState({
    name: '', slug: '', contactEmail: '', contactPhone: '', website: '',
    address: '', city: '', state: '', country: '', postalCode: '',
    defaultLanguage: 'en', defaultTimezone: 'UTC', defaultCurrency: 'USD',
    packageId: '',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const set = (k: string, v: string) => setForm(f => ({ ...f, [k]: v }));

  useEffect(() => {
    api.get('/admin/packages?pageSize=100').then(r =>
      setPackages((r.data.data?.items || []).map((p: any) => ({ id: p.id, name: p.name })))
    ).catch(() => {});
  }, []);

  const handleSubmit = async () => {
    if (!form.name.trim()) { setError('Company name required'); return; }
    setSaving(true); setError('');
    try {
      await api.post('/admin/companies', {
        ...form,
        defaultLanguage: form.defaultLanguage || undefined,
        defaultCurrency: form.defaultCurrency || undefined,
        defaultTimezone: form.defaultTimezone || undefined,
        packageId: form.packageId || null,
      });
      onSaved();
    } catch (e: any) {
      setError(e.response?.data?.message || 'Failed to create company');
    }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">Create Company</h2>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><Building2 className="w-4 h-4" /> Basic Information</div>
            <div className="grid grid-cols-2 gap-4">
              <div><label className={label}>Company Name *</label><input className={input} value={form.name} onChange={e => set('name', e.target.value)} placeholder="e.g. Fleet Corp" /></div>
              <div><label className={label}>Slug</label><input className={input} value={form.slug} onChange={e => set('slug', e.target.value)} placeholder="fleet-corp" /></div>
              <div><label className={label}>Contact Email</label><input className={input} type="email" value={form.contactEmail} onChange={e => set('contactEmail', e.target.value)} /></div>
              <div><label className={label}>Contact Phone</label><input className={input} value={form.contactPhone} onChange={e => set('contactPhone', e.target.value)} /></div>
              <div className="col-span-2"><label className={label}>Website</label><input className={input} value={form.website} onChange={e => set('website', e.target.value)} placeholder="https://..." /></div>
            </div>
          </div>
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><MapPin className="w-4 h-4" /> Address</div>
            <div><label className={label}>Street Address</label><input className={input} value={form.address} onChange={e => set('address', e.target.value)} /></div>
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mt-4">
              <div><label className={label}>City</label><input className={input} value={form.city} onChange={e => set('city', e.target.value)} /></div>
              <div><label className={label}>State</label><input className={input} value={form.state} onChange={e => set('state', e.target.value)} /></div>
              <div><label className={label}>Country</label><input className={input} value={form.country} onChange={e => set('country', e.target.value)} /></div>
              <div><label className={label}>Postal Code</label><input className={input} value={form.postalCode} onChange={e => set('postalCode', e.target.value)} /></div>
            </div>
          </div>
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><Globe className="w-4 h-4" /> Preferences</div>
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
              <div>
                <label className={label}>Language</label>
                <select className={input} value={form.defaultLanguage} onChange={e => set('defaultLanguage', e.target.value)} disabled={localeLoading}>
                  {languages.length === 0 && <option value="en">English (en)</option>}
                  {languages.map(l => <option key={l.code} value={l.code}>{l.label}</option>)}
                </select>
              </div>
              <div><label className={label}>Timezone</label><select className={input} value={form.defaultTimezone} onChange={e => set('defaultTimezone', e.target.value)}><option value="UTC">UTC</option><option value="Asia/Kolkata">Asia/Kolkata (IST)</option><option value="America/New_York">America/New_York (EST)</option><option value="Europe/London">Europe/London (GMT)</option><option value="Asia/Dubai">Asia/Dubai (GST)</option></select></div>
              <div>
                <label className={label}>Currency</label>
                <select className={input} value={form.defaultCurrency} onChange={e => set('defaultCurrency', e.target.value)} disabled={localeLoading}>
                  {currencies.length === 0 && <option value="USD">USD — US Dollar</option>}
                  {currencies.map(c => <option key={c.code} value={c.code}>{c.label}</option>)}
                </select>
              </div>
              <div>
                <label className={label}>Package</label>
                <select className={input} value={form.packageId} onChange={e => set('packageId', e.target.value)}>
                  <option value="">None (assign later)</option>
                  {packages.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
                <p className="text-[11px] text-gray-400 mt-0.5">The package defines which modules this company can use.</p>
              </div>
            </div>
          </div>
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">{saving ? 'Creating...' : 'Create Company'}</button>
        </div>
      </div>
    </div>
  );
}
