import { useEffect, useRef, useState } from 'react';
import { Circle, Hexagon, Eraser, Loader2, MapPinOff } from 'lucide-react';
import { loadGoogleMaps, googleMapsConfigured } from '../lib/googleMaps';
import {
  type GeoShape, circleFromLatLng, polygonFromLatLngs, validateShape, shapeSummary,
} from '../lib/geofenceGeometry';

interface Props {
  /** Shape to show when the pane builds (mount-time only — edits flow out via onChange). */
  initialShape: GeoShape | null;
  onChange: (shape: GeoShape | null) => void;
  readOnly?: boolean;
}

type MapStatus = 'nokey' | 'loading' | 'ready' | 'error';

const FILL = '#4CAF50';
const STROKE = '#2E7D32';

export default function GeofenceMapPane({ initialShape, onChange, readOnly }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [status, setStatus] = useState<MapStatus>(googleMapsConfigured() ? 'loading' : 'nokey');
  const [errorMsg, setErrorMsg] = useState('');
  const [summary, setSummary] = useState<string | null>(shapeSummary(initialShape));
  const [liveError, setLiveError] = useState<string | null>(validateShape(initialShape));
  const [tool, setTool] = useState<'circle' | 'polygon' | null>(null);
  const [shapeActive, setShapeActive] = useState(!!initialShape);

  // Refs holding the live map objects (never re-created on React re-render).
  const mapRef = useRef<google.maps.Map | null>(null);
  const dmRef = useRef<any>(null);
  const circleRef = useRef<google.maps.Circle | null>(null);
  const polyRef = useRef<google.maps.Polygon | null>(null);
  const builtRef = useRef(false);
  const initialRef = useRef(initialShape);
  const readonlyRef = useRef(readOnly);
  readonlyRef.current = readOnly;

  useEffect(() => {
    if (!googleMapsConfigured()) return;
    let cancelled = false;
    loadGoogleMaps(['drawing'])
      .then(() => { if (!cancelled) setStatus('ready'); })
      .catch((e: Error) => { if (!cancelled) { setStatus('error'); setErrorMsg(e.message); } });
    return () => { cancelled = true; };
  }, []);

  // ── One-time map + overlay build when the SDK is ready ────────────────────
  useEffect(() => {
    if (status !== 'ready' || builtRef.current || !containerRef.current) return;
    const google = window.google;
    if (!google?.maps) { setStatus('error'); setErrorMsg('Google Maps failed to initialize.'); return; }

    const startShape = initialRef.current;
    let center: google.maps.LatLngLiteral = { lat: 20.5937, lng: 78.9629 };
    let zoom = 5;
    const map = new google.maps.Map(containerRef.current, {
      center,
      zoom,
      mapTypeId: 'roadmap',
      mapTypeControl: false,
      streetViewControl: false,
      fullscreenControl: true,
    });
    mapRef.current = map;

    const syncShape = (shape: GeoShape | null) => {
      const err = shape ? validateShape(shape) : null;
      setLiveError(err);
      setSummary(shape ? shapeSummary(shape) : null);
      setShapeActive(!!shape);
      onChange(shape); // keep the draft so an inline error is actionable
    };

    const wireCircle = (circle: google.maps.Circle, skipZoom = false) => {
      circleRef.current?.setMap(null);
      circleRef.current = circle;
      if (!skipZoom) {
        const b = circle.getBounds();
        if (b) map.fitBounds(b, { top: 40, left: 40, right: 40, bottom: 40 });
        else { map.setCenter(circle.getCenter() ?? map.getCenter() ?? { lat: 0, lng: 0 }); map.setZoom(12); }
      }
      if (readonlyRef.current) return;
      circle.addListener('center_changed', () => {
        const c = circle.getCenter();
        const r = circle.getRadius();
        if (!c) return;
        syncShape(circleFromLatLng(c.lat(), c.lng(), r));
      });
      circle.addListener('radius_changed', () => {
        const c = circle.getCenter();
        if (!c) return;
        const r = circle.getRadius();
        syncShape(circleFromLatLng(c.lat(), c.lng(), r));
      });
    };

    const wirePolygon = (poly: google.maps.Polygon, skipZoom = false) => {
      polyRef.current?.setMap(null);
      polyRef.current = poly;
      if (!skipZoom) {
        const bounds = new google.maps.LatLngBounds();
        poly.getPath().forEach(p => bounds.extend(p));
        map.fitBounds(bounds, { top: 40, left: 40, right: 40, bottom: 40 });
      }
      if (readonlyRef.current) return;
      const syncPoly = () => {
        const shape = polygonFromLatLngs(poly.getPath().getArray().map(p => ({ lat: p.lat(), lng: p.lng() })));
        syncShape(shape);
      };
      // Vertex drags, insertions and deletions all surface here.
      const path = poly.getPath() as any;
      path.addListener('set_at', syncPoly);
      path.addListener('insert_at', syncPoly);
      path.addListener('remove_at', syncPoly);
      poly.addListener('dragend', syncPoly);
    };

    // Existing shape → editable overlay; new drawing → DrawingManager.
    if (startShape) {
      if (startShape.type === 'circle') {
        const c = new google.maps.Circle({
          map,
          center: { lat: startShape.center[1], lng: startShape.center[0] },
          radius: startShape.radiusMeters,
          editable: !readonlyRef.current,
          draggable: !readonlyRef.current,
          fillColor: FILL, fillOpacity: 0.2, strokeColor: STROKE, strokeWeight: 2,
        });
        wireCircle(c);
      } else {
        const p = new google.maps.Polygon({
          map,
          paths: startShape.coordinates.map(([lng, lat]) => ({ lat, lng })),
          editable: !readonlyRef.current,
          draggable: !readonlyRef.current,
          fillColor: FILL, fillOpacity: 0.2, strokeColor: STROKE, strokeWeight: 2,
        });
        wirePolygon(p);
      }
      builtRef.current = true;
      return;
    }

    // Fresh draw surface (create flow). The @types definition of
    // DrawingManager predates its methods — construct via the untyped surface.
    const DrawingManagerCtor = (google.maps.drawing as any).DrawingManager;
    const dm: any = new DrawingManagerCtor({
      drawingMode: null,
      drawingControl: false, // we render our own styled toolbar
      circleOptions: { fillColor: FILL, fillOpacity: 0.2, strokeColor: STROKE, strokeWeight: 2, zIndex: 1 },
      polygonOptions: { fillColor: FILL, fillOpacity: 0.2, strokeColor: STROKE, strokeWeight: 2, zIndex: 1 },
    });
    dm.setMap(map);
    dmRef.current = dm;

    google.maps.event.addListener(dm, 'overlaycomplete', (e: any) => {
      dm.setDrawingMode(null);
      setTool(null);
      if (e.type === 'circle') wireCircle(e.overlay as google.maps.Circle);
      else if (e.type === 'polygon') wirePolygon(e.overlay as google.maps.Polygon);
      setShapeActive(true);
    });

    builtRef.current = true;
  }, [status, onChange]);

  // ── Toolbar actions ───────────────────────────────────────────────────────
  const pickTool = (t: 'circle' | 'polygon') => {
    if (!dmRef.current || shapeActive) return;
    // Switching tool clears any half-drawn state — one shape at a time.
    setTool(cur => {
      const next = cur === t ? null : t;
      dmRef.current.setDrawingMode(next === null ? null : next);
      return next;
    });
  };

  const clearShape = () => {
    if (circleRef.current) { circleRef.current.setMap(null); circleRef.current = null; }
    if (polyRef.current) { polyRef.current.setMap(null); polyRef.current = null; }
    setSummary(null); setLiveError(null); setShapeActive(false);
    setTool(null);
    dmRef.current?.setDrawingMode(null);
    onChange(null);
  };

  const statusView = () => {
    if (status === 'nokey') {
      return (
        <div className="h-full flex flex-col items-center justify-center text-center px-6">
          <MapPinOff className="w-10 h-10 text-gray-300 mb-3" />
          <p className="text-sm font-medium text-gray-700">Map drawing is not configured</p>
          <p className="text-xs text-gray-500 mt-1 max-w-sm">
            Add a <code className="bg-gray-100 px-1 rounded">VITE_GOOGLE_MAPS_API_KEY</code> (Maps JavaScript API + Drawing
            Library enabled) to draw geofences on a map. Radius/address geofences and bulk import still work without it.
          </p>
        </div>
      );
    }
    if (status === 'loading') {
      return (
        <div className="h-full flex flex-col items-center justify-center gap-2 text-gray-500">
          <Loader2 className="w-6 h-6 animate-spin" />
          <p className="text-xs">Loading map…</p>
        </div>
      );
    }
    if (status === 'error') {
      return (
        <div className="h-full flex items-center justify-center text-center px-6">
          <p className="text-xs text-red-600 max-w-sm">{errorMsg || 'The map could not be loaded.'}</p>
        </div>
      );
    }
    return null;
  };

  return (
    <div>
      <div className="relative rounded-lg overflow-hidden border border-gray-200" style={{ height: 380 }}>
        {status === 'ready' ? (
          <>
            <div ref={containerRef} className="w-full h-full" />
            {/* Styled toolbar — replaces Google's default drawing control */}
            {!readOnly && (
              <div className="absolute top-2 left-2 flex items-center gap-1 bg-white rounded-lg shadow px-1 py-1">
                <button type="button"
                  onClick={() => pickTool('circle')}
                  disabled={shapeActive}
                  title={shapeActive ? 'Clear the current shape to draw a new one' : 'Draw a circle'}
                  className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs font-medium transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${tool === 'circle' ? 'bg-blue-600 text-white' : 'text-gray-700 hover:bg-gray-100'}`}>
                  <Circle className="w-3.5 h-3.5" /> Circle
                </button>
                <button type="button"
                  onClick={() => pickTool('polygon')}
                  disabled={shapeActive}
                  title={shapeActive ? 'Clear the current shape to draw a new one' : 'Draw a polygon'}
                  className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs font-medium transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${tool === 'polygon' ? 'bg-blue-600 text-white' : 'text-gray-700 hover:bg-gray-100'}`}>
                  <Hexagon className="w-3.5 h-3.5" /> Polygon
                </button>
                <button type="button"
                  onClick={clearShape}
                  disabled={!shapeActive && !circleRef.current && !polyRef.current}
                  title="Clear shape"
                  className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-xs font-medium text-red-600 hover:bg-red-50 disabled:opacity-40 disabled:cursor-not-allowed">
                  <Eraser className="w-3.5 h-3.5" /> Clear
                </button>
              </div>
            )}
            {!readOnly && shapeActive && (
              <div className="absolute bottom-2 left-2 bg-white/95 rounded-md px-2.5 py-1 text-[11px] text-gray-600 shadow">
                Drag the shape (or its vertices) to fine-tune it
              </div>
            )}
            {statusView()}
          </>
        ) : (
          <div className="h-full">{statusView()}</div>
        )}
      </div>

      {readOnly && !summary && <p className="text-xs text-gray-400 mt-2">No shape data.</p>}
      {summary && (
        <p className="text-xs font-medium text-gray-600 mt-2">
          {summary}{liveError && <span className="text-gray-400 font-normal"> · needs review</span>}
        </p>
      )}
      {liveError && (
        <p className={`text-xs mt-1 ${liveError.includes('crosses') ? 'text-red-600' : 'text-amber-600'}`}>
          {liveError}
        </p>
      )}
    </div>
  );
}
