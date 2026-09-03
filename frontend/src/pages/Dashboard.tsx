import { useEffect, useState } from 'react';
import api from '../lib/api';
import { useAuth } from '../contexts/AuthContext';
import { Truck, Users, Route, Map, Zap, Wrench, Clock } from 'lucide-react';
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
  const [stats, setStats] = useState<Stats | null>(null);
  const [vehicleStatus, setVehicleStatus] = useState<{ Status: string; Count: number }[]>([]);
  const [fuelTypes, setFuelTypes] = useState<{ FuelType: string; Count: number }[]>([]);
  const [driverStatus, setDriverStatus] = useState<{ Status: string; Count: number }[]>([]);
  const [topDrivers, setTopDrivers] = useState<{ Name: string; SafetyScore: number; BehaviourScore: number }[]>([]);
  const [recentVehicles, setRecentVehicles] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchAll = async () => {
      try {
        const [s, vs, ft, ds, td, rv] = await Promise.all([
          api.get('/dashboard/stats'),
          api.get('/dashboard/vehicles/by-status'),
          api.get('/dashboard/vehicles/by-fuel-type'),
          api.get('/dashboard/drivers/by-status'),
          api.get('/dashboard/drivers/top-safety'),
          api.get('/dashboard/vehicles/recent'),
        ]);
        setStats(s.data.data);
        // API returns camelCase rows; normalize to the shape the render code consumes.
        setVehicleStatus((vs.data.data || []).map((x: any) => ({ Status: x.status, Count: x.count })));
        setFuelTypes((ft.data.data || []).map((x: any) => ({ FuelType: x.fuelType, Count: x.count })));
        setDriverStatus((ds.data.data || []).map((x: any) => ({ Status: x.status, Count: x.count })));
        setTopDrivers((td.data.data || []).map((x: any) => ({ Name: x.name, SafetyScore: x.safetyScore, BehaviourScore: x.behaviourScore })));
        setRecentVehicles(rv.data.data || []);
      } catch (err) { console.error(err); }
      setLoading(false);
    };
    fetchAll();
  }, []);

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
        {/* Top Safety Drivers - Radar */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-4">Driver Safety Scores</h3>
          {topDrivers.length === 0 ? (
            <div className="h-64 flex items-center justify-center text-gray-400 text-sm">No driver scores available</div>
          ) : (
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={topDrivers} layout="vertical" margin={{ left: 20 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
                <XAxis type="number" domain={[0, 100]} tick={{ fontSize: 10 }} />
                <YAxis dataKey="Name" type="category" tick={{ fontSize: 10 }} width={80} />
                <Tooltip />
                <Legend />
                <Bar dataKey="SafetyScore" fill="#3b82f6" name="Safety" radius={[0, 4, 4, 0]} />
                <Bar dataKey="BehaviourScore" fill="#10b981" name="Behaviour" radius={[0, 4, 4, 0]} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </div>

        {/* Recent Vehicle Activity */}
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-sm font-semibold text-gray-900 mb-4">Recent Vehicle Activity</h3>
          {recentVehicles.length === 0 ? (
            <div className="h-64 flex items-center justify-center text-gray-400 text-sm">No vehicle activity</div>
          ) : (
            <div className="space-y-2 max-h-[280px] overflow-y-auto">
              {recentVehicles.map((v: any) => (
                <div key={v.id} className="flex items-center gap-3 p-2.5 bg-gray-50 rounded-lg">
                  <div className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${v.status === 'Active' ? 'bg-green-500' : v.status === 'InMaintenance' ? 'bg-yellow-500' : 'bg-gray-400'}`} />
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium text-gray-900 truncate">{v.registrationNumber} — {v.name}</div>
                    <div className="text-xs text-gray-500">
                      {v.make} {v.model} {v.driverName ? `• Driver: ${v.driverName}` : ''}
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
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
