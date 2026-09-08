import { useState, useEffect, useCallback } from 'react';
import { Search, ChevronDown, ChevronUp, ShieldAlert } from 'lucide-react';
import api from '../lib/api';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { Pager } from '../components/ui';

interface AuditEntry {
  id: string;
  actorUserId: string | null;
  actorEmail: string | null;
  actorRole: string | null;
  actionCode: string;
  action: string;
  targetEntityType: string;
  targetEntityId: string | null;
  targetEntityName: string | null;
  targetCompanyId: string | null;
  beforeState: unknown | null;
  afterState: unknown | null;
  ipAddress: string | null;
  source: string | null;
  timestamp: string;
}

const ACTION_LABELS: Record<string, string> = {
  'role.created': 'Role created',
  'role.updated': 'Role updated',
  'role.permission_updated': 'Role permissions updated',
  'role.deleted': 'Role deleted',
  'user.created': 'User created',
  'user.role_changed': "User's roles changed",
  'user.deactivated': 'User deactivated',
  'user.deleted': 'User deleted',
  'company.package_changed': 'Company package changed',
  'company.config_changed': 'Company configuration changed',
  'scope.switched': 'Scope switched (cross-tenant view)',
  'share_link.generated': 'Tracking link generated',
  'share_link.revoked': 'Tracking link revoked',
  'devicevendor.changed': 'Device vendor changed',
  'devicevendor.status_changed': 'Device vendor status changed',
};

function humanize(code: string): string {
  if (ACTION_LABELS[code]) return ACTION_LABELS[code];
  return code
    .split('.')
    .map(part => part.replace(/_/g, ' '))
    .join(' — ')
    .replace(/^./, c => c.toUpperCase());
}

