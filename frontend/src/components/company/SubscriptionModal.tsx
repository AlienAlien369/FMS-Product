import { useState } from 'react';
import api from '../../lib/api';
import { X } from 'lucide-react';

const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

interface SubscriptionInfo { id: string; packageId: string; packageName: string; status: number; startDate: string; endDate?: string; currentPrice: number; currency: string; billingCycle: string; discountPercentage?: number; taxPercentage?: number; maxUsers?: number; maxVehicles?: number; maxDrivers?: number; }
interface PackageOption { id: string; name: string; price: number; billingCycle: string; maxUsers: number; maxVehicles: number; maxDrivers: number; }

export default function SubscriptionModal({ companyId, subscription, packages, onClose, onSaved }: { companyId: string; subscription: SubscriptionInfo | null; packages: PackageOption[]; onClose: () => void; onSaved: () => void }) {
  const isEdit = !!subscription?.id;
  const [form, setForm] = useState({
    packageId: subscription?.packageId || (packages[0]?.id || ''),
    startDate: subscription?.startDate ? new Date(subscription.startDate).toISOString().split('T')[0] : new Date().toISOString().split('T')[0],
    endDate: subscription?.endDate ? new Date(subscription.endDate).toISOString().split('T')[0] : '',
    currentPrice: subscription?.currentPrice?.toString() || '',
    currency: subscription?.currency || 'USD', billingCycle: subscription?.billingCycle || 'monthly',
    discountPercentage: subscription?.discountPercentage?.toString() || '', taxPercentage: subscription?.taxPercentage?.toString() || '',
    maxUsers: subscription?.maxUsers?.toString() || '', maxVehicles: subscription?.maxVehicles?.toString() || '', maxDrivers: subscription?.maxDrivers?.toString() || '',
  });
  const [renewDate, setRenewDate] = useState('');
  const [action, setAction] = useState<'assign' | 'renew' | 'cancel'>(isEdit ? 'renew' : 'assign');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const selectedPkg = packages.find(p => p.id === form.packageId);

  const handleSubmit = async () => {
    setSaving(true); setError('');
    try {
      if (action === 'assign') {
        const payload: any = { companyId, packageId: form.packageId, startDate: form.startDate, currentPrice: selectedPkg?.price || 0, currency: form.currency, billingCycle: form.billingCycle };
        if (form.endDate) payload.endDate = form.endDate;
        if (form.discountPercentage) payload.discountPercentage = parseFloat(form.discountPercentage);
        if (form.taxPercentage) payload.taxPercentage = parseFloat(form.taxPercentage);
        if (form.maxUsers) payload.maxUsers = parseInt(form.maxUsers);
        if (form.maxVehicles) payload.maxVehicles = parseInt(form.maxVehicles);
        if (form.maxDrivers) payload.maxDrivers = parseInt(form.maxDrivers);
        await api.post(`/admin/companies/${companyId}/subscription`, payload);
      } else if (action === 'renew') {
        if (!renewDate) { setError('Renewal date required'); setSaving(false); return; }
        await api.post(`/admin/companies/${companyId}/subscription/renew`, { newEndDate: renewDate });
      } else if (action === 'cancel') {
        if (!confirm('Cancel subscription? All company users will be unable to log in.')) { setSaving(false); return; }
        await api.delete(`/admin/companies/${companyId}/subscription`);
      }
      onSaved();
    } catch (e: any) { setError(e.response?.data?.message || 'Failed'); }
    setSaving(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="fixed inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-xl shadow-2xl w-full max-w-lg max-h-[85vh] flex flex-col">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-semibold text-gray-900">{action === 'assign' ? 'Assign Subscription' : action === 'renew' ? 'Renew Subscription' : 'Cancel Subscription'}</h2>
          <button onClick={onClose} className="p-1 hover:bg-gray-100 rounded-lg"><X className="w-5 h-5 text-gray-500" /></button>
        </div>
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-sm text-red-700">{error}</div>}
          {isEdit && (
            <div className="flex gap-1 bg-gray-100 p-1 rounded-lg">
              <button onClick={() => setAction('assign')} className={`flex-1 px-3 py-2 rounded-md text-sm font-medium ${action === 'assign' ? 'bg-white shadow text-gray-900' : 'text-gray-600'}`}>Assign New</button>
              <button onClick={() => setAction('renew')} className={`flex-1 px-3 py-2 rounded-md text-sm font-medium ${action === 'renew' ? 'bg-white shadow text-gray-900' : 'text-gray-600'}`}>Renew</button>
              <button onClick={() => setAction('cancel')} className={`flex-1 px-3 py-2 rounded-md text-sm font-medium ${action === 'cancel' ? 'bg-white shadow text-red-600' : 'text-gray-600'}`}>Cancel</button>
            </div>
          )}
          {action === 'assign' && (
            <>
              <div><label className={LABEL}>Package *</label><select className={INPUT} value={form.packageId} onChange={e => setForm({ ...form, packageId: e.target.value })}>{packages.map(p => <option key={p.id} value={p.id}>{p.name} — ${p.price}/{p.billingCycle === 'monthly' ? 'mo' : 'yr'}</option>)}</select></div>
              {selectedPkg && <div className="p-3 bg-blue-50 border border-blue-200 rounded-lg text-sm text-blue-700">Max Users: {selectedPkg.maxUsers === -1 ? '∞' : selectedPkg.maxUsers} • Vehicles: {selectedPkg.maxVehicles === -1 ? '∞' : selectedPkg.maxVehicles} • Drivers: {selectedPkg.maxDrivers === -1 ? '∞' : selectedPkg.maxDrivers}</div>}
              <div className="grid grid-cols-2 gap-4">
                <div><label className={LABEL}>Start Date *</label><input className={INPUT} type="date" value={form.startDate} onChange={e => setForm({ ...form, startDate: e.target.value })} /></div>
                <div><label className={LABEL}>End Date</label><input className={INPUT} type="date" value={form.endDate} onChange={e => setForm({ ...form, endDate: e.target.value })} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={LABEL}>Price Override</label><input className={INPUT} type="number" step="0.01" value={form.currentPrice} onChange={e => setForm({ ...form, currentPrice: e.target.value })} /></div>
                <div><label className={LABEL}>Discount %</label><input className={INPUT} type="number" min="0" max="100" value={form.discountPercentage} onChange={e => setForm({ ...form, discountPercentage: e.target.value })} /></div>
                <div><label className={LABEL}>Tax %</label><input className={INPUT} type="number" min="0" max="100" value={form.taxPercentage} onChange={e => setForm({ ...form, taxPercentage: e.target.value })} /></div>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <div><label className={LABEL}>Max Users Override</label><input className={INPUT} type="number" min="-1" value={form.maxUsers} onChange={e => setForm({ ...form, maxUsers: e.target.value })} placeholder="Use package" /></div>
                <div><label className={LABEL}>Max Vehicles Override</label><input className={INPUT} type="number" min="-1" value={form.maxVehicles} onChange={e => setForm({ ...form, maxVehicles: e.target.value })} placeholder="Use package" /></div>
                <div><label className={LABEL}>Max Drivers Override</label><input className={INPUT} type="number" min="-1" value={form.maxDrivers} onChange={e => setForm({ ...form, maxDrivers: e.target.value })} placeholder="Use package" /></div>
              </div>
            </>
          )}
          {action === 'renew' && (
            <div>
              <p className="text-sm text-gray-600 mb-4">Current end date: {subscription?.endDate ? new Date(subscription.endDate).toLocaleDateString() : 'No end date'}</p>
              <div><label className={LABEL}>New End Date *</label><input className={INPUT} type="date" value={renewDate} onChange={e => setRenewDate(e.target.value)} min={new Date().toISOString().split('T')[0]} /></div>
            </div>
          )}
          {action === 'cancel' && (
            <div className="p-4 bg-red-50 border border-red-200 rounded-lg space-y-2">
              <p className="text-sm font-medium text-red-800">⚠️ Cancel Subscription</p>
              <p className="text-sm text-red-700">This will cancel the active subscription. All company users will be unable to log in until a new subscription is assigned.</p>
            </div>
          )}
        </div>
        <div className="flex justify-end gap-3 px-6 py-4 border-t border-gray-200">
          <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg">Cancel</button>
          <button onClick={handleSubmit} disabled={saving} className={`px-5 py-2 text-white text-sm font-medium rounded-lg disabled:bg-blue-400 ${action === 'cancel' ? 'bg-red-600 hover:bg-red-700' : 'bg-blue-600 hover:bg-blue-700'}`}>
            {saving ? 'Saving...' : action === 'assign' ? 'Assign Subscription' : action === 'renew' ? 'Renew' : 'Cancel Subscription'}
          </button>
        </div>
      </div>
    </div>
  );
}
