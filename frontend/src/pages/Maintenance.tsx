import { useCallback, useEffect, useState } from 'react';
import api from '../lib/api';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { usePermissions } from '../hooks/usePermissions';
import { Wrench, Plus, AlertTriangle, CalendarClock, CheckCircle2, ClipboardList, Trash2 } from 'lucide-react';
import { ModalSheet, ModalHeader, ModalFooter, ResponsiveCards, DetailField, BtnPrimary, BtnSecondary } from '../components/ui';

interface Schedule {
  id: string; vehicleId: string; vehicleRegistration?: string; vehicleName?: string;
  title: string; serviceType: string; triggerType: number; intervalValue: number; dueLeadValue?: number;
  lastServiceOdometer?: number; lastServiceDate?: string; nextDueOdometer?: number; nextDueDate?: string; nextDueEngineHours?: number;
  isActive: boolean; dueStatus: number; dueStatusDetail?: string;
}
interface MaintenanceRecord {
  id: string; vehicleId: string; vehicleRegistration?: string; title: string;
  recordType: number; breakdownSeverity?: string; downtimeHours?: number; rootCause?: string;
  workshop?: string; cost?: number; currency?: string; odometerAtService?: number; completedDate?: string; notes?: string;
}
interface Overview {
  totalSchedules: number; overdueCount: number; dueSoonCount: number; okCount: number; noDataCount: number;
  breakdownCount30d: number; schedules: Schedule[]; recentBreakdowns: MaintenanceRecord[];
}
interface VehicleOpt { id: string; registrationNumber: string; status: number; }

