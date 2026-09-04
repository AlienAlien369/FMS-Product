/**
 * Canonical geofence geometry — GeoJSON, mirrored 1:1 from the backend
 * GeofenceGeometry (src/Freebuff.Platform.Infrastructure/Geofencing/
 * GeofenceGeometry.cs) so live draw-time checks agree with the server's
 * save-time validation. The backend remains the source of truth — these
 * helpers only make the UX honest before a request leaves the browser.
 *
 * Storage shape (ONLY two forms):
 *   circle:  { type: 'circle',  center: [lng, lat], radiusMeters: number }
 *   polygon: { type: 'polygon', coordinates: [[lng, lat], ...] }  (ring closed)
 */

export type CircleShape = { type: 'circle'; center: [number, number]; radiusMeters: number };
export type PolygonShape = { type: 'polygon'; coordinates: [number, number][] };
export type GeoShape = CircleShape | PolygonShape;

export const MIN_RADIUS_M = 10;
export const MAX_RADIUS_M = 50_000;
export const MAX_POLYGON_AREA_KM2 = 8_000; // π·50km² ≈ 7,854 km² — same reasoning as the radius cap

const EPS = 1e-9;

export function parseShape(json: string | null | undefined): GeoShape | null {
  if (!json) return null;
  try {
    const obj = JSON.parse(json);
    return normalizeShape(obj);
  } catch {
    return null;
  }
}

/** Coerce an unknown object into a valid GeoShape, or null. */
export function normalizeShape(obj: unknown): GeoShape | null {
  if (!obj || typeof obj !== 'object') return null;
  const o = obj as Record<string, unknown>;
  if (o.type === 'circle') {
    const c = o.center;
    const r = o.radiusMeters;
    if (Array.isArray(c) && c.length >= 2 && typeof r === 'number' && isFinite(r)) {
      return { type: 'circle', center: [Number(c[0]), Number(c[1])], radiusMeters: r };
    }
    return null;
  }
  if (o.type === 'polygon') {
    const coords = o.coordinates;
    if (!Array.isArray(coords)) return null;
    const pts: [number, number][] = [];
    for (const p of coords) {
      if (!Array.isArray(p) || p.length < 2) return null;
      const lng = Number(p[0]);
      const lat = Number(p[1]);
      if (!isFinite(lng) || !isFinite(lat)) return null;
      pts.push([lng, lat]);
    }
    return { type: 'polygon', coordinates: pts };
  }
  return null;
}

export function serializeShape(shape: GeoShape | null): string | null {
  return shape ? JSON.stringify(shape) : null;
}

/**
 * Derive a polygon from a legacy Coordinates blob — either GeoJSON rings
 * [[lng,lat],…] or the pre-GeoJSON seed format [{lat,lng},…]. Returns null
 * when the blob is not a usable ring. Used as a fallback for rows whose
 * canonical Geometry was never backfilled.
 */
export function deriveLegacyPolygon(coordinates: string | undefined | null, type?: number): PolygonShape | null {
  if (!coordinates || coordinates === '[]' || (type !== 1 && type !== 2)) return null;
  let raw: unknown;
  try {
    raw = JSON.parse(coordinates);
  } catch {
    return null;
  }
  if (!Array.isArray(raw) || raw.length < 3) return null;
  const pts: [number, number][] = [];
  for (const item of raw) {
    if (Array.isArray(item) && item.length >= 2 && typeof item[0] === 'number' && typeof item[1] === 'number') {
      pts.push([item[0], item[1]]); // [lng, lat]
    } else if (item && typeof item === 'object' && typeof (item as { lng?: unknown }).lng === 'number' && typeof (item as { lat?: unknown }).lat === 'number') {
      const { lng, lat } = item as { lng: number; lat: number };
      pts.push([lng, lat]);
    } else {
      return null;
    }
  }
  return pts.length >= 3 ? { type: 'polygon', coordinates: pts } : null;
}

export function isCircle(shape: GeoShape): shape is CircleShape {
  return shape.type === 'circle';
}

export function isPolygon(shape: GeoShape): shape is PolygonShape {
  return shape.type === 'polygon';
}

/** Build a circle shape from a Google-style lat/lng center + radius in metres. */
export function circleFromLatLng(lat: number, lng: number, radiusMeters: number): CircleShape {
  return { type: 'circle', center: [lng, lat], radiusMeters };
}

/** Build a polygon shape from Google-style [lat, lng] vertices. */
export function polygonFromLatLngs(verts: { lat: number; lng: number }[]): PolygonShape {
  return { type: 'polygon', coordinates: verts.map(v => [v.lng, v.lat] as [number, number]) };
}

/** Human-readable summary for the modal ("Circle · 500 m" / "Polygon · 6 points · ~2.1 km²"). */
export function shapeSummary(shape: GeoShape | null): string | null {
  if (!shape) return null;
  if (isCircle(shape)) {
    return shape.radiusMeters >= 1000
      ? `Circle · ${(shape.radiusMeters / 1000).toFixed(1)} km radius`
      : `Circle · ${Math.round(shape.radiusMeters)} m radius`;
  }
  const km2 = polygonAreaKm2(shape.coordinates);
  return `Polygon · ${shape.coordinates.length} points · ~${km2 >= 10 ? km2.toFixed(0) : km2.toFixed(1)} km²`;
}