function entityLabel(code: string): string {
  return code.split('.').slice(0, -1).join('.') || code;
}

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export default function AuditLog() {
  const { isCrossTenant, companies, companyName } = useCompanyScope();
  const [items, setItems] = useState<AuditEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(25);
  const [actionCode, setActionCode] = useState('');
  const [actor, setActor] = useState('');
  const [companyId, setCompanyId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [expanded, setExpanded] = useState<string | null>(null);

  const fetchEntries = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
      if (actionCode) params.set('actionCode', actionCode);
      if (actor.trim()) params.set('actor', actor.trim());
      if (companyId) params.set('targetCompanyId', companyId);
      if (from) params.set('from', new Date(from + 'T00:00:00').toISOString());
      if (to) params.set('to', new Date(to + 'T23:59:59').toISOString());
      const r = await api.get(`/audit?${params}`);
      const data = r.data?.data;
      setItems(data?.items ?? []);
      setTotal(data?.totalCount ?? 0);
    } catch { /* ignore */ }
    setLoading(false);
  }, [page, pageSize, actionCode, actor, companyId, from, to]);

  useEffect(() => { fetchEntries(); }, [fetchEntries]);

  const allCodes = [
    'role.permission_updated', 'role.created', 'role.updated', 'role.deleted',
    'user.created', 'user.role_changed', 'user.deactivated', 'user.deleted',
    'company.package_changed', 'company.config_changed',
    'scope.switched', 'share_link.generated', 'share_link.revoked',
    'devicevendor.changed', 'devicevendor.status_changed',
  ];

  const renderDiff = (e: AuditEntry) => {
    if (expanded !== e.id) {
      return (
        <button
          onClick={() => setExpanded(expanded === e.id ? null : e.id)}
          className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline min-h-[44px]"
        >
          <ChevronDown className="w-3.5 h-3.5" /> Details
        </button>
      );
    }
    return (
      <div className="space-y-2">
        <button onClick={() => setExpanded(null)} className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline min-h-[44px]">
          <ChevronUp className="w-3.5 h-3.5" /> Collapse
        </button>
        {e.beforeState != null && (
          <pre className="text-[11px] bg-gray-50 border border-gray-200 rounded-lg p-2 overflow-x-auto whitespace-pre-wrap break-all">
            <span className="font-semibold text-gray-500">before </span>{JSON.stringify(e.beforeState, null, 2)}
          </pre>
        )}
        {e.afterState != null && (
          <pre className="text-[11px] bg-blue-50/60 border border-blue-100 rounded-lg p-2 overflow-x-auto whitespace-pre-wrap break-all">
            <span className="font-semibold text-blue-600">after </span>{JSON.stringify(e.afterState, null, 2)}
          </pre>
        )}
        {e.beforeState == null && e.afterState == null && (
          <span className="text-xs text-gray-400">No state captured</span>
        )}
      </div>
    );
  };

  const inputCls = "px-2.5 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 min-h-[44px]";
  const labelCls = "block text-xs font-medium text-gray-500 mb-1";

  return (
    <div className="p-4 sm:p-6 space-y-4">
      <div>
        <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
          <ShieldAlert className="w-5 h-5 text-gray-500" /> Audit Log
        </h1>
        <p className="text-sm text-gray-500 mt-0.5">
          Append-only record of privileged and sensitive actions. Entries cannot be edited or deleted.
        </p>
      </div>

      {/* Filters */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-3 bg-white border border-gray-200 rounded-xl p-3">
        <div className="col-span-2 lg:col-span-1">
          <label className={labelCls}>Action type</label>
          <select value={actionCode} onChange={e => { setActionCode(e.target.value); setPage(1); }} className={`${inputCls} w-full`}>
            <option value="">All actions</option>
            {allCodes.map(c => <option key={c} value={c}>{humanize(c)}</option>)}
          </select>
        </div>
        <div className="col-span-2 lg:col-span-1">
          <label className={labelCls}>Actor</label>
          <div className="relative">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
            <input
              value={actor}
              onChange={e => { setActor(e.target.value); setPage(1); }}
              placeholder="Email…"
              className={`${inputCls} w-full pl-8`}
            />
          </div>
        </div>
        {isCrossTenant && (
          <div className="col-span-2 lg:col-span-1">
            <label className={labelCls}>Target company</label>
            <select value={companyId} onChange={e => { setCompanyId(e.target.value); setPage(1); }} className={`${inputCls} w-full`}>
              <option value="">All companies</option>
              {companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
        )}
        <div>
          <label className={labelCls}>From</label>
          <input type="date" value={from} onChange={e => { setFrom(e.target.value); setPage(1); }} className={`${inputCls} w-full`} />
        </div>
        <div>
          <label className={labelCls}>To</label>
          <input type="date" value={to} onChange={e => { setTo(e.target.value); setPage(1); }} className={`${inputCls} w-full`} />
        </div>
      </div>

      {/* Desktop table */}
      <div className="hidden md:block bg-white border border-gray-200 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-left text-xs uppercase text-gray-500">
            <tr>
              <th className="px-4 py-3">When</th>
              <th className="px-4 py-3">Actor</th>
              <th className="px-4 py-3">Action</th>
              <th className="px-4 py-3">Target</th>
              <th className="px-4 py-3">Company</th>
              <th className="px-4 py-3">Diff</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {items.map(e => (
              <tr key={e.id} className="align-top hover:bg-gray-50/60">
                <td className="px-4 py-3 whitespace-nowrap text-xs text-gray-600">{fmtTime(e.timestamp)}</td>
                <td className="px-4 py-3">
                  <div className="text-gray-800">{e.actorEmail ?? '—'}</div>
                  {e.actorRole && <div className="text-xs text-gray-400">{e.actorRole}</div>}
                </td>
                <td className="px-4 py-3">
                  <div className="text-gray-800">{humanize(e.actionCode)}</div>
                  <div className="text-[11px] text-gray-400 font-mono">{e.actionCode}</div>
                </td>
                <td className="px-4 py-3 text-xs text-gray-600">
                  {entityLabel(e.actionCode)}<br />
                  {e.targetEntityName && <span className="text-gray-500">{e.targetEntityName}</span>}
                </td>
                <td className="px-4 py-3 text-xs text-gray-600">
                  {e.targetCompanyId ? (companyName(e.targetCompanyId) ?? e.targetCompanyId.slice(0, 8)) : '—'}
                </td>
                <td className="px-4 py-3">{renderDiff(e)}</td>
              </tr>
            ))}
            {!loading && items.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-10 text-center text-sm text-gray-400">No audit entries match the filters.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Mobile cards */}
      <div className="md:hidden space-y-3">
        {items.map(e => (
          <div key={e.id} className="bg-white border border-gray-200 rounded-xl p-4 space-y-2">
            <div className="flex items-start justify-between gap-2">
              <div>
                <div className="text-sm font-semibold text-gray-900">{humanize(e.actionCode)}</div>
                <div className="text-[11px] text-gray-400 font-mono">{e.actionCode}</div>
              </div>
              <span className="text-[11px] text-gray-500 whitespace-nowrap">{fmtTime(e.timestamp)}</span>
            </div>
            <div className="flex items-center gap-1.5 text-xs text-gray-600">
              <span className="text-gray-400">by</span>
              <span className="font-medium">{e.actorEmail ?? '—'}</span>
              {e.actorRole && <span className="text-gray-400">({e.actorRole})</span>}
            </div>
            {renderDiff(e)}
          </div>
        ))}
        {!loading && items.length === 0 && (
          <div className="bg-white border border-gray-200 rounded-xl p-8 text-center text-sm text-gray-400">No audit entries match the filters.</div>
        )}
      </div>

      <Pager
        page={page}
        totalPages={Math.max(1, Math.ceil(total / pageSize))}
        hasPrev={page > 1}
        hasNext={page * pageSize < total}
        onChange={setPage}
        label={`${total} entries`}
      />
    </div>
  );
}