const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";
const SERVICE_TYPES: Record<string, string> = {
  oil_change: 'Oil Change', tire_rotation: 'Tyre Rotation', brake_inspection: 'Brake Inspection',
  general_inspection: 'General Inspection', custom: 'Custom',
};
const TRIGGER_LABELS = ['Mileage (km)', 'Time (days)', 'Engine hours'];
const fmtMoney = (n?: number) => n == null ? '—' : `$${n.toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
const fmtNum = (n?: number, d = 0) => n == null ? '—' : n.toLocaleString(undefined, { maximumFractionDigits: d });

const STATUS_STYLE: Record<number, { label: string; cls: string }> = {
  0: { label: 'OK', cls: 'bg-green-100 text-green-700' },
  1: { label: 'Due Soon', cls: 'bg-amber-100 text-amber-700' },
  2: { label: 'Overdue', cls: 'bg-red-100 text-red-700' },
  3: { label: 'No Data', cls: 'bg-gray-100 text-gray-600' },
};

export default function MaintenancePage() {
  const { can } = usePermissions();
  const { version: scopeVersion } = useCompanyScope();
  const canCreate = can('maintenance.create');
  const canDelete = can('maintenance.delete');

  const [overview, setOverview] = useState<Overview | null>(null);
  const [vehicles, setVehicles] = useState<VehicleOpt[]>([]);
  const [loading, setLoading] = useState(true);
  const [addScheduleOpen, setAddScheduleOpen] = useState(false);
  const [logServiceOpen, setLogServiceOpen] = useState(false);

  const fetchAll = useCallback(async () => {
    setLoading(true);
    try {
      const [o, v] = await Promise.all([
        api.get('/maintenance/overview'),
        api.get('/vehicles?pageSize=500'),
      ]);
      setOverview(o.data.data);
      setVehicles(v.data.data?.items || []);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [scopeVersion]);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  const deleteSchedule = async (id: string) => {
    if (!window.confirm('Delete this maintenance schedule?')) return;
    try { await api.delete(`/maintenance/schedules/${id}`); fetchAll(); } catch (err) { console.error(err); }
  };

  const setVehicleInMaintenance = async (v: VehicleOpt) => {
    if (!window.confirm(`Set vehicle ${v.registrationNumber} to "In Maintenance" status?`)) return;
    try { await api.put(`/vehicles/${v.id}`, { status: 2 }); fetchAll(); } catch (err) { console.error(err); }
  };

  const overdue = overview?.schedules.filter(s => s.dueStatus === 2) || [];
  const dueSoon = overview?.schedules.filter(s => s.dueStatus === 1) || [];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Maintenance Management</h2>
          <p className="text-sm text-gray-500 mt-0.5">Predictive scheduling — what needs attention today</p>
        </div>
        <div className="flex items-center gap-2">
          {canCreate && (
            <>
              <button onClick={() => setLogServiceOpen(true)} className="min-h-[44px] px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 flex items-center gap-2">
                <Plus className="w-4 h-4" /> Log Service
              </button>
              <button onClick={() => setAddScheduleOpen(true)} className="min-h-[44px] px-4 py-2 border border-gray-300 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-50 flex items-center gap-2">
                <Plus className="w-4 h-4" /> Add Schedule
              </button>
            </>
          )}
        </div>
      </div>

      {/* Attention board */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <div className="px-5 py-3.5 border-b border-gray-100 flex items-center gap-2 bg-red-50/50">
            <AlertTriangle className="w-4 h-4 text-red-500" />
            <h3 className="text-sm font-semibold text-gray-900">Overdue ({overdue.length})</h3>
          </div>
          <div className="divide-y divide-gray-50 max-h-72 overflow-y-auto">
            {overdue.length === 0 ? (
              <div className="px-5 py-8 text-center text-sm text-gray-400">Nothing overdue — good work.</div>
            ) : overdue.map(s => (
              <div key={s.id} className="px-5 py-3 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="text-sm font-medium text-gray-900 truncate">{s.vehicleRegistration} — {s.title}</div>
                  <div className="text-xs text-red-600">{s.dueStatusDetail}</div>
                </div>
                <span className="shrink-0 px-2 py-0.5 rounded-full text-xs font-medium bg-red-100 text-red-700">Overdue</span>
              </div>
            ))}
          </div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <div className="px-5 py-3.5 border-b border-gray-100 flex items-center gap-2 bg-amber-50/50">
            <CalendarClock className="w-4 h-4 text-amber-500" />
            <h3 className="text-sm font-semibold text-gray-900">Due Soon ({dueSoon.length})</h3>
          </div>
          <div className="divide-y divide-gray-50 max-h-72 overflow-y-auto">
            {dueSoon.length === 0 ? (
              <div className="px-5 py-8 text-center text-sm text-gray-400">No services in the lead window.</div>
            ) : dueSoon.map(s => (
              <div key={s.id} className="px-5 py-3 flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="text-sm font-medium text-gray-900 truncate">{s.vehicleRegistration} — {s.title}</div>
                  <div className="text-xs text-amber-600">{s.dueStatusDetail}</div>
                </div>
                <span className="shrink-0 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-700">Due soon</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        {[
          { label: 'Active Schedules', value: overview?.totalSchedules ?? 0, cls: 'text-gray-900' },
          { label: 'Overdue', value: overview?.overdueCount ?? 0, cls: overview?.overdueCount ? 'text-red-600' : 'text-gray-900' },
          { label: 'Due Soon', value: overview?.dueSoonCount ?? 0, cls: overview?.dueSoonCount ? 'text-amber-600' : 'text-gray-900' },
          { label: 'On Track', value: overview?.okCount ?? 0, cls: 'text-green-600' },
          { label: 'Breakdowns (30d)', value: overview?.breakdownCount30d ?? 0, cls: overview?.breakdownCount30d ? 'text-red-600' : 'text-gray-900' },
        ].map(c => (
          <div key={c.label} className="bg-white rounded-xl border border-gray-200 p-4">
            <p className={`text-2xl font-bold ${c.cls}`}>{c.value}</p>
            <p className="text-xs text-gray-500 mt-0.5">{c.label}</p>
          </div>
        ))}
      </div>

      {/* All schedules */}
      <div className="bg-white rounded-xl border border-gray-200">
        <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-gray-900 flex items-center gap-2"><Wrench className="w-4 h-4 text-blue-500" /> All Schedules</h3>
          <span className="text-xs text-gray-400">sorted by urgency</span>
        </div>
        <div className="hidden md:block overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-gray-500 border-b border-gray-100 uppercase tracking-wide">
                <th className="px-5 py-3">Vehicle</th>
                <th className="px-3 py-3">Service</th>
                <th className="px-3 py-3">Trigger</th>
                <th className="px-3 py-3">Interval</th>
                <th className="px-3 py-3">Next Due</th>
                <th className="px-3 py-3">Last Serviced</th>
                <th className="px-3 py-3">Status</th>
                <th className="px-3 py-3"></th>
              </tr>
            </thead>
            <tbody>
              {(overview?.schedules || []).map(s => (
                <tr key={s.id} className="border-b border-gray-50 hover:bg-gray-50">
                  <td className="px-5 py-3 font-medium text-gray-900">{s.vehicleRegistration || '—'}</td>
                  <td className="px-3 py-3">{s.title}</td>
                  <td className="px-3 py-3 text-gray-600">{TRIGGER_LABELS[s.triggerType] || '—'}</td>
                  <td className="px-3 py-3 text-gray-600">{fmtNum(s.intervalValue, 0)}</td>
                  <td className="px-3 py-3 text-gray-600">
                    {s.nextDueOdometer != null ? `${fmtNum(s.nextDueOdometer)} km` : s.nextDueDate ? new Date(s.nextDueDate).toLocaleDateString() : s.nextDueEngineHours != null ? `${fmtNum(s.nextDueEngineHours)} h` : '—'}
                  </td>
                  <td className="px-3 py-3 text-gray-600">{s.lastServiceDate ? new Date(s.lastServiceDate).toLocaleDateString() : s.lastServiceOdometer != null ? `${fmtNum(s.lastServiceOdometer)} km` : 'Never'}</td>
                  <td className="px-3 py-3">
                    <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_STYLE[s.dueStatus]?.cls || 'bg-gray-100 text-gray-600'}`} title={s.dueStatusDetail}>
                      {STATUS_STYLE[s.dueStatus]?.label || '—'}
                    </span>
                  </td>
                  <td className="px-3 py-3">
                    {canDelete && (
                      <button onClick={() => deleteSchedule(s.id)} aria-label="Delete schedule" className="p-2 hover:bg-red-50 rounded-lg text-gray-400 hover:text-red-600 min-w-[44px] min-h-[44px]">
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {(overview?.schedules || []).length === 0 && !loading && (
                <tr><td colSpan={8} className="px-5 py-10 text-center text-gray-400">No schedules yet — add one to start predictive maintenance</td></tr>
              )}
            </tbody>
          </table>
        </div>
        <ResponsiveCards
          items={overview?.schedules || []}
          loading={loading}
          empty="No schedules yet"
          keyOf={s => s.id}
          title={s => `${s.vehicleRegistration || 'Vehicle'} — ${s.title}`}
          statusChip={s => <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_STYLE[s.dueStatus]?.cls || 'bg-gray-100 text-gray-600'}`}>{STATUS_STYLE[s.dueStatus]?.label || '—'}</span>}
          primary={s => [
            { label: 'Trigger', value: TRIGGER_LABELS[s.triggerType] || '—' },
            { label: 'Interval', value: fmtNum(s.intervalValue, 0) },
            { label: 'Next due', value: s.nextDueOdometer != null ? `${fmtNum(s.nextDueOdometer)} km` : s.nextDueDate ? new Date(s.nextDueDate).toLocaleDateString() : '—' },
            { label: 'Status detail', value: s.dueStatusDetail || '—' },
          ]}
          actions={s => canDelete ? [{ key: 'delete', label: 'Delete schedule', icon: <Trash2 className="w-4 h-4" />, onClick: () => deleteSchedule(s.id), danger: true }] : []}
        />
      </div>

      {/* Recent breakdowns + vehicle status suggestion */}
      <div className="bg-white rounded-xl border border-gray-200">
        <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-gray-900 flex items-center gap-2"><ClipboardList className="w-4 h-4 text-red-500" /> Recent Breakdowns (30d)</h3>
          <span className="text-xs text-gray-400">unscheduled service events</span>
        </div>
        {(overview?.recentBreakdowns || []).length === 0 ? (
          <div className="px-5 py-8 text-center text-sm text-gray-400">No breakdowns logged in the last 30 days.</div>
        ) : (
          <div className="divide-y divide-gray-50">
            {(overview?.recentBreakdowns || []).map(r => {
              const vehicle = vehicles.find(v => v.id === r.vehicleId);
              const alreadyInMaint = vehicle?.status === 2;
              return (
                <div key={r.id} className="px-5 py-3.5 flex flex-wrap items-center justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-gray-900">{r.vehicleRegistration} — {r.title}</div>
                    <div className="text-xs text-gray-500 mt-0.5">
                      {r.completedDate ? new Date(r.completedDate).toLocaleDateString() : '—'}
                      {r.breakdownSeverity && <span className={`ml-2 px-1.5 py-0.5 rounded text-[10px] font-medium ${r.breakdownSeverity === 'critical' ? 'bg-red-100 text-red-700' : r.breakdownSeverity === 'major' ? 'bg-amber-100 text-amber-700' : 'bg-gray-100 text-gray-600'}`}>{r.breakdownSeverity}</span>}
                      {r.downtimeHours ? <span className="ml-2 text-gray-400">{fmtNum(r.downtimeHours, 1)} h downtime</span> : null}
                      {r.rootCause ? <span className="ml-2 text-gray-400">root cause: {r.rootCause}</span> : null}
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    {can('vehicle.update') && vehicle && !alreadyInMaint && (
                      <button onClick={() => setVehicleInMaintenance(vehicle)} className="text-xs font-medium text-blue-600 hover:bg-blue-50 px-3 py-2 rounded-lg min-h-[36px]">
                        Set vehicle In Maintenance
                      </button>
                    )}
                    {alreadyInMaint && <span className="text-xs text-green-600 flex items-center gap-1"><CheckCircle2 className="w-3.5 h-3.5" /> In maintenance</span>}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {addScheduleOpen && <AddScheduleModal vehicles={vehicles} onClose={() => setAddScheduleOpen(false)} onSaved={() => { setAddScheduleOpen(false); fetchAll(); }} />}
      {logServiceOpen && <LogServiceModal vehicles={vehicles} onClose={() => setLogServiceOpen(false)} onSaved={() => { setLogServiceOpen(false); fetchAll(); }} />}
    </div>
  );
}

function AddScheduleModal({ vehicles, onClose, onSaved }: { vehicles: VehicleOpt[]; onClose: () => void; onSaved: () => void }) {
  const [vehicleId, setVehicleId] = useState('');
  const [title, setTitle] = useState('');
  const [serviceType, setServiceType] = useState('general_inspection');
  const [triggerType, setTriggerType] = useState(0);
  const [intervalValue, setIntervalValue] = useState('');
  const [dueLead, setDueLead] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const submit = async () => {
    if (!vehicleId || !intervalValue || parseFloat(intervalValue) <= 0) { setError('Vehicle and interval are required'); return; }
    setSaving(true); setError('');
    try {
      await api.post('/maintenance/schedules', {
        vehicleId, title: title || SERVICE_TYPES[serviceType] || serviceType,
        serviceType, triggerType, intervalValue: parseFloat(intervalValue),
        dueLeadValue: dueLead ? parseFloat(dueLead) : null,
      });
      onSaved();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to create schedule');
      setSaving(false);
    }
  };

  return (
    <ModalSheet onClose={onClose}>
      <ModalHeader title="Add Maintenance Schedule" onClose={onClose} />
      <div className="flex-1 overflow-y-auto px-4 sm:px-6 py-5 space-y-4">
        <div>
          <label className={LABEL}>Vehicle *</label>
          <select value={vehicleId} onChange={e => setVehicleId(e.target.value)} className={INPUT}>
            <option value="">Select vehicle</option>
            {vehicles.map(v => <option key={v.id} value={v.id}>{v.registrationNumber}</option>)}
          </select>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className={LABEL}>Title</label>
            <input value={title} onChange={e => setTitle(e.target.value)} className={INPUT} placeholder="e.g. Engine oil change" />
          </div>
          <div>
            <label className={LABEL}>Service type</label>
            <select value={serviceType} onChange={e => setServiceType(e.target.value)} className={INPUT}>
              {Object.entries(SERVICE_TYPES).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
            </select>
          </div>
          <div>
            <label className={LABEL}>Trigger type</label>
            <select value={triggerType} onChange={e => setTriggerType(Number(e.target.value))} className={INPUT}>
              <option value={0}>Mileage (km)</option>
              <option value={1}>Time (days)</option>
              <option value={2}>Engine hours</option>
            </select>
          </div>
          <div>
            <label className={LABEL}>Interval *</label>
            <input type="number" step="1" value={intervalValue} onChange={e => setIntervalValue(e.target.value)} className={INPUT} placeholder={triggerType === 0 ? 'e.g. 10000' : triggerType === 1 ? 'e.g. 180' : 'e.g. 500'} />
          </div>
          <div>
            <label className={LABEL}>Due-soon lead ({triggerType === 0 ? 'km' : triggerType === 1 ? 'days' : 'hours'})</label>
            <input type="number" step="1" value={dueLead} onChange={e => setDueLead(e.target.value)} className={INPUT} placeholder="e.g. 500" />
          </div>
        </div>
        <p className="text-xs text-gray-400">The first due point is seeded from the vehicle's current odometer/engine hours. Logging a preventive service against this schedule rolls the next due point forward.</p>
        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
      <ModalFooter>
        <button className={BtnSecondary} onClick={onClose}>Cancel</button>
        <button className={BtnPrimary} onClick={submit} disabled={saving}>{saving ? 'Saving…' : 'Add Schedule'}</button>
      </ModalFooter>
    </ModalSheet>
  );
}

function LogServiceModal({ vehicles, onClose, onSaved }: { vehicles: VehicleOpt[]; onClose: () => void; onSaved: () => void }) {
  const [vehicleId, setVehicleId] = useState('');
  const [title, setTitle] = useState('');
  const [recordType, setRecordType] = useState<0 | 1>(0);
  const [schedules, setSchedules] = useState<Schedule[]>([]);
  const [scheduleId, setScheduleId] = useState('');
  const [severity, setSeverity] = useState('minor');
  const [downtime, setDowntime] = useState('');
  const [rootCause, setRootCause] = useState('');
  const [workshop, setWorkshop] = useState('');
  const [cost, setCost] = useState('');
  const [odometer, setOdometer] = useState('');
  const [engineHours, setEngineHours] = useState('');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (vehicleId && recordType === 0) {
      api.get(`/maintenance?vehicleId=${vehicleId}`).then(r => setSchedules(r.data.data || [])).catch(() => setSchedules([]));
    } else setSchedules([]);
  }, [vehicleId, recordType]);

  const submit = async () => {
    if (!vehicleId || !title) { setError('Vehicle and title are required'); return; }
    setSaving(true); setError('');
    try {
      await api.post('/maintenance/records', {
        vehicleId, title, recordType,
        maintenanceScheduleId: recordType === 0 && scheduleId ? scheduleId : null,
        breakdownSeverity: recordType === 1 ? severity : null,
        downtimeHours: recordType === 1 && downtime ? parseFloat(downtime) : null,
        rootCause: recordType === 1 ? rootCause || null : null,
        workshop: workshop || null, cost: cost ? parseFloat(cost) : null,
        odometerAtService: odometer ? parseFloat(odometer) : null,
        engineHoursAtService: engineHours ? parseFloat(engineHours) : null,
        notes: notes || null,
      });
      onSaved();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to log service');
      setSaving(false);
    }
  };

  return (
    <ModalSheet onClose={onClose} maxWidth="max-w-2xl">
      <ModalHeader title="Log Service" onClose={onClose} />
      <div className="flex-1 overflow-y-auto px-4 sm:px-6 py-5 space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className={LABEL}>Vehicle *</label>
            <select value={vehicleId} onChange={e => setVehicleId(e.target.value)} className={INPUT}>
              <option value="">Select vehicle</option>
              {vehicles.map(v => <option key={v.id} value={v.id}>{v.registrationNumber}</option>)}
            </select>
          </div>
          <div>
            <label className={LABEL}>Title *</label>
            <input value={title} onChange={e => setTitle(e.target.value)} className={INPUT} placeholder="e.g. Engine oil change" />
          </div>
        </div>
        <div>
          <label className={LABEL}>Type</label>
          <div className="flex gap-2">
            <button onClick={() => setRecordType(0)} className={`flex-1 px-3 py-2 rounded-lg text-sm font-medium border ${recordType === 0 ? 'border-blue-600 bg-blue-50 text-blue-700' : 'border-gray-300 text-gray-600'}`}>
              Preventive (scheduled)
            </button>
            <button onClick={() => setRecordType(1)} className={`flex-1 px-3 py-2 rounded-lg text-sm font-medium border ${recordType === 1 ? 'border-red-600 bg-red-50 text-red-700' : 'border-gray-300 text-gray-600'}`}>
              Breakdown (unscheduled)
            </button>
          </div>
        </div>
        {recordType === 0 && (
          <div>
            <label className={LABEL}>Complete schedule (optional)</label>
            <select value={scheduleId} onChange={e => setScheduleId(e.target.value)} className={INPUT} disabled={!vehicleId}>
              <option value="">— standalone service —</option>
              {schedules.map(s => <option key={s.id} value={s.id}>{s.title}</option>)}
            </select>
          </div>
        )}
        {recordType === 1 && (
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className={LABEL}>Severity</label>
              <select value={severity} onChange={e => setSeverity(e.target.value)} className={INPUT}>
                <option value="minor">Minor</option>
                <option value="major">Major</option>
                <option value="critical">Critical</option>
              </select>
            </div>
            <div>
              <label className={LABEL}>Downtime (hours)</label>
              <input type="number" step="0.5" value={downtime} onChange={e => setDowntime(e.target.value)} className={INPUT} placeholder="e.g. 6" />
            </div>
            <div className="col-span-2">
              <label className={LABEL}>Root cause</label>
              <input value={rootCause} onChange={e => setRootCause(e.target.value)} className={INPUT} placeholder="e.g. Failed water pump — belt snapped" />
            </div>
          </div>
        )}
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
          <div>
            <label className={LABEL}>Workshop / vendor</label>
            <input value={workshop} onChange={e => setWorkshop(e.target.value)} className={INPUT} />
          </div>
          <div>
            <label className={LABEL}>Cost</label>
            <input type="number" step="0.01" value={cost} onChange={e => setCost(e.target.value)} className={INPUT} />
          </div>
          <div>
            <label className={LABEL}>Odometer (km)</label>
            <input type="number" value={odometer} onChange={e => setOdometer(e.target.value)} className={INPUT} />
          </div>
          <div>
            <label className={LABEL}>Engine hours</label>
            <input type="number" value={engineHours} onChange={e => setEngineHours(e.target.value)} className={INPUT} />
          </div>
          <div className="sm:col-span-2">
            <label className={LABEL}>Notes</label>
            <input value={notes} onChange={e => setNotes(e.target.value)} className={INPUT} />
          </div>
        </div>
        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
      <ModalFooter>
        <button className={BtnSecondary} onClick={onClose}>Cancel</button>
        <button className={BtnPrimary} onClick={submit} disabled={saving}>{saving ? 'Saving…' : 'Log Service'}</button>
      </ModalFooter>
    </ModalSheet>
  );
}