/** Full validation — returns null when the shape is acceptable to save. Mirrors backend Validate(). */
export function validateShape(shape: GeoShape | null): string | null {
  if (!shape) return 'Draw or enter a shape first.';
  if (isCircle(shape)) {
    const [lng, lat] = shape.center;
    if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return 'Circle center is outside valid coordinates.';
    if (!isFinite(shape.radiusMeters) || shape.radiusMeters < MIN_RADIUS_M) {
      return `Radius must be at least ${MIN_RADIUS_M.toLocaleString()} m.`;
    }
    if (shape.radiusMeters > MAX_RADIUS_M) {
      return `Radius cannot exceed ${MAX_RADIUS_M / 1000} km — that is a region, not a geofence.`;
    }
    return null;
  }
  const pts = shape.coordinates;
  if (pts.length < 3) return 'A polygon needs at least 3 points.';
  const distinct = new Set(pts.map(p => `${p[0]},${p[1]}`));
  if (distinct.size < 3) return 'A polygon needs at least 3 distinct points.';
  for (const [lng, lat] of pts) {
    if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return 'Polygon contains a point outside valid coordinates.';
  }
  for (let i = 0; i < pts.length; i++) {
    const a = pts[i];
    const b = pts[(i + 1) % pts.length];
    if (a[0] === b[0] && a[1] === b[1]) return 'Polygon has duplicate consecutive points.';
  }
  if (isSelfIntersecting(pts)) return 'This shape crosses itself — adjust the points.';
  const area = polygonAreaKm2(pts);
  if (area > MAX_POLYGON_AREA_KM2) {
    return `Polygon area is too large (${area.toFixed(1)} km²) — max is ${MAX_POLYGON_AREA_KM2.toLocaleString()} km².`;
  }
  return null;
}

/** True when any two non-adjacent edges properly cross or overlap collinearly. */
export function isSelfIntersecting(verts: [number, number][]): boolean {
  const n = verts.length;
  for (let i = 0; i < n; i++) {
    const a1 = verts[i];
    const a2 = verts[(i + 1) % n];
    for (let j = i + 1; j < n; j++) {
      if (j === i + 1 || (i === 0 && j === n - 1)) continue; // adjacent edges share a vertex
      const b1 = verts[j];
      const b2 = verts[(j + 1) % n];
      if (segmentsIntersect(a1, a2, b1, b2)) return true;
    }
  }
  return false;
}

function cross(o: number[], a: number[], b: number[]): number {
  return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
}

function onSeg(a: number[], b: number[], p: number[]): boolean {
  return p[0] >= Math.min(a[0], b[0]) - EPS && p[0] <= Math.max(a[0], b[0]) + EPS
    && p[1] >= Math.min(a[1], b[1]) - EPS && p[1] <= Math.max(a[1], b[1]) + EPS;
}

function segmentsIntersect(a: number[], b: number[], c: number[], d: number[]): boolean {
  const d1 = cross(c, d, a);
  const d2 = cross(c, d, b);
  const d3 = cross(a, b, c);
  const d4 = cross(a, b, d);
  if (((d1 > EPS && d2 < -EPS) || (d1 < -EPS && d2 > EPS))
    && ((d3 > EPS && d4 < -EPS) || (d3 < -EPS && d4 > EPS))) return true;
  if (Math.abs(d1) <= EPS && onSeg(c, d, a)) return true;
  if (Math.abs(d2) <= EPS && onSeg(c, d, b)) return true;
  if (Math.abs(d3) <= EPS && onSeg(a, b, c)) return true;
  if (Math.abs(d4) <= EPS && onSeg(a, b, d)) return true;
  return false;
}

/** Equirectangular area approximation — same as backend (fine for a sanity bound). */
export function polygonAreaKm2(verts: [number, number][]): number {
  const n = verts.length;
  if (n < 3) return 0;
  const meanLat = (verts.reduce((s, v) => s + v[1], 0) / n) * Math.PI / 180;
  const lngScale = 111.32 * Math.cos(meanLat);
  let sum = 0;
  for (let i = 0; i < n; i++) {
    const [lng1, lat1] = verts[i];
    const [lng2, lat2] = verts[(i + 1) % n];
    sum += (lng1 * lngScale) * (lat2 * 111.32) - (lng2 * lngScale) * (lat1 * 111.32);
  }
  return Math.abs(sum / 2);
}

/** Haversine distance in metres — for client-side containment sanity checks. */
export function haversineMeters(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6371000;
  const phi1 = lat1 * Math.PI / 180;
  const phi2 = lat2 * Math.PI / 180;
  const dPhi = (lat2 - lat1) * Math.PI / 180;
  const dLng = (lng2 - lng1) * Math.PI / 180;
  const a = Math.sin(dPhi / 2) ** 2 + Math.cos(phi1) * Math.cos(phi2) * Math.sin(dLng / 2) ** 2;
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

/** Point-in-polygon (ray casting, on-boundary counts as inside) — client mirror of the backend. */
export function pointInPolygon(verts: [number, number][], lat: number, lng: number): boolean {
  const n = verts.length;
  if (n < 3) return false;
  for (let i = 0; i < n; i++) {
    const a = verts[i];
    const b = verts[(i + 1) % n];
    if (Math.abs(cross(a, b, [lng, lat])) <= EPS && onSeg(a, b, [lng, lat])) return true;
  }
  let inside = false;
  for (let i = 0, j = n - 1; i < n; j = i++) {
    const vi = verts[i];
    const vj = verts[j];
    const crosses = (vi[1] > lat) !== (vj[1] > lat)
      && lng < (vj[0] - vi[0]) * (lat - vi[1]) / (vj[1] - vi[1]) + vi[0];
    if (crosses) inside = !inside;
  }
  return inside;
}

export function containsPoint(shape: GeoShape | null, lat: number, lng: number): boolean {
  if (!shape) return false;
  if (isCircle(shape)) {
    const [clng, clat] = shape.center;
    return haversineMeters(clat, clng, lat, lng) <= shape.radiusMeters;
  }
  return pointInPolygon(shape.coordinates, lat, lng);
}
