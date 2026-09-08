import { useEffect, useState } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import { useCompanyScope } from '../contexts/CompanyScopeContext';
import { Truck, Users, Route, Map, Zap, Wrench, Clock, Gauge, CircleDot } from 'lucide-react';
import { PieChart, Pie, Cell, ResponsiveContainer, BarChart, Bar, XAxis, YAxis, Tooltip, CartesianGrid, Legend, RadarChart, Radar, PolarGrid, PolarAngleAxis, PolarRadiusAxis } from 'recharts';

const COLORS = ['#3b82f6', '#10b981', '#f59e0b', '#ef4444', '#8b5cf6', '#06b6d4', '#ec4899', '#6366f1'];

interface Stats {
  vehicles: { total: number; active: number; maintenance: number };
  drivers: { total: number; active: number; onTrip: number };
  trips: { total: number; active: number };
  users: number;
  geofences: number;
}

export default function Dashboard() {
  const { user } = useAuth();
  const { version: scopeVersion, scopeLabel, isMultiCompany } = useCompanyScope();
  const [stats, setStats] = useState<Stats | null>(null);
  const [vehicleStatus, setVehicleStatus] = useState<{ Status: string; Count: number }[]>([]);
  const [fuelTypes, setFuelTypes] = useState<{ FuelType: string; Count: number }[]>([]);
  const [driverStatus, setDriverStatus] = useState<{ Status: string; Count: number }[]>([]);
  const [scoreOverview, setScoreOverview] = useState<{ average: number; belowThresholdCount: number; threshold: number; insufficientDataCount: number; distribution: { bucket: string; count: number }[] } | null>(null);
  const [recentVehicles, setRecentVehicles] = useState<any[]>([]);
  const [fleetHealth, setFleetHealth] = useState<{ overSpeedCount: number; tyreAnomalyCount: number; withSpeedPolicy: number; withTyrePolicy: number } | null>(null);
  const [fuelOverview, setFuelOverview] = useState<any>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchAll = async () => {
      try {
        const [s, vs, ft, ds, so, rv, fh, fo] = await Promise.all([
          api.get('/dashboard/stats'),
          api.get('/dashboard/vehicles/by-status'),
          api.get('/dashboard/vehicles/by-fuel-type'),
          api.get('/dashboard/drivers/by-status'),
          api.get('/dashboard/drivers/scorecard-overview'),
          api.get('/dashboard/vehicles/recent'),
          api.get('/dashboard/fleet-health'),
          api.get('/dashboard/fuel-overview'),
        ]);
        setStats(s.data.data);
        // API returns camelCase rows; normalize to the shape the render code consumes.
        setVehicleStatus((vs.data.data || []).map((x: any) => ({ Status: x.status, Count: x.count })));
        setFuelTypes((ft.data.data || []).map((x: any) => ({ FuelType: x.fuelType, Count: x.count })));
        setDriverStatus((ds.data.data || []).map((x: any) => ({ Status: x.status, Count: x.count })));
        setScoreOverview(so.data.data || null);
        setRecentVehicles(rv.data.data || []);
        setFleetHealth(fh.data.data || null);
        setFuelOverview(fo.data.data || null);
      } catch (err) { console.error(err); }
      setLoading(false);
    };
    fetchAll();
  }, [scopeVersion]);

  const statCards = stats ? [
    { label: 'Total Vehicles', value: stats.vehicles.total, sub: `${stats.vehicles.active} active`, icon: Truck, color: 'bg-blue-500' },
    { label: 'Active Drivers', value: stats.drivers.active, sub: `${stats.drivers.onTrip} on trip`, icon: Users, color: 'bg-green-500' },
    { label: 'Active Trips', value: stats.trips.active, sub: `${stats.trips.total} total`, icon: Route, color: 'bg-purple-500' },
    { label: 'Geofences', value: stats.geofences, sub: 'configured', icon: Map, color: 'bg-cyan-500' },
    { label: 'Team Members', value: stats.users, sub: 'registered users', icon: Zap, color: 'bg-orange-500' },
    { label: 'In Maintenance', value: stats.vehicles.maintenance, sub: 'vehicles', icon: Wrench, color: 'bg-red-500' },
  ] : [];

  if (loading) return <div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" /></div>;

  return (
    <div className="space-y-6">
      {/* Welcome */}
      <div>
        <h2 className="text-2xl font-bold text-gray-900">Welcome back, {user?.firstName}</h2>
        <p className="text-gray-500 text-sm mt-1">Here's an overview of your fleet</p>
      </div>

      {/* Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-4">
        {statCards.map(stat => {
          const Icon = stat.icon;
          return (
            <div key={stat.label} className="bg-white rounded-xl border border-gray-200 p-4 hover:shadow-md transition-shadow">
              <div className="flex items-center justify-between mb-2">
                <div className={`w-10 h-10 ${stat.color} bg-opacity-10 rounded-lg flex items-center justify-center`}>
                  <Icon className={`w-5 h-5 ${stat.color.replace('bg-', 'text-')}`} />
                </div>
              </div>
              <p className="text-2xl font-bold text-gray-900">{stat.value}</p>
              <p className="text-xs text-gray-500 mt-0.5">{stat.label}</p>
              <p className="text-xs text-gray-400 mt-0.5">{stat.sub}</p>
            </div>
          );
        })}
      </div>

      {/* Fleet Health (speed policy + tyre pressure) */}
      {fleetHealth && (
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div className="bg-white rounded-xl border border-gray-200 p-5 flex items-center gap-4">
            <div className={`w-12 h-12 rounded-lg flex items-center justify-center ${fleetHealth.overSpeedCount > 0 ? 'bg-red-50' : 'bg-green-50'}`}>
              <Gauge className={`w-6 h-6 ${fleetHealth.overSpeedCount > 0 ? 'text-red-600' : 'text-green-600'}`} />
            </div>
            <div>
              <p className="text-2xl font-bold text-gray-900">{fleetHealth.overSpeedCount}</p>
              <p className="text-xs text-gray-500">vehicles over speed policy</p>
              <p className="text-[10px] text-gray-400 mt-0.5">{fleetHealth.withSpeedPolicy} vehicles have a speed policy configured</p>
            </div>
          </div>
          <div className="bg-white rounded-xl border border-gray-200 p-5 flex items-center gap-4">
            <div className={`w-12 h-12 rounded-lg flex items-center justify-center ${fleetHealth.tyreAnomalyCount > 0 ? 'bg-amber-50' : 'bg-green-50'}`}>
              <CircleDot className={`w-6 h-6 ${fleetHealth.tyreAnomalyCount > 0 ? 'text-amber-600' : 'text-green-600'}`} />
            </div>
            <div>
              <p className="text-2xl font-bold text-gray-900">{fleetHealth.tyreAnomalyCount}</p>
              <p className="text-xs text-gray-500">vehicles with tyre pressure anomaly</p>
              <p className="text-[10px] text-gray-400 mt-0.5">{fleetHealth.withTyrePolicy} vehicles have a tyre policy configured</p>
            </div>
          </div>
        </div>
      )}

      {/* Charts Row 1 */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Vehicle Status Pie */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-4">Vehicle Status</h3>
          {vehicleStatus.length === 0 ? (
            <div className="h-48 flex items-center justify-center text-gray-400 text-sm">No vehicle data</div>
          ) : (
            <ResponsiveContainer width="100%" height={200}>
              <PieChart>
                <Pie data={vehicleStatus.map(d => ({ name: d.Status, value: d.Count }))}
                  cx="50%" cy="50%" innerRadius={45} outerRadius={80} paddingAngle={3} dataKey="value">
                  {vehicleStatus.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          )}
          {vehicleStatus.length > 0 && (
            <div className="flex flex-wrap gap-3 mt-2">
              {vehicleStatus.map((d, i) => (
                <div key={d.Status} className="flex items-center gap-1.5 text-xs text-gray-600">
                  <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: COLORS[i % COLORS.length] }} />
                  {d.Status} ({d.Count})
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Fuel Type Bar */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-4">Vehicles by Fuel Type</h3>
          {fuelTypes.length === 0 ? (
            <div className="h-48 flex items-center justify-center text-gray-400 text-sm">No data</div>
          ) : (
            <ResponsiveContainer width="100%" height={200}>
              <BarChart data={fuelTypes.map(d => ({ name: d.FuelType, count: d.Count }))}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                <XAxis dataKey="name" tick={{ fontSize: 10 }} />
                <YAxis tick={{ fontSize: 10 }} allowDecimals={false} />
                <Tooltip />
                <Bar dataKey="count" radius={[4, 4, 0, 0]}>
                  {fuelTypes.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          )}
        </div>

        {/* Fuel Consumption & Cost (30d) — real fuel transaction data */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-semibold text-gray-900">Fuel Consumption & Cost</h3>
            <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-blue-100 text-blue-700">last 30 days</span>
          </div>
          {!fuelOverview || fuelOverview.transactionCount === 0 ? (
            <div className="h-48 flex items-center justify-center text-gray-400 text-sm">No fuel transactions yet — log fill-ups on the Fuel page</div>
          ) : (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div className="text-center">
                  <div className="text-xl font-bold text-gray-900">${fuelOverview.totalSpend?.toLocaleString(undefined, { maximumFractionDigits: 2 })}</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">Total spend</div>
                </div>
                <div className="text-center">
                  <div className="text-xl font-bold text-gray-900">{fuelOverview.totalLiters?.toLocaleString()} L</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">Fuel consumed</div>
                </div>
                <div className="text-center">
                  <div className={`text-xl font-bold ${fuelOverview.avgEfficiencyKmPerLiter != null ? 'text-gray-900' : 'text-gray-300'}`}>{fuelOverview.avgEfficiencyKmPerLiter ?? '—'}</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">km / liter</div>
                </div>
                <div className="text-center">
                  <div className={`text-xl font-bold ${fuelOverview.costPerKm != null ? 'text-gray-900' : 'text-gray-300'}`}>{fuelOverview.costPerKm != null ? `$${fuelOverview.costPerKm}` : '—'}</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">cost / km</div>
                </div>
              </div>
              {fuelOverview.anomalyCount > 0 && (
                <p className="text-[11px] text-red-600 font-medium">{fuelOverview.anomalyCount} transaction(s) flagged as anomalies</p>
              )}
              {(fuelOverview.topConsumers || []).length > 0 && (
                <div className="space-y-1.5">
                  <div className="text-[10px] text-gray-400 uppercase tracking-wide">Top consumers</div>
                  {(fuelOverview.topConsumers as any[]).map((c: any) => {
                    const max = Math.max(1, ...(fuelOverview.topConsumers as any[]).map((x: any) => x.liters));
                    return (
                      <div key={c.vehicleId} className="flex items-center gap-2">
                        <span className="w-24 truncate text-[11px] text-gray-600">{c.registration}</span>
                        <div className="flex-1 h-2 bg-gray-100 rounded-full overflow-hidden">
                          <div className="h-full rounded-full bg-blue-400" style={{ width: `${(c.liters / max) * 100}%` }} />
                        </div>
                        <span className="w-14 text-right text-[11px] text-gray-600">{c.liters} L</span>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Driver Status */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-4">Driver Availability</h3>
          {driverStatus.length === 0 ? (
            <div className="h-48 flex items-center justify-center text-gray-400 text-sm">No driver data</div>
          ) : (
            <ResponsiveContainer width="100%" height={200}>
              <PieChart>
                <Pie data={driverStatus.map(d => ({ name: d.Status, value: d.Count }))}
                  cx="50%" cy="50%" innerRadius={45} outerRadius={80} paddingAngle={3} dataKey="value">
                  {driverStatus.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          )}
          {driverStatus.length > 0 && (
            <div className="flex flex-wrap gap-3 mt-2">
              {driverStatus.map((d, i) => (
                <div key={d.Status} className="flex items-center gap-1.5 text-xs text-gray-600">
                  <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: COLORS[i % COLORS.length] }} />
                  {d.Status} ({d.Count})
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Charts Row 2 */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Driver Safety Scores — wired to materialized scorecard data */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-semibold text-gray-900">Driver Safety Scores</h3>
            {scoreOverview && (
              <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-100 text-green-700" title="Aggregated from materialized driver scorecards (30-day window)">
                30-day scorecards
              </span>
            )}
          </div>
          {!scoreOverview || (scoreOverview.distribution ?? []).every((d: any) => d.count === 0) ? (
            <div className="h-64 flex items-center justify-center text-gray-400 text-sm">No driver scores available — scores materialize from events + completed trips</div>
          ) : (
            <div className="space-y-4">
              <div className="grid grid-cols-3 gap-3">
                <div className="text-center">
                  <div className="text-2xl font-bold text-gray-900">{scoreOverview.average}</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">Fleet average</div>
                </div>
                <div className="text-center">
                  <div className={`text-2xl font-bold ${scoreOverview.belowThresholdCount > 0 ? 'text-red-500' : 'text-gray-900'}`}>{scoreOverview.belowThresholdCount}</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">Below {scoreOverview.threshold}</div>
                </div>
                <div className="text-center">
                  <div className="text-2xl font-bold text-gray-400">{scoreOverview.insufficientDataCount}</div>
                  <div className="text-[10px] text-gray-500 uppercase tracking-wide">Insufficient data</div>
                </div>
              </div>
              <div className="space-y-2">
                {scoreOverview.distribution.map((b: any) => {
                  const max = Math.max(1, ...scoreOverview.distribution.map((x: any) => x.count));
                  return (
                    <div key={b.bucket} className="flex items-center gap-2">
                      <span className="w-12 text-[10px] text-gray-500">{b.bucket}</span>
                      <div className="flex-1 h-2 bg-gray-100 rounded-full overflow-hidden">
                        <div className={`h-full rounded-full ${b.bucket.split('-')[0] === '0' || b.bucket.startsWith('21') ? 'bg-red-400' : b.bucket.startsWith('41') ? 'bg-amber-400' : 'bg-emerald-400'}`}
                          style={{ width: `${(b.count / max) * 100}%` }} />
                      </div>
                      <span className="w-6 text-xs text-gray-600">{b.count}</span>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>

        {/* Recent Vehicle Activity */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-4">Recent Vehicle Activity</h3>
          {recentVehicles.length === 0 ? (
            <div className="h-64 flex items-center justify-center text-gray-400 text-sm">No vehicle activity</div>
          ) : (
            <div className="space-y-2 max-h-[280px] overflow-y-auto">
              {recentVehicles.map((v: any) => {
                const name = (v.name || '').trim();
                const makeModel = [v.make, v.model].filter(Boolean).join(' ').trim();
                // Avoid duplicating the vehicle name (name often already contains make + model)
                let title = name || makeModel || '—';
                let sub = '';
                if (makeModel && name) {
                  if (makeModel.startsWith(name) || name.startsWith(makeModel)) sub = '';
                  else sub = makeModel;
                } else if (makeModel && !name) {
                  sub = '';
                }
                return (
                <div key={v.id} className="flex items-center gap-3 p-2.5 bg-gray-50 rounded-lg">
                  <div className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${v.status === 'Active' ? 'bg-green-500' : v.status === 'InMaintenance' ? 'bg-yellow-500' : 'bg-gray-400'}`} />
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-gray-900 truncate">{v.registrationNumber} — {title}</div>
                    <div className="text-xs text-gray-500">
                      {isMultiCompany && v.companyName ? <span className="font-medium text-gray-600">{v.companyName} · </span> : null}
                      {sub} {v.driverName ? `• Driver: ${v.driverName}` : ''}
                    </div>
                  </div>
                  <div className="text-right flex-shrink-0">
                    {v.speed != null && (
                      <div className="flex items-center gap-1 text-xs text-gray-500">
                        <Zap className="w-3 h-3" /> {v.speed} km/h
                      </div>
                    )}
                    {v.ignition && <span className="text-xs text-green-600 font-medium">Running</span>}
                    {!v.ignition && <span className="text-xs text-gray-400">Stopped</span>}
                  </div>
                </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
