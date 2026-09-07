import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Bell, CheckCheck, Inbox, X } from 'lucide-react';
import api from '../lib/api';
import { onChannelUp, onNotification, stop } from '../lib/notificationSocket';
import { useAuth } from '../contexts/AuthContext';
import SeverityChip from './SeverityChip';
import { SheetDropdown } from './ui';

interface NotificationItem {
  id: string;
  title: string;
  message: string;
  eventType?: string;
  actionUrl?: string;
  severity: number;
  isRead: boolean;
  createdAt: string;
}

function timeAgo(iso: string): string {
  const s = Math.max(1, Math.floor((Date.now() - new Date(iso).getTime()) / 1000));
  if (s < 60) return `${s}s ago`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ago`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ago`;
  return `${Math.floor(h / 24)}d ago`;
}

export default function NotificationBell() {
  const [open, setOpen] = useState(false);
  const [unread, setUnread] = useState(0);
  const [items, setItems] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const { refreshPermissions } = useAuth();

  const load = async () => {
    try {
      const [countRes, recentRes] = await Promise.all([
        api.get('/notifications/unread-count'),
        api.get('/notifications/recent?limit=10'),
      ]);
      setUnread(countRes.data?.data?.count ?? 0);
      const recent: NotificationItem[] = recentRes.data?.data?.items ?? [];
      setItems(prev => {
        const joined = [...recent, ...prev];
        const seen = new Set<string>();
        return joined.filter(n => (seen.has(n.id) ? false : (seen.add(n.id), true))).slice(0, 10);
      });
      // Live UI refresh: a role permission edit during an active session must
      // reflect in the sidebar/buttons without a logout. Detect the event and
      // re-fetch the effective permission set.
      if (recent.some(n => n.eventType === 'permission.role_updated')) {
        refreshPermissions();
      }
    } catch {
      // silent — bell is best-effort
    }
  };

  // Instant updates: the server pushes "notification.new" over the SignalR
  // channel the moment a row is committed; the badge re-fetches on each push
  // (and on every (re)connect, to catch anything missed while the socket was
  // down). No polling.
  useEffect(() => {
    load();
    const offNew = onNotification(() => load());
    const offUp = onChannelUp(() => load());
    return () => {
      offNew();
      offUp();
      void stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const markReadAndGo = async (n: NotificationItem) => {
    setOpen(false);
    if (!n.isRead) {
      setUnread(u => Math.max(0, u - 1));
      setItems(prev => prev.map(x => (x.id === n.id ? { ...x, isRead: true } : x)));
      try { await api.put(`/notifications/${n.id}/read`); } catch { /* best-effort */ }
    }
    if (n.actionUrl) navigate(n.actionUrl);
  };

  const markAllRead = async () => {
    try {
      await api.put('/notifications/read-all');
      setUnread(0);
      setItems(prev => prev.map(n => ({ ...n, isRead: true })));
    } catch { /* best-effort */ }
  };

  return (
    <SheetDropdown
      open={open}
      onOpenChange={setOpen}
      panelClass="sm:w-96"
      trigger={
        <button
          onClick={() => { setOpen(!open); if (!open) { setLoading(true); load().finally(() => setLoading(false)); } }}
          className="relative p-2 rounded-lg hover:bg-gray-100 transition-colors min-w-[44px] min-h-[44px] flex items-center justify-center"
          aria-label="Notifications"
        >
          <Bell className="w-5 h-5 text-gray-500 hover:text-gray-700" />
          {unread > 0 && (
            <span className="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 rounded-full bg-red-500 text-white text-[10px] font-bold flex items-center justify-center">
              {unread > 99 ? '99+' : unread}
            </span>
          )}
        </button>
      }
      header={
        <div className="flex items-center justify-between px-4 py-3 border-b border-gray-100 shrink-0">
          <span className="text-sm font-semibold text-gray-900">Notifications</span>
          <div className="flex items-center gap-1">
            {unread > 0 && (
              <button onClick={markAllRead} className="flex items-center gap-1 text-xs text-blue-600 hover:text-blue-700 min-h-[44px] px-2">
                <CheckCheck className="w-3.5 h-3.5" /> Mark all read
              </button>
            )}
            <button onClick={() => setOpen(false)} aria-label="Close notifications" className="sm:hidden p-2.5 text-gray-500 min-w-[44px] min-h-[44px]"><X className="w-5 h-5" /></button>
          </div>
        </div>
      }
    >
      <div className="max-h-[70dvh] sm:max-h-[60vh] overflow-y-auto divide-y divide-gray-50">
        {items.length === 0 ? (
          <div className="py-10 text-center text-gray-400">
            <Inbox className="w-8 h-8 mx-auto mb-2 text-gray-300" />
            <p className="text-sm">{loading ? 'Loading…' : 'No notifications'}</p>
          </div>
        ) : items.map(n => (
          <button
            key={n.id}
            onClick={() => markReadAndGo(n)}
            className={`w-full text-left px-4 py-3 hover:bg-gray-50 transition-colors ${n.isRead ? 'opacity-60' : ''}`}
          >
            <div className="flex items-start gap-2.5">
              <SeverityChip severity={n.severity} className="mt-0.5" />
              <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between gap-2">
                  <p className="text-sm font-medium text-gray-900 truncate">{n.title}</p>
                  <span className="text-[11px] text-gray-400 whitespace-nowrap">{timeAgo(n.createdAt)}</span>
                </div>
                <p className="text-xs text-gray-500 mt-0.5 line-clamp-2">{n.message}</p>
              </div>
            </div>
          </button>
        ))}
      </div>
      <div className="border-t border-gray-100 p-2 shrink-0 pb-[max(0.5rem,env(safe-area-inset-bottom))]">
        <button
          onClick={() => { setOpen(false); navigate('/notifications'); }}
          className="w-full px-3 py-2.5 text-center text-sm font-medium text-blue-600 hover:bg-blue-50 rounded-lg transition-colors min-h-[44px]"
        >
          View All
        </button>
      </div>
    </SheetDropdown>
  );
}