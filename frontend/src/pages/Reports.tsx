import { useCallback, useEffect, useState } from 'react';
import api from '../lib/api';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { usePermissions } from '../hooks/usePermissions';
import {
  Gauge, Fuel, Wrench, Award, Navigation, Bell, FileText, ArrowLeft, Download,
  Calendar, Plus, Trash2, Pause, Play, Clock,
} from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend } from 'recharts';
import { ModalSheet, ModalHeader, ModalFooter, BtnPrimary, BtnSecondary, FilterTabs, FilterChip } from '../components/ui';

interface ReportCatalogItem {
  code: string; name: string; description: string; icon: string;
  requiresView: string[]; requiresExport: string[]; canExport: boolean; canSchedule: boolean;
}
interface ReportColumn { key: string; label: string; type: string; }
interface ReportResult {
  reportType: string; title: string; generatedAt: string; from?: string; to?: string;
  columns: ReportColumn[]; rows: Record<string, any>[];
  series: { label: string; points: { x: string; y: number }[] }[];
  summary: { label: string; value: string }[];
}
interface ScheduledReport {
  id: string; reportType: string; title: string; cadence: string;
  dayOfWeek?: number; dayOfMonth?: number; isActive: boolean;
  nextRunAt?: string; lastRunAt?: string; lastRunStatus?: string; lastRunSummary?: string;
  parameters: { from?: string; to?: string; vehicleIds: string[]; driverIds: string[] };
}
interface VehicleOpt { id: string; registrationNumber: string; }
interface DriverOpt { id: string; fullName?: string; firstName?: string; lastName?: string; }

const ICONS: Record<string, any> = { gauge: Gauge, fuel: Fuel, wrench: Wrench, award: Award, navigation: Navigation, bell: Bell };
const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";

const fmtNum = (n: any, d = 1) => n == null ? '—' : Number(n).toLocaleString(undefined, { maximumFractionDigits: d });

