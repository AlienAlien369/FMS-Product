/**
 * Lazy loader for the Google Maps JavaScript API (+ optional libraries).
 * The app key comes from VITE_GOOGLE_MAPS_API_KEY — without it the map pane
 * renders a clear setup state instead of attempting to load the SDK.
 * The Maps JavaScript API and (for geofence drawing) the "drawing" library
 * must both be enabled for that key in Google Cloud Console.
 *
 * IMPORTANT: the Maps version is pinned to 3.64, not "weekly". Google removed
 * the google.maps.drawing.DrawingManager class in version 3.65 (the drawing
 * library is deprecated; `v=weekly` now serves >= 3.65 and throws on
 * instantiation, which breaks the Draw-on-Map geofence editor). 3.64 is the
 * last release that still ships the implementation. When 3.64 is eventually
 * retired, the drawing UI must move to custom overlay drawing — see
 * GeofenceMapPane.
 */

let loadPromise: Promise<typeof google> | null = null;

export const GOOGLE_MAPS_API_KEY: string = (import.meta.env.VITE_GOOGLE_MAPS_API_KEY as string | undefined)?.trim() || '';

export function googleMapsConfigured(): boolean {
  return GOOGLE_MAPS_API_KEY.length > 0;
}

export function loadGoogleMaps(libraries: ('drawing' | 'places' | 'geometry' | 'marker')[] = []): Promise<typeof google> {
  if (!googleMapsConfigured()) {
    return Promise.reject(new Error('VITE_GOOGLE_MAPS_API_KEY is not configured.'));
  }
  if (loadPromise) return loadPromise;
  loadPromise = new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>('script[data-fms-gmaps]');
    const existingLibraries = existing?.dataset.libraries?.split(',') ?? [];
    const needed = [...new Set([...existingLibraries, ...libraries])];
    const onReady = () => {
      if (typeof google !== 'undefined' && google.maps) {
        resolve(google);
      } else {
        reject(new Error('Google Maps failed to initialize.'));
      }
    };
    if (existing && (typeof google === 'undefined' || !google.maps)) {
      existing.addEventListener('load', onReady);
      existing.addEventListener('error', () => reject(new Error('Google Maps script failed to load.')));
      return;
    }
    if (existing && typeof google !== 'undefined' && google.maps) {
      onReady();
      return;
    }
    const script = document.createElement('script');
    script.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(GOOGLE_MAPS_API_KEY)}&libraries=${needed.join(',')}&v=3.64&callback=__fmsGmapsReady`;
    script.async = true;
    script.defer = true;
    script.dataset.fmsGmaps = '1';
    script.dataset.libraries = needed.join(',');
    script.addEventListener('error', () => {
      loadPromise = null;
      reject(new Error('Google Maps script failed to load. Check the API key and enabled APIs.'));
    });
    (window as any).__fmsGmapsReady = onReady;
    document.head.appendChild(script);
  });
  return loadPromise;
}

/** Small helper for a style-friendly default map.</summary> */
export const DEFAULT_MAP_CENTER: google.maps.LatLngLiteral = { lat: 23.0, lng: 79.0 };
export const DEFAULT_MAP_ZOOM = 5;