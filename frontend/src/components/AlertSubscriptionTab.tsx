import { useState, useEffect, useCallback } from 'react';
import { Bell, BellOff, Save } from 'lucide-react';
import api from '../lib/api';

interface AlertTypeItem {
  id: string;
  code: string;
  name: string;
  category: string;
}

interface Subscription {
  id: string;
  alertTypeId: string;
  alertTypeCode: string;
  alertTypeName: string;
  alertTypeCategory: string;
  enabled: boolean;
  source: string;
}

export default function AlertSubscriptionTab({ companyId }: { companyId: string }) {
  const [subs, setSubs] = useState<Subscription[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const fetchSubs = useCallback(async () => {
    setLoading(true);
    try {
      const r = await api.get(`/companies/${companyId}/alert-subscriptions`);
      setSubs(r.data.data || []);
    } catch { /* ignore */ }
    setLoading(false);
  }, [companyId]);

  useEffect(() => { fetchSubs(); }, [fetchSubs]);

  const toggleSub = async (alertTypeId: string, enabled: boolean) => {
    setSaving(true);
    try {
      await api.put(`/companies/${companyId}/alert-subscriptions/${alertTypeId}`, { enabled });
      setSubs(prev => prev.map(s => s.alertTypeId === alertTypeId ? { ...s, enabled, source: 'admin_override' } : s));
    } catch { /* ignore */ }
    setSaving(false);
  };

  const bulkToggle = async (category: string | null, enabled: boolean) => {
    setSaving(true);
    try {
      await api.put(`/companies/${companyId}/alert-subscriptions/bulk`, { enabled, category });
      setSubs(prev => prev.map(s => !category || s.alertTypeCategory === category ? { ...s, enabled, source: 'admin_override' } : s));
    } catch { /* ignore */ }
    setSaving(false);
  };

  const categories = [...new Set(subs.map(s => s.alertTypeCategory))];
  const enabledCount = subs.filter(s => s.enabled).length;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold text-gray-900 flex items-center gap-2">
            <Bell className="h-5 w-5" /> Alert Subscriptions
          </h3>
          <p className="text-sm text-gray-500 mt-1">
            {enabledCount} of {subs.length} alert types enabled for this company
          </p>
        </div>
        <div className="flex gap-2">
          <button onClick={() => bulkToggle(null, true)} disabled={saving}
            className="px-3 py-1.5 text-xs font-medium bg-green-50 text-green-700 rounded-lg hover:bg-green-100 disabled:opacity-50">
            Enable All
          </button>
          <button onClick={() => bulkToggle(null, false)} disabled={saving}
            className="px-3 py-1.5 text-xs font-medium bg-red-50 text-red-700 rounded-lg hover:bg-red-100 disabled:opacity-50">
            Disable All
          </button>
        </div>
      </div>

      {loading ? (
        <div className="text-center py-8 text-gray-500">Loading...</div>
      ) : (
        <div className="space-y-4">
          {categories.map(cat => {
            const catSubs = subs.filter(s => s.alertTypeCategory === cat);
            const catEnabled = catSubs.filter(s => s.enabled).length;
            return (
              <div key={cat} className="bg-white border rounded-lg overflow-hidden">
                <div className="flex items-center justify-between px-4 py-3 bg-gray-50 border-b">
                  <div className="flex items-center gap-2">
                    <span className="font-medium text-sm text-gray-900">{cat}</span>
                    <span className="text-xs text-gray-500">({catEnabled}/{catSubs.length})</span>
                  </div>
                  <div className="flex gap-1">
                    <button onClick={() => bulkToggle(cat, true)} disabled={saving}
                      className="text-xs text-green-600 hover:text-green-800 px-2 py-1">Enable All</button>
                    <button onClick={() => bulkToggle(cat, false)} disabled={saving}
                      className="text-xs text-red-600 hover:text-red-800 px-2 py-1">Disable All</button>
                  </div>
                </div>
                <div className="divide-y divide-gray-100">
                  {catSubs.map(s => (
                    <div key={s.alertTypeId} className="flex items-center justify-between px-4 py-3 hover:bg-gray-50">
                      <div className="flex-1">
                        <div className="text-sm font-medium text-gray-900">{s.alertTypeName}</div>
                        <div className="text-xs text-gray-500 font-mono">{s.alertTypeCode}</div>
                      </div>
                      <div className="flex items-center gap-3">
                        {s.source === 'admin_override' && (
                          <span className="text-xs text-blue-600 bg-blue-50 px-2 py-0.5 rounded">overridden</span>
                        )}
                        <button onClick={() => toggleSub(s.alertTypeId, !s.enabled)} disabled={saving}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                            s.enabled ? 'bg-blue-600' : 'bg-gray-300'
                          } disabled:opacity-50`}>
                          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                            s.enabled ? 'translate-x-6' : 'translate-x-1'
                          }`} />
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
