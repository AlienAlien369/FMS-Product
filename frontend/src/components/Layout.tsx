import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import {
  LayoutDashboard, Truck, Users, Map, Route, Bell,
  Fuel, Wrench, FileText, Settings, LogOut, ChevronLeft,
  Building2, Shield, Package, Globe, Menu, UserCog, Crown, CreditCard, Navigation
} from 'lucide-react';
import { useState, useMemo } from 'react';
import { Search } from 'lucide-react';

const navItems = [
  { path: '/', label: 'Dashboard', icon: LayoutDashboard },
  { path: '/admin/companies', label: 'Platform Admin', icon: Crown, adminOnly: true },
  { path: '/companies', label: 'Companies', icon: Building2, adminOnly: true },
  { path: '/vehicles', label: 'Vehicles', icon: Truck },
  { path: '/drivers', label: 'Drivers', icon: Users },
  { path: '/trips', label: 'Trips', icon: Route },
  { path: '/geofences', label: 'Geofences', icon: Map },
  { path: '/routes', label: 'Routes', icon: Navigation },
  { path: '/fuel', label: 'Fuel', icon: Fuel },
  { path: '/maintenance', label: 'Maintenance', icon: Wrench },
  { path: '/alerts', label: 'Alerts', icon: Bell },
  { path: '/documents', label: 'Documents', icon: FileText },
  { path: '/users', label: 'Users', icon: UserCog },
  { path: '/roles', label: 'Roles & Permissions', icon: Shield },
  { path: '/packages', label: 'Packages', icon: CreditCard, adminOnly: true },
  { path: '/modules', label: 'Modules', icon: Package },
  { path: '/localization', label: 'Localization', icon: Globe },
  { path: '/settings', label: 'Settings', icon: Settings },
];

export default function Layout() {
  const { user, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');

  const filteredNavItems = useMemo(() => {
    const items = navItems.filter(i => {
      if (i.adminOnly && !user?.roles?.includes('SuperAdmin')) return false;
      return true;
    });
    if (!searchQuery.trim()) return items;
    return items.filter(i => i.label.toLowerCase().includes(searchQuery.toLowerCase()));
  }, [user?.roles, searchQuery]);

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  const Sidebar = () => (
    <aside className={`bg-gray-900 text-white h-full flex flex-col transition-all duration-300 ${collapsed ? 'w-16' : 'w-64'} ${mobileOpen ? 'fixed inset-0 z-50 w-64' : 'hidden lg:flex'}`}>
      <div className="p-4 flex items-center justify-between border-b border-gray-700">
        {!collapsed && <span className="font-bold text-lg tracking-tight">Freebuff</span>}
        <button onClick={() => setCollapsed(!collapsed)} className="hidden lg:block p-1 hover:bg-gray-700 rounded">
          <ChevronLeft className={`w-5 h-5 transition-transform ${collapsed ? 'rotate-180' : ''}`} />
        </button>
      </div>
      {!collapsed && (
        <div className="px-3 py-2">
          <div className="relative">
            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-500" />
            <input type="text" value={searchQuery} onChange={e => setSearchQuery(e.target.value)}
              placeholder="Search..."
              className="w-full pl-8 pr-3 py-1.5 bg-gray-800 border border-gray-700 rounded-md text-xs text-gray-300 placeholder-gray-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </div>
        </div>
      )}
      <nav className="flex-1 overflow-y-auto py-2">
        {filteredNavItems.map(item => {
          const Icon = item.icon;
          const active = location.pathname === item.path;
          return (
            <Link key={item.path} to={item.path} onClick={() => setMobileOpen(false)}
              className={`flex items-center gap-3 px-4 py-2.5 mx-2 rounded-lg text-sm transition-colors ${active ? 'bg-blue-600 text-white' : 'text-gray-300 hover:bg-gray-800 hover:text-white'}`}>
              <Icon className="w-5 h-5 flex-shrink-0" />
              {!collapsed && <span>{item.label}</span>}
            </Link>
          );
        })}
      </nav>
      <div className="p-3 border-t border-gray-700">
        {!collapsed && (
          <div className="text-xs text-gray-400 mb-2 truncate">{user?.companyName}</div>
        )}
        <button onClick={handleLogout} className="flex items-center gap-3 px-4 py-2 w-full rounded-lg text-sm text-gray-300 hover:bg-gray-800 hover:text-white transition-colors">
          <LogOut className="w-5 h-5" />
          {!collapsed && <span>Logout</span>}
        </button>
      </div>
    </aside>
  );

  return (
    <div className="flex h-screen bg-gray-50">
      <Sidebar />
      {mobileOpen && <div className="fixed inset-0 bg-black/50 z-40 lg:hidden" onClick={() => setMobileOpen(false)} />}
      <div className="flex-1 flex flex-col overflow-hidden">
        <header className="bg-white border-b border-gray-200 px-4 py-3 flex items-center justify-between lg:px-6">
          <div className="flex items-center gap-3">
            <button onClick={() => setMobileOpen(true)} className="lg:hidden p-1 hover:bg-gray-100 rounded">
              <Menu className="w-5 h-5" />
            </button>
            <h1 className="text-lg font-semibold text-gray-800">{navItems.find(i => i.path === location.pathname)?.label || 'Freebuff'}</h1>
          </div>
          <div className="flex items-center gap-3">
            <Bell className="w-5 h-5 text-gray-500 hover:text-gray-700 cursor-pointer" />
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 bg-blue-600 rounded-full flex items-center justify-center text-white text-sm font-medium">
                {user?.firstName?.[0]}{user?.lastName?.[0]}
              </div>
              <span className="text-sm font-medium text-gray-700 hidden sm:inline">{user?.firstName} {user?.lastName}</span>
            </div>
          </div>
        </header>
        <main className="flex-1 overflow-y-auto p-4 lg:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
