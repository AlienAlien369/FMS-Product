import { useCallback, useEffect, useState } from 'react';
import api from '../lib/api';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { usePermissions } from '../hooks/usePermissions';
import { Bell, Check, AlertTriangle, Info, XCircle, Filter } from 'lucide-react';
import { FilterTabs, FilterChip, Pager } from '../components/ui';

interface AlertItem {
  id: string; alertType: string; title: string; message?: string; severity: number;
  status: string; vehicleId?: string; vehicleRegistration?: string;
  driverId?: string; driverName?: string; createdAt: string; acknowledgedAt?: string;
}
interface AlertStats {
  totalCount: number; openCount: number; acknowledgedCount: number;
  byType: Record<string, number>; bySeverity: Record<string, number>;
}

const SEVERITY_COLORS: Record<number, string> = {
  0: 'bg-gray-100 text-gray-600', 1: 'bg-green-100 text-green-700',
  2: 'bg-amber-100 text-amber-700', 3: 'bg-orange-100 text-orange-700', 4: 'bg-red-100 text-red-700',
};
const SEVERITY_LABELS: Record<number, string> = { 0: 'Info', 1: 'Low', 2: 'Medium', 3: 'High', 4: 'Critical' };

export default function AlertsPage() {
  const { can } = usePermissions();
  const { version: scopeVersion } = useCompanyScope();
  const [items, setItems] = useState<AlertItem[]>([]);
  const [stats, setStats] = useState<AlertStats | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [total, setTotal] = useState(0);
  const [severityFilter, setSeverityFilter] = useState<number | null>(null);
  const [statusFilter, setStatusFilter] = useState<'all' | 'open' | 'acknowledged' | null>(null);
  const [loading, setLoading] = useState(true);
  const canAcknowledge = can('alert.update');

  const fetchAll = useCallback(async () => {
    setLoading(true);
    const params: Record<string, any> = { page, pageSize };
    if (severityFilter != null) params.severity = severityFilter;
    if (statusFilter === 'open') params.status = 'active';
    if (statusFilter === 'acknowledged') params.status = 'inactive';
    // statusFilter === null means "All" — no status param is sent.
    try {
      const [listRes, statsRes] = await Promise.all([
        api.get('/alerts', { params }),
        api.get('/alerts/stats'),
      ]);
      setItems(listRes.data.data?.items || []);
      setTotal(listRes.data.data?.totalCount || 0);
      setStats(statsRes.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [page, pageSize, severityFilter, statusFilter, scopeVersion]);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  const acknowledge = async (id: string) => {
    try { await api.post(`/alerts/${id}/acknowledge`); fetchAll(); } catch (err) { console.error(err); }
  };

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const byType = stats?.byType ? Object.entries(stats.byType).sort((a, b) => b[1] - a[1]).slice(0, 6) : [];

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Alerts</h2>
          <p className="text-sm text-gray-500 mt-0.5">Alert volume by type and severity — acknowledge to close.</p>
        </div>
        <div className="flex items-center gap-2">
          <FilterTabs>
            {([null, 'open', 'acknowledged'] as const).map(s => (
              <FilterChip key={String(s)} active={statusFilter === s} onClick={() => { setStatusFilter(s); setPage(1); }}>
                {s === null ? 'All' : s === 'open' ? 'Open' : 'Acknowledged'}
              </FilterChip>
            ))}
          </FilterTabs>
          <select value={severityFilter ?? ''} onChange={e => { setSeverityFilter(e.target.value === '' ? null : Number(e.target.value)); setPage(1); }}
            className="px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white min-h-[44px]">
            <option value="">All severities</option>
            {[0, 1, 2, 3, 4].map(s => <option key={s} value={s}>{SEVERITY_LABELS[s]}</option>)}
          </select>
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-white rounded-xl border border-gray-200 p-4">
          <p className="text-xl font-bold text-gray-900">{stats?.totalCount ?? '—'}</p>
          <p className="text-xs text-gray-500 mt-0.5">Total alerts</p>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4">
          <p className="text-xl font-bold text-amber-600">{stats?.openCount ?? '—'}</p>
          <p className="text-xs text-gray-500 mt-0.5">Open</p>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4">
          <p className="text-xl font-bold text-green-600">{stats?.acknowledgedCount ?? '—'}</p>
          <p className="text-xs text-gray-500 mt-0.5">Acknowledged</p>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4">
          <p className="text-xl font-bold text-red-600">{(stats?.bySeverity?.['4'] || 0) + (stats?.bySeverity?.['3'] || 0)}</p>
          <p className="text-xs text-gray-500 mt-0.5">Critical/High</p>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-4 gap-6">
        {/* Top types */}
        <div className="bg-white rounded-xl border border-gray-200 p-5 xl:col-span-1 h-fit">
          <h3 className="text-sm font-semibold text-gray-900 flex items-center gap-2 mb-4"><Filter className="w-4 h-4 text-gray-400" /> Top Alert Types</h3>
          {byType.length === 0 ? <p className="text-sm text-gray-400">No alerts yet.</p> : (
            <div className="space-y-3">
              {byType.map(([type, count]) => (
                <div key={type}>
                  <div className="flex items-center justify-between text-xs mb-1">
                    <span className="text-gray-600 font-medium truncate">{type}</span>
                    <span className="text-gray-400">{count}</span>
                  </div>
                  <div className="h-1.5 bg-gray-100 rounded-full overflow-hidden">
                    <div className="h-full bg-blue-500 rounded-full" style={{ width: `${Math.min(100, (count / (stats?.totalCount || 1)) * 100)}%` }} />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* List */}
        <div className="bg-white rounded-xl border border-gray-200 xl:col-span-3">
          <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
            <h3 className="text-sm font-semibold text-gray-900 flex items-center gap-2"><Bell className="w-4 h-4 text-blue-500" /> Alert Feed</h3>
            <span className="text-xs text-gray-400">{total} total</span>
          </div>
          {loading ? (
            <p className="px-5 py-8 text-sm text-gray-400">Loading…</p>
          ) : items.length === 0 ? (
            <p className="px-5 py-8 text-sm text-gray-400 text-center">No alerts match these filters.</p>
          ) : (
            <div className="divide-y divide-gray-50">
              {items.map(a => (
                <div key={a.id} className="px-5 py-3 flex flex-wrap items-start justify-between gap-3">
                  <div className="flex items-start gap-3 min-w-0">
                    {a.severity >= 3 ? <XCircle className="w-4 h-4 text-red-500 mt-1 shrink-0" />
                      : a.severity === 2 ? <AlertTriangle className="w-4 h-4 text-amber-500 mt-1 shrink-0" />
                      : <Info className="w-4 h-4 text-gray-400 mt-1 shrink-0" />}
                    <div className="min-w-0">
                      <p className="text-sm font-medium text-gray-900">{a.title}</p>
                      {a.message && <p className="text-xs text-gray-500 mt-0.5 line-clamp-2">{a.message}</p>}
                      <p className="text-[11px] text-gray-400 mt-1">
                        {a.alertType} · {new Date(a.createdAt).toLocaleString()}
                        {a.vehicleRegistration ? ` · ${a.vehicleRegistration}` : ''}
                        {a.driverName ? ` · ${a.driverName}` : ''}
                      </p>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-medium ${SEVERITY_COLORS[a.severity]}`}>
                      {SEVERITY_LABELS[a.severity]}
                    </span>
                    {a.status === 'Active' && canAcknowledge && (
                      <button onClick={() => acknowledge(a.id)}
                        className="min-h-[36px] px-3 py-1.5 text-xs font-medium rounded-lg border border-green-300 text-green-700 hover:bg-green-50 flex items-center gap-1">
                        <Check className="w-3.5 h-3.5" /> Acknowledge
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
          <div className="px-5 py-3 border-t border-gray-100">
            <Pager page={page} totalPages={totalPages} hasPrev={page > 1} hasNext={page < totalPages}
              onChange={p => setPage(p)} label={`Page ${page} of ${totalPages}`} />
          </div>
        </div>
      </div>
    </div>
  );
}