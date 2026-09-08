import { useCallback, useEffect, useState } from 'react';
import api from '../lib/api';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { usePermissions } from '../hooks/usePermissions';
import { FUEL_TYPE } from '../lib/constants';
import { Fuel, Plus, Trash2, AlertTriangle, Droplets, DollarSign, Gauge, TrendingDown } from 'lucide-react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend } from 'recharts';
import { ModalSheet, ModalHeader, ModalFooter, ResponsiveCards, DetailField, BtnPrimary, BtnSecondary, FilterTabs, FilterChip } from '../components/ui';

interface FuelRecord {
  id: string; vehicleId: string; vehicleRegistration?: string; fuelType: number;
  quantity: number; unit?: string; pricePerUnit?: number; totalCost?: number;
  odometerReading?: number; source?: string; station?: string; distanceTraveledKm?: number;
  isAnomaly: boolean; anomalyReason?: string; notes?: string; recordDate: string;
  efficiencyKmPerLiter?: number; consumptionLitersPer100km?: number;
}
interface FuelStats {
  transactionCount: number; totalLiters: number; totalSpend: number; avgPricePerLiter?: number;
  avgEfficiencyKmPerLiter?: number; costPerKm?: number; anomalyCount: number;
  sensorCoveredVehicles: number; manualVehicles: number;
}
interface TrendPoint { day: string; liters: number; spend: number; efficiencyKmPerLiter?: number; }
interface VehicleOpt { id: string; registrationNumber: string; }

const INPUT = "w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500";
const LABEL = "block text-sm font-medium text-gray-700 mb-1";
const fmtMoney = (n?: number) => n == null ? '—' : `$${n.toLocaleString(undefined, { maximumFractionDigits: 2 })}`;
const fmtNum = (n?: number, d = 1) => n == null ? '—' : n.toLocaleString(undefined, { maximumFractionDigits: d });

