import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import api from '../lib/api';
import { Bell, CheckCheck, Filter, Inbox } from 'lucide-react';

interface NotificationItem {
  id: string; title: string; message: string; eventType?: string; actionUrl?: string;
  severity: number; relatedEntityType?: string; relatedEntityId?: string; isRead: boolean; createdAt: string;
}
interface PreferenceItem { eventType: string; eventTypeName: string; category: string; enabled: boolean; }
interface PagedData<T> { items: T[]; totalCount: number; page: number; pageSize: number; totalPages: number; }

const SEVERITY_LABELS: Record<number, string> = { 0: 'Info', 1: 'Low', 2: 'Medium', 3: 'High', 4: 'Critical' };
const SEVERITY_COLORS: Record<number, string> = {
  0: 'bg-gray-100 text-gray-600', 1: 'bg-blue-100 text-blue-700',
  2: 'bg-amber-100 text-amber-700', 3: 'bg-orange-100 text-orange-700', 4: 'bg-red-100 text-red-700',
};

export default function Notifications() {
  const navigate = useNavigate();
  const [data, setData] = useState<PagedData<NotificationItem> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [eventType, setEventType] = useState('');
  const [severity, setSeverity] = useState('');
  const [eventTypes, setEventTypes] = useState<{ code: string; name: string }[]>([]);
  const [prefs, setPrefs] = useState<PreferenceItem[] | null>(null);
  const [showPrefs, setShowPrefs] = useState(false);
  const [prefDirty, setPrefDirty] = useState(false);

  const fetchList = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: '15' });
      if (unreadOnly) params.set('unread', 'true');
      if (eventType) params.set('eventType', eventType);
      if (severity) params.set('severity', severity);
      const r = await api.get(`/notifications?${params.toString()}`);
      setData(r.data.data);
    } catch { /* silent */ }
    setLoading(false);
  }, [page, unreadOnly, eventType, severity]);

  useEffect(() => { fetchList(); }, [fetchList]);

  useEffect(() => {
    api.get('/notifications/event-types').then(r =>
      setEventTypes((r.data.data || []).map((e: any) => ({ code: e.code, name: e.name })))).catch(() => {});
    api.get('/notifications/preferences').then(r => setPrefs(r.data.data || [])).catch(() => {});
  }, []);

  const togglePref = (code: string) => {
    setPrefs(prev => prev?.map(p => p.eventType === code ? { ...p, enabled: !p.enabled } : p) ?? prev);
    setPrefDirty(true);
  };

  const savePrefs = async () => {
    if (!prefs) return;
    await api.put('/notifications/preferences', prefs.map(p => ({ eventType: p.eventType, enabled: p.enabled })));
    setPrefDirty(false);
  };

  const markAllRead = async () => {
    await api.put('/notifications/read-all');
    fetchList();
  };

  const open = async (n: NotificationItem) => {
    if (!n.isRead) await api.put(`/notifications/${n.id}/read`);
    if (n.actionUrl) navigate(n.actionUrl);
  };

  const resetFilters = () => { setUnreadOnly(false); setEventType(''); setSeverity(''); setPage(1); };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-gray-900">My Notifications</h2>
          <p className="text-sm text-gray-500 mt-0.5">In-app history — mute any event type from your personal preferences.</p>
        </div>
        <div className="flex items-center gap-2">
          <button onClick={() => setShowPrefs(!showPrefs)}
            className="flex items-center gap-2 px-3 py-2 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm font-medium text-gray-700 transition-colors">
            <Filter className="w-4 h-4" /> {showPrefs ? 'Hide preferences' : 'Notification preferences'}
          </button>
          <button onClick={markAllRead} className="flex items-center gap-2 px-3 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-sm font-medium transition-colors">
            <CheckCheck className="w-4 h-4" /> Mark all read
          </button>
        </div>
      </div>

      {showPrefs && (
        <div className="bg-white rounded-xl border border-gray-200 p-5 space-y-3">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold text-gray-900">Personal preferences</h3>
            {prefDirty && (
              <button onClick={savePrefs} className="px-3 py-1.5 bg-blue-600 text-white text-xs font-medium rounded-lg hover:bg-blue-700">
                Save changes
              </button>
            )}
          </div>
          <p className="text-xs text-gray-500">Muting an event type stops it from reaching your bell and this page. Absence of a toggle row means enabled by default.</p>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-2">
            {prefs?.map(p => (
              <label key={p.eventType} className="flex items-center justify-between gap-2 px-3 py-2 rounded-lg border border-gray-200 text-sm">
                <span className="text-gray-700 truncate" title={p.eventType}>{p.eventTypeName}</span>
                <button
                  onClick={() => togglePref(p.eventType)}
                  className={`w-9 h-5 rounded-full transition-colors relative flex-shrink-0 ${p.enabled ? 'bg-blue-600' : 'bg-gray-300'}`}
                  aria-label={`Toggle ${p.eventTypeName}`}
                >
                  <span className={`absolute top-0.5 w-4 h-4 bg-white rounded-full shadow transition-all ${p.enabled ? 'left-4.5' : 'left-0.5'}`} style={{ left: p.enabled ? '18px' : '2px' }} />
                </button>
              </label>
            ))}
            {prefs?.length === 0 && <p className="text-sm text-gray-400 col-span-full">No event types registered yet.</p>}
          </div>
        </div>
      )}

      <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="px-4 py-3 border-b border-gray-100 flex flex-wrap items-center gap-2">
          <button onClick={() => { setUnreadOnly(!unreadOnly); setPage(1); }}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors ${unreadOnly ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-600 hover:bg-gray-200'}`}>
            Unread only
          </button>
          <select value={eventType} onChange={e => { setEventType(e.target.value); setPage(1); }}
            className="px-3 py-1.5 rounded-lg border border-gray-200 text-xs text-gray-600 bg-white">
            <option value="">All event types</option>
            {eventTypes.map(e => <option key={e.code} value={e.code}>{e.name}</option>)}
          </select>
          <select value={severity} onChange={e => { setSeverity(e.target.value); setPage(1); }}
            className="px-3 py-1.5 rounded-lg border border-gray-200 text-xs text-gray-600 bg-white">
            <option value="">All severities</option>
            {[0, 1, 2, 3, 4].map(s => <option key={s} value={s}>{SEVERITY_LABELS[s]}</option>)}
          </select>
          {(unreadOnly || eventType || severity) && (
            <button onClick={resetFilters} className="px-3 py-1.5 rounded-lg text-xs font-medium text-gray-500 hover:text-gray-700">Clear</button>
          )}
        </div>

        <div className="divide-y divide-gray-50">
          {loading ? (
            <div className="text-center py-12 text-gray-400">Loading…</div>
          ) : !data || data.items.length === 0 ? (
            <div className="py-12 text-center text-gray-400">
              <Inbox className="w-10 h-10 mx-auto mb-2 text-gray-300" />
              <p className="text-sm">No notifications match the current filters.</p>
            </div>
          ) : data.items.map(n => (
            <button key={n.id} onClick={() => open(n)}
              className={`w-full text-left px-4 py-3.5 hover:bg-gray-50 transition-colors ${n.isRead ? 'opacity-60' : ''}`}>
              <div className="flex items-start gap-3">
                <span className={`mt-0.5 px-2 py-0.5 rounded-full text-[10px] font-semibold flex-shrink-0 ${SEVERITY_COLORS[n.severity] ?? 'bg-gray-100 text-gray-600'}`}>
                  {SEVERITY_LABELS[n.severity] ?? '—'}
                </span>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-sm font-medium text-gray-900">{n.title}</p>
                    <span className="text-[11px] text-gray-400 whitespace-nowrap">{new Date(n.createdAt).toLocaleString()}</span>
                  </div>
                  <p className="text-xs text-gray-500 mt-0.5">{n.message}</p>
                  {n.eventType && <span className="inline-flex mt-1.5 px-1.5 py-0.5 rounded bg-gray-100 text-gray-500 text-[10px] font-mono">{n.eventType}</span>}
                </div>
              </div>
            </button>
          ))}
        </div>

        {data && data.totalPages > 1 && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-gray-100">
            <span className="text-xs text-gray-500">{data.totalCount} total</span>
            <div className="flex items-center gap-2">
              <button disabled={page <= 1} onClick={() => setPage(page - 1)}
                className="px-3 py-1.5 rounded-lg text-xs font-medium bg-gray-100 text-gray-600 hover:bg-gray-200 disabled:opacity-40">Prev</button>
              <span className="text-xs text-gray-500">{page} / {data.totalPages}</span>
              <button disabled={page >= data.totalPages} onClick={() => setPage(page + 1)}
                className="px-3 py-1.5 rounded-lg text-xs font-medium bg-gray-100 text-gray-600 hover:bg-gray-200 disabled:opacity-40">Next</button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}