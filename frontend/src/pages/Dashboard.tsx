import { useAuth } from '../contexts/AuthContext';
import { Truck, Users, Map, Route, Bell, AlertTriangle } from 'lucide-react';

const stats = [
  { label: 'Total Vehicles', value: '—', icon: Truck, color: 'bg-blue-500' },
  { label: 'Active Drivers', value: '—', icon: Users, color: 'bg-green-500' },
  { label: 'Active Trips', value: '—', icon: Route, color: 'bg-purple-500' },
  { label: 'Geofences', value: '—', icon: Map, color: 'bg-cyan-500' },
  { label: 'Active Alerts', value: '—', icon: Bell, color: 'bg-orange-500' },
  { label: 'Pending Maintenance', value: '—', icon: AlertTriangle, color: 'bg-red-500' },
];

export default function Dashboard() {
  const { user } = useAuth();

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">Welcome back, {user?.firstName}</h2>
          <p className="text-gray-500 text-sm mt-1">Here's an overview of your fleet</p>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        {stats.map(stat => {
          const Icon = stat.icon;
          return (
            <div key={stat.label} className="bg-white rounded-xl border border-gray-200 p-5 hover:shadow-md transition-shadow">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm text-gray-500">{stat.label}</p>
                  <p className="text-2xl font-bold text-gray-900 mt-1">{stat.value}</p>
                </div>
                <div className={`w-12 h-12 ${stat.color} bg-opacity-10 rounded-xl flex items-center justify-center`}>
                  <Icon className={`w-6 h-6 ${stat.color.replace('bg-', 'text-')}`} />
                </div>
              </div>
            </div>
          );
        })}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="bg-white rounded-xl border border-gray-200 p-6">
          <h3 className="font-semibold text-gray-900 mb-4">Fleet Overview</h3>
          <div className="h-64 flex items-center justify-center text-gray-400 border-2 border-dashed border-gray-200 rounded-lg">
            <p>Dashboard widgets will appear here</p>
          </div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-6">
          <h3 className="font-semibold text-gray-900 mb-4">Recent Activity</h3>
          <div className="h-64 flex items-center justify-center text-gray-400 border-2 border-dashed border-gray-200 rounded-lg">
            <p>Activity feed will appear here</p>
          </div>
        </div>
      </div>
    </div>
  );
}