export default function FuelPage() {
  const { can } = usePermissions();
  const { version: scopeVersion } = useCompanyScope();
  const canCreate = can('fuel.create');
  const canDelete = can('fuel.delete');

  const [vehicles, setVehicles] = useState<VehicleOpt[]>([]);
  const [vehicleFilter, setVehicleFilter] = useState<string>('');
  const [range, setRange] = useState<'7d' | '30d' | '90d'>('30d');
  const [stats, setStats] = useState<FuelStats | null>(null);
  const [trend, setTrend] = useState<TrendPoint[]>([]);
  const [records, setRecords] = useState<FuelRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [logOpen, setLogOpen] = useState(false);

  const days = range === '7d' ? 7 : range === '90d' ? 90 : 30;

  const fetchAll = useCallback(async () => {
    setLoading(true);
    const q = `?days=${days}`;
    const from = new Date(Date.now() - days * 86400000).toISOString();
    try {
      const [s, t, r, v] = await Promise.all([
        api.get(`/fuel/stats?from=${encodeURIComponent(from)}`),
        api.get(`/fuel/trend?from=${encodeURIComponent(from)}`),
        api.get(`/fuel?pageSize=100&from=${encodeURIComponent(from)}${vehicleFilter ? `&vehicleId=${vehicleFilter}` : ''}`),
        api.get('/vehicles?pageSize=500'),
      ]);
      setStats(s.data.data);
      setTrend(t.data.data || []);
      setRecords(r.data.data?.items || []);
      setVehicles(v.data.data?.items || []);
    } catch (err) { console.error(err); }
    setLoading(false);
  }, [days, vehicleFilter, scopeVersion]);

  useEffect(() => { fetchAll(); }, [fetchAll]);

  const deleteRecord = async (id: string) => {
    if (!window.confirm('Delete this fuel record?')) return;
    try { await api.delete(`/fuel/${id}`); fetchAll(); } catch (err) { console.error(err); }
  };

  const statCards = [
    { label: 'Total Spend', value: fmtMoney(stats?.totalSpend), sub: `${range} window`, icon: DollarSign, color: 'bg-green-500' },
    { label: 'Fuel Consumed', value: `${fmtNum(stats?.totalLiters, 0)} L`, sub: `${stats?.transactionCount ?? 0} fill-ups`, icon: Droplets, color: 'bg-blue-500' },
    { label: 'Avg Efficiency', value: stats?.avgEfficiencyKmPerLiter != null ? `${fmtNum(stats.avgEfficiencyKmPerLiter, 2)} km/L` : '—', sub: 'distance ÷ liters', icon: Gauge, color: 'bg-purple-500' },
    { label: 'Cost per km', value: fmtMoney(stats?.costPerKm), sub: 'total spend ÷ distance', icon: TrendingDown, color: 'bg-amber-500' },
    { label: 'Anomalies', value: stats?.anomalyCount ?? 0, sub: 'suspicious drops flagged', icon: AlertTriangle, color: stats?.anomalyCount ? 'bg-red-500' : 'bg-gray-400' },
  ];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-xl font-bold text-gray-900">Fuel Management</h2>
          <p className="text-sm text-gray-500 mt-0.5">Transactions, consumption analytics and anomaly detection</p>
        </div>
        <div className="flex items-center gap-2">
          <FilterTabs>
            {(['7d', '30d', '90d'] as const).map(r => (
              <FilterChip key={r} active={range === r} onClick={() => setRange(r)}>{r}</FilterChip>
            ))}
          </FilterTabs>
          <select value={vehicleFilter} onChange={e => setVehicleFilter(e.target.value)} className="px-3 py-2 border border-gray-300 rounded-lg text-sm bg-white min-h-[44px]">
            <option value="">All vehicles</option>
            {vehicles.map(v => <option key={v.id} value={v.id}>{v.registrationNumber}</option>)}
          </select>
          {canCreate && (
            <button onClick={() => setLogOpen(true)} className="min-h-[44px] px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 flex items-center gap-2">
              <Plus className="w-4 h-4" /> Log Fill-up
            </button>
          )}
        </div>
      </div>

      {/* Stat cards */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        {statCards.map(stat => {
          const Icon = stat.icon;
          return (
            <div key={stat.label} className="bg-white rounded-xl border border-gray-200 p-4">
              <div className={`w-10 h-10 ${stat.color} bg-opacity-10 rounded-lg flex items-center justify-center mb-2`}>
                <Icon className={`w-5 h-5 ${stat.color.replace('bg-', 'text-')}`} />
              </div>
              <p className="text-xl font-bold text-gray-900 truncate">{stat.value}</p>
              <p className="text-xs text-gray-500 mt-0.5">{stat.label}</p>
              <p className="text-[10px] text-gray-400 mt-0.5">{stat.sub}</p>
            </div>
          );
        })}
      </div>

      {/* Trend chart */}
      <div className="bg-white rounded-xl border border-gray-200 p-5">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-sm font-semibold text-gray-900">Consumption Trend</h3>
          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-blue-100 text-blue-700">daily aggregates</span>
        </div>
        {trend.length === 0 ? (
          <div className="h-56 flex items-center justify-center text-gray-400 text-sm">
            No fuel transactions in this window — log a fill-up to see the trend.
          </div>
        ) : (
          <ResponsiveContainer width="100%" height={240}>
            <LineChart data={trend}>
              <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
              <XAxis dataKey="day" tick={{ fontSize: 10 }} minTickGap={30} />
              <YAxis yAxisId="l" tick={{ fontSize: 10 }} />
              <YAxis yAxisId="e" orientation="right" tick={{ fontSize: 10 }} />
              <Tooltip />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Line yAxisId="l" type="monotone" dataKey="liters" name="Liters" stroke="#3b82f6" strokeWidth={2} dot={false} />
              <Line yAxisId="l" type="monotone" dataKey="spend" name="Spend ($)" stroke="#10b981" strokeWidth={2} dot={false} />
              <Line yAxisId="e" type="monotone" dataKey="efficiencyKmPerLiter" name="Efficiency (km/L)" stroke="#f59e0b" strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>

      {/* Transactions */}
      <div className="bg-white rounded-xl border border-gray-200">
        <div className="px-5 py-4 border-b border-gray-100 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-gray-900 flex items-center gap-2"><Fuel className="w-4 h-4 text-blue-500" /> Transaction History</h3>
          <span className="text-xs text-gray-400">{records.length} shown</span>
        </div>

        {/* Desktop table */}
        <div className="hidden md:block overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-gray-500 border-b border-gray-100 uppercase tracking-wide">
                <th className="px-5 py-3">Date</th>
                <th className="px-3 py-3">Vehicle</th>
                <th className="px-3 py-3">Liters</th>
                <th className="px-3 py-3">Cost</th>
                <th className="px-3 py-3">Odometer</th>
                <th className="px-3 py-3">Distance</th>
                <th className="px-3 py-3">Efficiency</th>
                <th className="px-3 py-3">Source</th>
                <th className="px-3 py-3"></th>
              </tr>
            </thead>
            <tbody>
              {records.map(r => (
                <tr key={r.id} className="border-b border-gray-50 hover:bg-gray-50">
                  <td className="px-5 py-3 text-gray-600 whitespace-nowrap">{new Date(r.recordDate).toLocaleDateString()}</td>
                  <td className="px-3 py-3 font-medium text-gray-900">{r.vehicleRegistration || '—'}</td>
                  <td className="px-3 py-3">{fmtNum(r.quantity, 1)} L</td>
                  <td className="px-3 py-3">{fmtMoney(r.totalCost)}</td>
                  <td className="px-3 py-3 text-gray-600">{r.odometerReading != null ? `${fmtNum(r.odometerReading, 0)} km` : '—'}</td>
                  <td className="px-3 py-3 text-gray-600">{r.distanceTraveledKm != null ? `${fmtNum(r.distanceTraveledKm, 0)} km` : '—'}</td>
                  <td className="px-3 py-3">
                    {r.efficiencyKmPerLiter != null ? (
                      <span className="inline-flex items-center gap-1 text-green-700 bg-green-50 px-2 py-0.5 rounded-full text-xs font-medium">
                        {fmtNum(r.efficiencyKmPerLiter, 2)} km/L
                      </span>
                    ) : <span className="text-gray-300">—</span>}
                    {r.isAnomaly && (
                      <span className="ml-1 inline-flex items-center gap-1 text-red-600 bg-red-50 px-2 py-0.5 rounded-full text-xs font-medium">
                        <AlertTriangle className="w-3 h-3" /> flagged
                      </span>
                    )}
                  </td>
                  <td className="px-3 py-3">
                    <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${r.source === 'sensor_derived' ? 'bg-indigo-100 text-indigo-700' : 'bg-gray-100 text-gray-600'}`}>
                      {r.source === 'sensor_derived' ? 'Sensor' : 'Manual'}
                    </span>
                  </td>
                  <td className="px-3 py-3">
                    {canDelete && (
                      <button onClick={() => deleteRecord(r.id)} aria-label="Delete record" className="p-2 hover:bg-red-50 rounded-lg text-gray-400 hover:text-red-600 min-w-[44px] min-h-[44px]">
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {records.length === 0 && !loading && (
                <tr><td colSpan={9} className="px-5 py-10 text-center text-gray-400">No transactions yet</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {/* Mobile cards */}
        <ResponsiveCards
          items={records}
          loading={loading}
          empty="No transactions yet — log a fill-up to start tracking"
          keyOf={r => r.id}
          title={r => r.vehicleRegistration || 'Unknown vehicle'}
          subtitle={r => `${new Date(r.recordDate).toLocaleDateString()} · ${r.station || '—'}`}
          statusChip={r => r.isAnomaly ? <span className="px-2 py-0.5 bg-red-100 text-red-700 text-xs rounded-full">anomaly</span> : undefined}
          primary={r => [
            { label: 'Liters', value: `${fmtNum(r.quantity, 1)} L` },
            { label: 'Cost', value: fmtMoney(r.totalCost) },
            { label: 'Efficiency', value: r.efficiencyKmPerLiter != null ? `${fmtNum(r.efficiencyKmPerLiter, 2)} km/L` : '—' },
            { label: 'Odometer', value: r.odometerReading != null ? `${fmtNum(r.odometerReading, 0)} km` : '—' },
          ]}
          details={r => (
            <div className="grid grid-cols-2 gap-3 pt-2">
              <DetailField label="Source" value={r.source === 'sensor_derived' ? 'Sensor-derived' : 'Manual'} />
              <DetailField label="Distance" value={r.distanceTraveledKm != null ? `${fmtNum(r.distanceTraveledKm, 0)} km` : '—'} />
              <DetailField label="Price / L" value={fmtMoney(r.pricePerUnit)} />
              <DetailField label="Fuel Type" value={FUEL_TYPE[r.fuelType] || '—'} />
              {r.anomalyReason && <div className="col-span-2"><DetailField label="Flag" value={r.anomalyReason} /></div>}
              {canDelete && <div className="col-span-2 pt-1"><button onClick={() => deleteRecord(r.id)} className="text-sm text-red-600 font-medium">Delete record</button></div>}
            </div>
          )}
        />
      </div>

      {logOpen && (
        <LogFillUpModal vehicles={vehicles} onClose={() => setLogOpen(false)} onSaved={() => { setLogOpen(false); fetchAll(); }} />
      )}
    </div>
  );
}

function LogFillUpModal({ vehicles, onClose, onSaved }: { vehicles: VehicleOpt[]; onClose: () => void; onSaved: () => void }) {
  const [vehicleId, setVehicleId] = useState('');
  const [quantity, setQuantity] = useState('');
  const [pricePerUnit, setPricePerUnit] = useState('');
  const [totalCost, setTotalCost] = useState('');
  const [odometer, setOdometer] = useState('');
  const [station, setStation] = useState('');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const submit = async () => {
    if (!vehicleId || !quantity || parseFloat(quantity) <= 0) { setError('Vehicle and liters are required'); return; }
    setSaving(true); setError('');
    try {
      await api.post('/fuel', {
        vehicleId, quantity: parseFloat(quantity),
        pricePerUnit: pricePerUnit ? parseFloat(pricePerUnit) : null,
        totalCost: totalCost ? parseFloat(totalCost) : null,
        odometerReading: odometer ? parseFloat(odometer) : null,
        station: station || null, notes: notes || null,
      });
      onSaved();
    } catch (err: any) {
      setError(err.response?.data?.message || 'Failed to log fill-up');
      setSaving(false);
    }
  };

  return (
    <ModalSheet onClose={onClose}>
      <ModalHeader title="Log Fuel Fill-up" onClose={onClose} />
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
            <label className={LABEL}>Liters *</label>
            <input type="number" step="0.1" value={quantity} onChange={e => setQuantity(e.target.value)} className={INPUT} placeholder="e.g. 45.5" />
          </div>
          <div>
            <label className={LABEL}>Odometer (km)</label>
            <input type="number" value={odometer} onChange={e => setOdometer(e.target.value)} className={INPUT} placeholder="e.g. 45230" />
          </div>
          <div>
            <label className={LABEL}>Price per liter</label>
            <input type="number" step="0.01" value={pricePerUnit} onChange={e => setPricePerUnit(e.target.value)} className={INPUT} placeholder="e.g. 1.45" />
          </div>
          <div>
            <label className={LABEL}>Total cost</label>
            <input type="number" step="0.01" value={totalCost} onChange={e => setTotalCost(e.target.value)} className={INPUT} placeholder="e.g. 65.98" />
          </div>
        </div>
        <div>
          <label className={LABEL}>Station / location</label>
          <input value={station} onChange={e => setStation(e.target.value)} className={INPUT} placeholder="e.g. Shell — Riverside" />
        </div>
        <div>
          <label className={LABEL}>Notes</label>
          <textarea value={notes} onChange={e => setNotes(e.target.value)} rows={2} className={INPUT} />
        </div>
        <p className="text-xs text-gray-400">Efficiency for this fill-up is computed automatically against the vehicle's previous transaction odometer.</p>
        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
      <ModalFooter>
        <button className={BtnSecondary} onClick={onClose}>Cancel</button>
        <button className={BtnPrimary} onClick={submit} disabled={saving}>{saving ? 'Saving…' : 'Log Fill-up'}</button>
      </ModalFooter>
    </ModalSheet>
  );
}