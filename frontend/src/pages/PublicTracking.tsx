import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { AlertTriangle, Clock, Loader2, MapPin, Navigation, PackageCheck, ShieldX, Truck } from 'lucide-react';
import { resolveApiUrl } from '../lib/api';
import RouteMapPane from '../components/RouteMapPane';
import PodEvidence, { type PodRecord } from '../components/PodEvidence';

/**
 * Customer Tracking Link — the public, read-only view of ONE trip, keyed by an
 * unguessable share token. No login, no role, no company membership: possession
 * of the token alone. Deliberately minimal — only what a customer needs to
 * know about their shipment: status, position, ETA, waypoints and POD evidence.
 *
 * This page intentionally does NOT use the shared axios client: it must not
 * attach a stale admin JWT, and its 401 interceptor would bounce an
 * unauthenticated viewer to /login. Raw fetch against the API base instead.
 */
export default function PublicTracking() {
  const { token } = useParams<{ token: string }>();
  const [state, setState] = useState<{ loading: boolean; dead?: boolean; data?: PublicTripView }>({ loading: true });

  useEffect(() => {
    if (!token) return;
    let alive = true;
    setState({ loading: true });
    fetch(resolveApiUrl(`/api/v1/public/trips/${encodeURIComponent(token)}`))
      .then(async resp => {
        if (!alive) return;
        if (resp.status === 404) { setState({ loading: false, dead: true }); return; }
        if (!resp.ok) { setState({ loading: false, dead: true }); return; }
        const body = await resp.json();
        if (!alive) return;
        setState({ loading: false, data: body?.data as PublicTripView });
      })
      .catch(() => { if (alive) setState({ loading: false, dead: true }); });
    return () => { alive = false; };
  }, [token]);

  if (state.loading) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center">
        <div className="text-center text-gray-500"><Loader2 className="w-8 h-8 animate-spin mx-auto mb-2" /><p className="text-sm">Loading tracking view…</p></div>
      </div>
    );
  }

  if (state.dead || !state.data) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center px-4">
        <div className="bg-white rounded-xl border border-gray-200 p-8 max-w-md text-center shadow-sm">
          <ShieldX className="w-10 h-10 text-gray-300 mx-auto mb-3" />
          <h1 className="text-lg font-semibold text-gray-900">Tracking link unavailable</h1>
          <p className="text-sm text-gray-500 mt-2">This tracking link is no longer available. It may have expired, been revoked, or never existed.</p>
          <p className="text-xs text-gray-400 mt-4">Please contact the sender for an updated link.</p>
        </div>
      </div>
    );
  }

  const d = state.data;
  const statusChip = {
    'Preparing': 'bg-gray-100 text-gray-700',
    'Scheduled': 'bg-blue-100 text-blue-700',
    'In Transit': 'bg-green-100 text-green-700',
    'Delayed': 'bg-amber-100 text-amber-700',
    'Delivered': 'bg-purple-100 text-purple-700',
    'Cancelled': 'bg-red-100 text-red-700',
  }[d.statusLabel] ?? 'bg-gray-100 text-gray-700';

  const waypoints = d.waypoints ?? [];
  const routePath = (d.routePath ?? []).map(([lat, lng]) => ({ lat, lng }));
  const nextStop = waypoints.find(w => !w.arrived);

  return (
    <div className="min-h-screen bg-gray-50 pb-12">
      {/* Header */}
      <div className="bg-white border-b border-gray-200">
        <div className="max-w-3xl mx-auto px-4 py-5">
          <div className="flex items-center gap-2 text-[11px] uppercase tracking-wider text-gray-400 font-medium">
            <PackageCheck className="w-3.5 h-3.5" /> Shipment tracking · {d.companyName}
          </div>
          <div className="flex items-center justify-between gap-3 mt-1">
            <h1 className="text-xl font-bold text-gray-900">{d.tripName}</h1>
            <span className={`px-2.5 py-1 rounded-full text-xs font-medium shrink-0 ${statusChip}`}>{d.statusLabel}</span>
          </div>
          {d.isDelayed && d.status !== 3 && (
            <div className="mt-2 flex items-center gap-1.5 text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded-lg px-3 py-1.5">
              <AlertTriangle className="w-3.5 h-3.5" /> This shipment is running behind schedule.
            </div>
          )}
        </div>
      </div>

      <div className="max-w-3xl mx-auto px-4 mt-5 space-y-4">
        {/* Position + ETA */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="bg-white rounded-xl border border-gray-200 p-4 sm:col-span-1">
            <p className="text-[11px] uppercase tracking-wider text-gray-400 font-medium flex items-center gap-1"><MapPin className="w-3.5 h-3.5" /> Live position</p>
            {d.vehiclePosition?.latitude != null && d.vehiclePosition.longitude != null ? (
              <>
                <p className="text-lg font-bold text-gray-900 mt-1">
                  {d.vehiclePosition.speedKmh != null ? `${d.vehiclePosition.speedKmh.toFixed(0)} km/h` : 'Moving'}
                </p>
                <p className="text-[11px] text-gray-500 font-mono mt-0.5">
                  {d.vehiclePosition.latitude.toFixed(4)}, {d.vehiclePosition.longitude.toFixed(4)}
                </p>
                <p className="text-[10px] text-gray-400 mt-1">
                  {d.vehiclePosition.updatedAt ? `Updated ${new Date(d.vehiclePosition.updatedAt).toLocaleString()}` : ''}
                </p>
              </>
            ) : (
              <p className="text-sm text-gray-400 mt-1">No live position yet.</p>
            )}
          </div>
          <div className="bg-white rounded-xl border border-gray-200 p-4 sm:col-span-1">
            <p className="text-[11px] uppercase tracking-wider text-gray-400 font-medium flex items-center gap-1"><Clock className="w-3.5 h-3.5" /> ETA to next stop</p>
            <p className="text-lg font-bold text-gray-900 mt-1">
              {d.etaMinutesToNextStop != null ? `~${d.etaMinutesToNextStop} min` : '—'}
            </p>
            <p className="text-[11px] text-gray-500 mt-0.5">{nextStop?.name ?? 'Final destination'}</p>
          </div>
          <div className="bg-white rounded-xl border border-gray-200 p-4 sm:col-span-1">
            <p className="text-[11px] uppercase tracking-wider text-gray-400 font-medium flex items-center gap-1"><Truck className="w-3.5 h-3.5" /> Expected delivery</p>
            <p className="text-lg font-bold text-gray-900 mt-1">
              {d.expectedFinalArrivalUtc ? new Date(d.expectedFinalArrivalUtc).toLocaleString(undefined, { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—'}
            </p>
            <p className="text-[11px] text-gray-500 mt-0.5">Final stop arrival</p>
          </div>
        </div>

        {/* Map */}
        {routePath.length >= 2 ? (
          <RouteMapPane waypoints={routePath} routeGeometry={null} fences={[]} />
        ) : (
          <div className="bg-white rounded-xl border border-gray-200 p-4 text-sm text-gray-400">Route preview unavailable.</div>
        )}

        {/* Waypoints */}
        <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100 flex items-center gap-1.5">
            <Navigation className="w-4 h-4 text-gray-400" />
            <h2 className="text-sm font-semibold text-gray-900">Stops</h2>
          </div>
          <ul className="divide-y divide-gray-100">
            {waypoints.map(w => (
              <li key={w.sequenceOrder} className="px-4 py-3">
                <div className="flex items-center gap-3">
                  <span className={`w-6 h-6 rounded-full text-[10px] font-bold flex items-center justify-center shrink-0 ${w.arrived ? 'bg-green-100 text-green-700' : 'bg-blue-100 text-blue-700'}`}>
                    {w.sequenceOrder}
                  </span>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-gray-900 truncate">{w.name}</p>
                    <p className="text-[11px] text-gray-500">
                      {w.arrived ? 'Arrived' : 'Not yet arrived'}
                      {w.expectedArrivalUtc ? ` · expected ${new Date(w.expectedArrivalUtc).toLocaleString(undefined, { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}` : ''}
                    </p>
                  </div>
                  {w.arrived && <span className="text-[10px] font-medium text-green-700 bg-green-100 px-1.5 py-0.5 rounded-full shrink-0">✓ Arrived</span>}
                </div>
                {w.podEvidence && w.podEvidence.length > 0 && (
                  <div className="mt-2 ml-9">
                    <PodEvidence records={w.podEvidence.map(p => ({ ...p, waypointName: w.name }))}
                      compact imageSrcResolver={url => resolveApiUrl(url)} />
                  </div>
                )}
              </li>
            ))}
            {waypoints.length === 0 && <li className="px-4 py-3 text-sm text-gray-400">No stops on this shipment.</li>}
          </ul>
        </div>
      </div>
    </div>
  );
}

interface PublicTripView {
  status: number;
  statusLabel: string;
  isDelayed: boolean;
  tripName: string;
  companyName: string;
  vehiclePosition?: { latitude?: number | null; longitude?: number | null; speedKmh?: number | null; headingDeg?: number | null; updatedAt?: string | null } | null;
  waypoints: { name: string; sequenceOrder: number; latitude: number; longitude: number; arrived: boolean; expectedArrivalUtc?: string | null; podEvidence?: PodRecord[] }[];
  etaMinutesToNextStop?: number | null;
  expectedFinalArrivalUtc?: string | null;
  routePath?: number[][];
}