function fmtCell(value: any, type: string): string {
  if (value == null) return '—';
  if (type === 'currency') return `$${Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
  if (type === 'number') return Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 });
  if (type === 'date') return new Date(value).toLocaleString();
  return String(value);
}

export default function ReportsPage() {
  const { can } = usePermissions();
  const { version: scopeVersion } = useCompanyScope();
  const [catalog, setCatalog] = useState<ReportCatalogItem[]>([]);
  const [selected, setSelected] = useState<ReportCatalogItem | null>(null);
  const [result, setResult] = useState<ReportResult | null>(null);
  const [loading, setLoading] = useState(false);

  // Filters
  const [preset, setPreset] = useState<'7d' | '30d' | '90d'>('30d');
  const [from, setFrom] = useState<string>('');
  const [to, setTo] = useState<string>('');
  const [vehicleId, setVehicleId] = useState<string>('');
  const [driverId, setDriverId] = useState<string>('');
  const [vehicles, setVehicles] = useState<VehicleOpt[]>([]);
  const [drivers, setDrivers] = useState<DriverOpt[]>([]);

  // Scheduled reports
  const [schedules, setSchedules] = useState<ScheduledReport[]>([]);
  const [scheduleOpen, setScheduleOpen] = useState(false);
  const [scheduleTitle, setScheduleTitle] = useState('');
  const [scheduleCadence, setScheduleCadence] = useState<'daily' | 'weekly' | 'monthly'>('daily');

  const canSchedule = can('report.create');

  const loadCatalog = useCallback(async () => {
    try {
      const res = await api.get('/reports/catalog');
      setCatalog(res.data.data || []);
    } catch (err) { console.error(err); }
  }, [scopeVersion]);

  const loadSchedules = useCallback(async () => {
    if (!canSchedule) return;
    try {
      const res = await api.get('/reports/schedules');
      setSchedules(res.data.data || []);
    } catch (err) { console.error(err); }
  }, [canSchedule, scopeVersion]);

  useEffect(() => { loadCatalog(); loadSchedules(); }, [loadCatalog, loadSchedules]);

  const applyPreset = (p: '7d' | '30d' | '90d') => {
    setPreset(p);
    const days = p === '7d' ? 7 : p === '90d' ? 90 : 30;
    setFrom(new Date(Date.now() - days * 86400000).toISOString().slice(0, 10));
    setTo(new Date().toISOString().slice(0, 10));
  };

  const openReport = async (item: ReportCatalogItem) => {
    setSelected(item);
    setResult(null);
    applyPreset('30d');
    setVehicleId(''); setDriverId('');
    try {
      const [v, d] = await Promise.all([
        api.get('/vehicles?pageSize=500'),
        api.get('/drivers?pageSize=500'),
      ]);
      setVehicles(v.data.data?.items || []);
      setDrivers(d.data.data?.items || []);
    } catch (err) { console.error(err); }
    await runReport(item.code);
  };

  const runReport = useCallback(async (code?: string) => {
    const reportCode = code || selected?.code;
    if (!reportCode) return;
    setLoading(true);
    try {
      const res = await api.post('/reports/run', {
        reportType: reportCode,
        from: from ? new Date(`${from}T00:00:00Z`).toISOString() : null,
        to: to ? new Date(`${to}T23:59:59Z`).toISOString() : null,
        vehicleIds: vehicleId ? [vehicleId] : [],
        driverIds: driverId ? [driverId] : [],
      });
      setResult(res.data.data);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [selected, from, to, vehicleId, driverId]);

  const downloadExport = async (format: 'csv' | 'pdf') => {
    if (!selected) return;
    try {
      const res = await api.get('/reports/export', {
        params: {
          type: selected.code, format,
          from: from ? new Date(`${from}T00:00:00Z`).toISOString() : undefined,
          to: to ? new Date(`${to}T23:59:59Z`).toISOString() : undefined,
          vehicleIds: vehicleId || undefined,
          driverIds: driverId || undefined,
        },
        responseType: 'blob',
      });
      const url = URL.createObjectURL(res.data);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${selected.code}-report.${format}`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err) { console.error(err); }
  };

  const saveSchedule = async () => {
    if (!selected) return;
    try {
      await api.post('/reports/schedules', {
        reportType: selected.code,
        title: scheduleTitle || `${selected.name} — scheduled`,
        parameters: {
          from: from ? new Date(`${from}T00:00:00Z`).toISOString() : null,
          to: to ? new Date(`${to}T23:59:59Z`).toISOString() : null,
          vehicleIds: vehicleId ? [vehicleId] : [],
          driverIds: driverId ? [driverId] : [],
        },
        cadence: scheduleCadence,
        dayOfWeek: scheduleCadence === 'weekly' ? 1 : null,
        dayOfMonth: scheduleCadence === 'monthly' ? 1 : null,
      });
      setScheduleOpen(false);
      setScheduleTitle('');
      loadSchedules();
    } catch (err) { console.error(err); }
  };

  const toggleSchedule = async (s: ScheduledReport) => {
    try {
      await api.put(`/reports/schedules/${s.id}`, { isActive: !s.isActive });
      loadSchedules();
    } catch (err) { console.error(err); }
  };

  const deleteSchedule = async (s: ScheduledReport) => {
    if (!window.confirm('Delete this scheduled report?')) return;
    try { await api.delete(`/reports/schedules/${s.id}`); loadSchedules(); } catch (err) { console.error(err); }
  };

  const chartData = result && result.series.length > 0
    ? result.series[0].points.map((p, i) => {
        const row: Record<string, any> = { x: p.x };
        result.series.forEach(s => { row[s.label] = s.points[i]?.y; });
        return row;
      })
    : [];

  // ── Landing (catalog) ────────────────────────────────────────────────────
  if (!selected) {
    return (
      <div className="space-y-6">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Reports</h2>
          <p className="text-sm text-gray-500 mt-0.5">Parameterized analytics over every fleet dataset — run, export, and schedule.</p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {catalog.map(item => {
            const Icon = ICONS[item.icon] || FileText;
            return (
              <button key={item.code} onClick={() => openReport(item)}
                className="text-left bg-white rounded-xl border border-gray-200 p-5 hover:border-blue-300 hover:shadow-sm transition-all">
                <div className="w-10 h-10 bg-blue-50 rounded-lg flex items-center justify-center mb-3">
                  <Icon className="w-5 h-5 text-blue-600" />
                </div>
                <h3 className="font-semibold text-gray-900">{item.name}</h3>
                <p className="text-sm text-gray-500 mt-1 min-h-[40px]">{item.description}</p>
                <div className="flex items-center gap-2 mt-3">
                  {item.canExport && <span className="px-2 py-0.5 rounded-full text-[10px] font-medium bg-green-100 text-green-700">CSV · PDF</span>}
                  {item.canSchedule && <span className="px-2 py-0.5 rounded-full text-[10px] font-medium bg-indigo-100 text-indigo-700">Schedulable</span>}
                </div>
              </button>
            );
          })}
        </div>

        {canSchedule && (
          <div className="bg-white rounded-xl border border-gray-200">
            <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-900 flex items-center gap-2">
                <Clock className="w-4 h-4 text-indigo-500" /> My Scheduled Reports
              </h3>
              <span className="text-xs text-gray-400">{schedules.length} active</span>
            </div>
            {schedules.length === 0 ? (
              <p className="px-5 py-8 text-sm text-gray-400 text-center">No scheduled reports yet — open a report and pick “Save as scheduled”.</p>
            ) : (
              <div className="divide-y divide-gray-50">
                {schedules.map(s => (
                  <div key={s.id} className="px-5 py-3 flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <p className="text-sm font-medium text-gray-900">{s.title}</p>
                      <p className="text-xs text-gray-500">
                        {s.cadence === 'daily' ? 'Daily' : s.cadence === 'weekly' ? 'Weekly' : 'Monthly'}
                        {s.nextRunAt ? ` · next ${new Date(s.nextRunAt).toLocaleString()}` : ''}
                        {s.lastRunAt ? ` · last ${new Date(s.lastRunAt).toLocaleDateString()}` : ''}
                        {s.lastRunStatus ? ` · ${s.lastRunStatus === 'ok' ? '✓ ok' : '✗ ' + (s.lastRunSummary || 'error')}` : ''}
                      </p>
                    </div>
                    <div className="flex items-center gap-2">
                      <button onClick={() => toggleSchedule(s)}
                        className="p-2 rounded-lg hover:bg-gray-100 text-gray-600" title={s.isActive ? 'Pause' : 'Resume'}>
                        {s.isActive ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                      </button>
                      <button onClick={() => deleteSchedule(s)}
                        className="p-2 rounded-lg hover:bg-red-50 text-red-500" title="Delete">
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    );
  }

  // ── Report view ──────────────────────────────────────────────────────────
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <button onClick={() => { setSelected(null); }} className="p-2 rounded-lg hover:bg-gray-100 text-gray-500" title="Back to catalog">
            <ArrowLeft className="w-5 h-5" />
          </button>
          <div>
            <h2 className="text-xl font-bold text-gray-900">{selected.name}</h2>
            <p className="text-sm text-gray-500 mt-0.5">{selected.description}</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <FilterTabs>
            {(['7d', '30d', '90d'] as const).map(p => (
              <FilterChip key={p} active={preset === p} onClick={() => applyPreset(p)}>{p}</FilterChip>
            ))}
          </FilterTabs>
          <input type="date" value={from} onChange={e => { setFrom(e.target.value); setPreset('30d'); }}
            className={`${INPUT} w-36`} title="From" />
          <input type="date" value={to} onChange={e => setTo(e.target.value)}
            className={`${INPUT} w-36`} title="To" />
          <button onClick={() => runReport()} className="min-h-[44px] px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 flex items-center gap-2">
            <Calendar className="w-4 h-4" /> Run
          </button>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <label className={LABEL}>Vehicle</label>
          <select value={vehicleId} onChange={e => setVehicleId(e.target.value)} className={`${INPUT} min-w-[180px] bg-white`}>
            <option value="">All vehicles</option>
            {vehicles.map(v => <option key={v.id} value={v.id}>{v.registrationNumber}</option>)}
          </select>
        </div>
        <div>
          <label className={LABEL}>Driver</label>
          <select value={driverId} onChange={e => setDriverId(e.target.value)} className={`${INPUT} min-w-[180px] bg-white`}>
            <option value="">All drivers</option>
            {drivers.map(d => <option key={d.id} value={d.id}>{d.fullName || `${d.firstName || ''} ${d.lastName || ''}`.trim()}</option>)}
          </select>
        </div>
        <div className="flex items-center gap-2 pb-1">
          {selected.canExport && (
            <>
              <button onClick={() => downloadExport('csv')} className="min-h-[44px] px-4 py-2 border border-gray-300 text-sm font-medium rounded-lg hover:bg-gray-50 flex items-center gap-2">
                <Download className="w-4 h-4" /> CSV
              </button>
              <button onClick={() => downloadExport('pdf')} className="min-h-[44px] px-4 py-2 border border-gray-300 text-sm font-medium rounded-lg hover:bg-gray-50 flex items-center gap-2">
                <Download className="w-4 h-4" /> PDF
              </button>
            </>
          )}
          {selected.canSchedule && (
            <button onClick={() => setScheduleOpen(true)} className="min-h-[44px] px-4 py-2 border border-indigo-300 text-indigo-700 text-sm font-medium rounded-lg hover:bg-indigo-50 flex items-center gap-2">
              <Plus className="w-4 h-4" /> Save as scheduled
            </button>
          )}
        </div>
      </div>

      {loading && <p className="text-sm text-gray-400">Generating report…</p>}

      {result && (
        <>
          {/* Summary */}
          <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-4">
            {result.summary.map(s => (
              <div key={s.label} className="bg-white rounded-xl border border-gray-200 p-4">
                <p className="text-lg font-bold text-gray-900 truncate">{s.value}</p>
                <p className="text-xs text-gray-500 mt-0.5">{s.label}</p>
              </div>
            ))}
          </div>

          {/* Chart */}
          {chartData.length > 0 && (
            <div className="bg-white rounded-xl border border-gray-200 p-5">
              <h3 className="text-sm font-semibold text-gray-900 mb-4">Trend</h3>
              <ResponsiveContainer width="100%" height={240}>
                <LineChart data={chartData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                  <XAxis dataKey="x" tick={{ fontSize: 10 }} minTickGap={30} />
                  <YAxis tick={{ fontSize: 10 }} />
                  <Tooltip />
                  <Legend wrapperStyle={{ fontSize: 12 }} />
                  {result.series.map((s, i) => (
                    <Line key={s.label} type="monotone" dataKey={s.label} name={s.label}
                      stroke={['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6'][i % 5]} strokeWidth={2} dot={false} />
                  ))}
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}

          {/* Table */}
          <div className="bg-white rounded-xl border border-gray-200">
            <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-900">Data</h3>
              <span className="text-xs text-gray-400">{result.rows.length} rows · generated {new Date(result.generatedAt).toLocaleString()}</span>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-gray-500 border-b border-gray-100 uppercase tracking-wide">
                    {result.columns.map(c => <th key={c.key} className="px-5 py-3">{c.label}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {result.rows.map((row, i) => (
                    <tr key={i} className="border-b border-gray-50 hover:bg-gray-50">
                      {result.columns.map(c => (
                        <td key={c.key} className="px-5 py-3 text-gray-600 whitespace-nowrap">{fmtCell(row[c.key], c.type)}</td>
                      ))}
                    </tr>
                  ))}
                  {result.rows.length === 0 && (
                    <tr><td colSpan={result.columns.length} className="px-5 py-8 text-center text-sm text-gray-400">No data in this window.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}

      {scheduleOpen && (
        <ModalSheet onClose={() => setScheduleOpen(false)} maxWidth="max-w-md">
          <ModalHeader title="Save as scheduled report" onClose={() => setScheduleOpen(false)} />
          <div className="p-5 space-y-4">
            <div>
              <label className={LABEL}>Title</label>
              <input value={scheduleTitle} onChange={e => setScheduleTitle(e.target.value)}
                placeholder={`${selected.name} — scheduled`} className={INPUT} />
            </div>
            <div>
              <label className={LABEL}>Cadence</label>
              <select value={scheduleCadence} onChange={e => setScheduleCadence(e.target.value as any)} className={`${INPUT} bg-white`}>
                <option value="daily">Daily</option>
                <option value="weekly">Weekly (Mondays)</option>
                <option value="monthly">Monthly (1st)</option>
              </select>
            </div>
            <p className="text-xs text-gray-500">Current filters (date range, vehicle, driver) are captured. The report is generated automatically and delivered to your notification inbox.</p>
          </div>
          <ModalFooter>
            <button className={BtnSecondary} onClick={() => setScheduleOpen(false)}>Cancel</button>
            <button className={BtnPrimary} onClick={saveSchedule}>Save schedule</button>
          </ModalFooter>
        </ModalSheet>
      )}
    </div>
  );
}