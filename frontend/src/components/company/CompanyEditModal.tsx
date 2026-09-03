import { useState } from 'react';
import api from '../../lib/api';
import { X, Building2, MapPin, Globe } from 'lucide-react';
import { useLocalizationOptions } from '../../hooks/useLocalizationOptions';

const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

interface CompanyInfo {
  id: string; name: string; slug: string; contactEmail?: string; contactPhone?: string;
  website?: string; address?: string; city?: string; state?: string; country?: string; postalCode?: string;
  status: number; createdAt: string; defaultLanguage: string; defaultTimezone: string; defaultCurrency: string;
  dateFormat?: string; timeFormat?: string; numberFormat?: string;
}

export default function CompanyEditModal({ company, onClose, onSaved }: { company: CompanyInfo; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    name: company.name, contactEmail: company.contactEmail || '', contactPhone: company.contactPhone || '',
    website: company.website || '', address: company.address || '', city: company.city || '',
    state: company.state || '', country: company.country || '', postalCode: company.postalCode || '',
    defaultLanguage: company.defaultLanguage, defaultTimezone: company.defaultTimezone, defaultCurrency: company.defaultCurrency,
    dateFormat: company.dateFormat || 'yyyy-MM-dd', timeFormat: company.timeFormat || 'HH:mm',
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const { languages, currencies, loading: localeLoading } = useLocalizationOptions();

  const handleSave = async () => {
    if (!form.name.trim()) { setError('Company name required'); return; }
    setSaving(true); setError('');
    try {
      await api.put(`/admin/companies/${company.id}/extended`, form);
      setSaving(false);
      onSaved();
    } catch (e: any) {
      setSaving(false);
      setError(e.response?.data?.message || 'Failed to save company');
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4"><div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200"><h2 className="text-lg font-semibold text-gray-900">Edit Company</h2><button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button></div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-5">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><Building2 className="w-4 h-4" /> Basic Information</div>
            <div className="grid grid-cols-2 gap-4">
              <div><label className={LABEL}>Company Name *</label><input className={INPUT} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></div>
              <div><label className={LABEL}>Website</label><input className={INPUT} value={form.website} onChange={e => setForm({ ...form, website: e.target.value })} placeholder="https://..." /></div>
              <div><label className={LABEL}>Contact Email</label><input className={INPUT} type="email" value={form.contactEmail} onChange={e => setForm({ ...form, contactEmail: e.target.value })} /></div>
              <div><label className={LABEL}>Contact Phone</label><input className={INPUT} value={form.contactPhone} onChange={e => setForm({ ...form, contactPhone: e.target.value })} /></div>
            </div>
          </div>
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><MapPin className="w-4 h-4" /> Address</div>
            <div><label className={LABEL}>Street Address</label><input className={INPUT} value={form.address} onChange={e => setForm({ ...form, address: e.target.value })} /></div>
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mt-4">
              <div><label className={LABEL}>City</label><input className={INPUT} value={form.city} onChange={e => setForm({ ...form, city: e.target.value })} /></div>
              <div><label className={LABEL}>State</label><input className={INPUT} value={form.state} onChange={e => setForm({ ...form, state: e.target.value })} /></div>
              <div><label className={LABEL}>Country</label><input className={INPUT} value={form.country} onChange={e => setForm({ ...form, country: e.target.value })} /></div>
              <div><label className={LABEL}>Postal Code</label><input className={INPUT} value={form.postalCode} onChange={e => setForm({ ...form, postalCode: e.target.value })} /></div>
            </div>
          </div>
          <div>
            <div className="flex items-center gap-2 text-gray-900 font-semibold mb-3"><Globe className="w-4 h-4" /> Preferences</div>
            <div className="grid grid-cols-2 lg:grid-cols-3 gap-4">
              <div>
                <label className={LABEL}>Language</label>
                <select className={INPUT} value={form.defaultLanguage} onChange={e => setForm({ ...form, defaultLanguage: e.target.value })} disabled={localeLoading}>
                  {languages.length === 0 && <option value={form.defaultLanguage}>{form.defaultLanguage}</option>}
                  {languages.map(l => <option key={l.code} value={l.code}>{l.label}</option>)}
                </select>
                <p className="text-[11px] text-gray-400 mt-0.5">Only languages active on the platform's master list are allowed.</p>
              </div>
              <div><label className={LABEL}>Timezone</label><select className={INPUT} value={form.defaultTimezone} onChange={e => setForm({ ...form, defaultTimezone: e.target.value })}><option value="UTC">UTC</option><option value="Asia/Kolkata">IST</option><option value="America/New_York">EST</option><option value="Europe/London">GMT</option><option value="Asia/Dubai">GST</option></select></div>
              <div>
                <label className={LABEL}>Currency</label>
                <select className={INPUT} value={form.defaultCurrency} onChange={e => setForm({ ...form, defaultCurrency: e.target.value })} disabled={localeLoading}>
                  {currencies.length === 0 && <option value={form.defaultCurrency}>{form.defaultCurrency}</option>}
                  {currencies.map(c => <option key={c.code} value={c.code}>{c.label}</option>)}
                </select>
                <p className="text-[11px] text-gray-400 mt-0.5">Only currencies active on the platform's master list are allowed.</p>
              </div>
              <div><label className={LABEL}>Date Format</label><input className={INPUT} value={form.dateFormat} onChange={e => setForm({ ...form, dateFormat: e.target.value })} placeholder="yyyy-MM-dd" /></div>
              <div><label className={LABEL}>Time Format</label><input className={INPUT} value={form.timeFormat} onChange={e => setForm({ ...form, timeFormat: e.target.value })} placeholder="HH:mm" /></div>
            </div>
          </div>
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSave} disabled={saving} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:bg-blue-400">{saving ? 'Saving...' : 'Save Changes'}</button>
        </div>
      </div>
    </div>
  );
}
