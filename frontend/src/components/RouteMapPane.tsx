import { useEffect, useRef, useState } from 'react';
import { Loader2, MapPinOff } from 'lucide-react';
import { loadGoogleMaps, googleMapsConfigured } from '../lib/googleMaps';
import { parseShape } from '../lib/geofenceGeometry';

/**
 * Route path preview/editor overlay.
 *
 * Without a Google Maps key this renders an honest setup state plus the same
 * summary the modal already shows — click-to-place waypoints and Directions
 * road snapping genuinely require the Maps JavaScript + Directions + Drawing
 * libraries, which cannot run without VITE_GOOGLE_MAPS_API_KEY.
 *
 * With a key the pane renders the waypoint polyline and every linked geofence
 * color-coded by its role (checkpoint = green, restricted = red, start/end =
 * blue). RouteGeometry (a GeoJSON LineString produced by Directions) is drawn
 * when present; otherwise straight segments between waypoints stand in.
 */

interface LinkedFence {
  geofenceId: string;
  geofenceName: string;
  geometry?: string;      // canonical GeoJSON (circle/polygon)
  centerLatitude?: number; centerLongitude?: number; radius?: number;
  role: number;
}

interface Waypoint { lat: number; lng: number; }

interface Props {
  waypoints: Waypoint[];
  routeGeometry?: string | null; // GeoJSON LineString
  fences: LinkedFence[];
}

const ROLE_STYLE: Record<number, { stroke: string; fill: string; label: string }> = {
  0: { stroke: '#2E7D32', fill: '#4CAF5033', label: 'checkpoint' },
  1: { stroke: '#C62828', fill: '#F4433633', label: 'restricted' },
  2: { stroke: '#1565C0', fill: '#2196F333', label: 'start' },
  3: { stroke: '#283593', fill: '#3F51B533', label: 'end' },
};

export default function RouteMapPane({ waypoints, routeGeometry, fences }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState<'nokey' | 'loading' | 'ready' | 'error'>(googleMapsConfigured() ? 'loading' : 'nokey');
  const builtRef = useRef(false);

  useEffect(() => {
    if (!googleMapsConfigured()) return;
    let cancelled = false;
    loadGoogleMaps([])
      .then(() => { if (!cancelled) setStatus('ready'); })
      .catch((e: Error) => { if (!cancelled) { setStatus('error'); } });
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (status !== 'ready' || builtRef.current || !containerRef.current || !window.google) return;
    builtRef.current = true;
    const google = window.google;
    const map = new google.maps.Map(containerRef.current, {
      center: { lat: 23.0, lng: 72.0 },
      zoom: 6,
      mapTypeControl: false,
      streetViewControl: false,
    });

    // Path: prefer the stored LineString geometry, else straight waypoint segments.
    let pathPts: { lat: number; lng: number }[] = [];
    if (routeGeometry) {
      try {
        const geom = JSON.parse(routeGeometry);
        if (geom?.type === 'LineString' && Array.isArray(geom.coordinates)) {
          pathPts = geom.coordinates.map((c: number[]) => ({ lat: c[1], lng: c[0] }));
        }
      } catch { pathPts = []; }
    }
    if (pathPts.length < 2) pathPts = waypoints.filter(w => isFinite(w.lat) && isFinite(w.lng));
    if (pathPts.length >= 2) {
      new google.maps.Polyline({
        map,
        path: pathPts,
        geodesic: true,
        strokeColor: '#1D4ED8',
        strokeOpacity: 0.9,
        strokeWeight: 4,
      });
    }
    // Origin/destination markers when no polyline exists yet.
    if (pathPts.length === 0 && waypoints.length >= 2) {
      const bounds = new google.maps.LatLngBounds();
      waypoints.filter(w => isFinite(w.lat) && isFinite(w.lng)).forEach(w => bounds.extend({ lat: w.lat, lng: w.lng }));
      if (!bounds.isEmpty()) map.fitBounds(bounds, { top: 40, left: 40, right: 40, bottom: 40 });
    }

    // Linked geofence overlays, color-coded by role.
    for (const f of fences) {
      const style = ROLE_STYLE[f.role] ?? ROLE_STYLE[0];
      const shape = parseShape(f.geometry);
      if (shape?.type === 'circle') {
        new google.maps.Circle({
          map,
          center: { lat: shape.center[1], lng: shape.center[0] },
          radius: shape.radiusMeters,
          strokeColor: style.stroke, strokeOpacity: 0.9, strokeWeight: 2,
          fillColor: style.fill, fillOpacity: 0.25,
        });
      } else if (shape?.type === 'polygon') {
        new google.maps.Polygon({
          map,
          paths: shape.coordinates.map(([lng, lat]) => ({ lat, lng })),
          strokeColor: style.stroke, strokeOpacity: 0.9, strokeWeight: 2,
          fillColor: style.fill, fillOpacity: 0.25,
        });
      } else if (f.centerLatitude != null && f.centerLongitude != null && f.radius != null) {
        new google.maps.Circle({
          map,
          center: { lat: f.centerLatitude, lng: f.centerLongitude },
          radius: f.radius,
          strokeColor: style.stroke, strokeOpacity: 0.9, strokeWeight: 2,
          fillColor: style.fill, fillOpacity: 0.25,
        });
      }
    }
  }, [status, waypoints, routeGeometry, fences]);

  return (
    <div className="relative rounded-lg overflow-hidden border border-gray-200" style={{ height: 300 }}>
      {status === 'ready' ? <div ref={containerRef} className="w-full h-full" />
        : (
          <div className="h-full flex flex-col items-center justify-center text-center px-6">
            {status === 'loading' ? <Loader2 className="w-6 h-6 animate-spin text-gray-400" />
              : <MapPinOff className="w-8 h-8 text-gray-300 mb-2" />}
            <p className="text-xs font-medium text-gray-600 mt-1">
              {status === 'nokey'
                ? 'Map preview needs a VITE_GOOGLE_MAPS_API_KEY (Maps JavaScript + Directions + Drawing libraries).'
                : status === 'error' ? 'The map could not be loaded.' : 'Loading map…'}
            </p>
            {status === 'nokey' && waypoints.length > 0 && (
              <p className="text-[11px] text-gray-400 mt-1">
                {waypoints.length} path point{waypoints.length === 1 ? '' : 's'} ·{' '}
                {fences.length} linked geofence{fences.length === 1 ? '' : 's'} stored — the route still saves and validates without the map.
              </p>
            )}
          </div>
        )}
    </div>
  );
}
