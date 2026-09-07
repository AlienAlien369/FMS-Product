// Live end-to-end trip drive against the real local API (localhost:8080) + real Postgres:
//   device fixes via /api/v1/ingest/sample-json -> trip auto-start on origin exit ->
//   corridor-deviation alert after threshold -> restricted-zone entry alert ->
//   auto-complete on end-zone entry. Verifies Alert rows via API/SQL afterwards and
//   alert.fired notifications landing for the company's admins.
const BASE = process.env.BASE || 'http://localhost:8080';
const EMAIL = process.env.EMAIL || 'admin@demofleet.com';
const PASS = process.env.PASS || 'Admin@123';

let failures = 0;
function check(label, ok, detail) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${detail ? ` — ${detail}` : ''}`);
  if (!ok) failures++;
}

async function login() {
  const r = await fetch(`${BASE}/api/v1/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASS }),
  });
  const j = await r.json();
  if (!j.success) throw new Error(`login failed: ${JSON.stringify(j)}`);
  return j.data.token;
}

async function api(token, method, path, body) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const r = await fetch(`${BASE}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
  let j = null;
  try { j = await r.json(); } catch { /* empty body */ }
  return { status: r.status, json: j };
}

const suffix = Date.now().toString(36);
const name = `LIVE-DRIVE-${suffix}`;
const imei = '860' + Math.floor(100_000_000_000 + Math.random() * 899_999_999_999); // 15 digits

const token = await login();
console.log(`login OK as ${EMAIL}`);

// ── Fleet + device ────────────────────────────────────────────────────────
const veh = await api(token, 'POST', '/api/v1/vehicles', {
  registrationNumber: `LD-${suffix.toUpperCase()}`, name,
  vehicleType: 'Truck', make: 'Tata', model: 'Prima', year: 2023, fuelType: 1,
});
check('vehicle created', veh.status === 201 || veh.status === 200, `status ${veh.status}`);
const vehicleId = veh.json?.data?.id;

const drv = await api(token, 'POST', '/api/v1/drivers', {
  employeeId: `LD-${suffix}`, firstName: 'Live', lastName: 'Drive', email: `live.drive.${suffix}@test.dev`,
});
check('driver created', drv.status === 201 || drv.status === 200, `status ${drv.status}`);
const driverId = drv.json?.data?.id;

const dev = await api(token, 'POST', '/api/v1/devices', {
  vendorCode: 'sample-json', deviceType: 0, identityType: 0, identityValue: imei,
});
check('device registered (sample-json IMEI)', dev.status === 201 || dev.status === 200, `status ${dev.status}`);
const deviceId = dev.json?.data?.id;
const asg = await api(token, 'POST', `/api/v1/vehicles/${vehicleId}/devices`, { deviceId, role: 0 });
check('device assigned to vehicle', asg.status === 201 || asg.status === 200, `status ${asg.status}`);

// ── Geofences: origin, end, restricted ───────────────────────────────────
async function geofence(fn, lat, lng, r) {
  const g = await api(token, 'POST', '/api/v1/geofences', {
    name: `${fn} ${name}`, type: 0, centerLatitude: lat, centerLongitude: lng, radius: r,
  });
  return { status: g.status, json: g.json };
}
const gOrigin = await geofence('G-ORIGIN', 23.0, 72.5, 500);
const gEnd = await geofence('G-END', 23.2, 72.7, 500);
const gRestricted = await geofence('G-RESTRICTED', 23.15, 72.65, 300);
check('origin/end/restricted geofences created',
  gOrigin.status === 201 && gEnd.status === 201 && gRestricted.status === 201,
  `${gOrigin.status}/${gEnd.status}/${gRestricted.status}`);
// Geofence create returns 201 without an id payload — resolve by exact name via the list endpoint.
async function resolveGeofenceId(fn) {
  const list = await api(token, 'GET', `/api/v1/geofences?search=${encodeURIComponent(fn + ' ' + name)}&pageSize=20`);
  const hit = (list.json?.data?.items ?? []).find((g) => g.name === `${fn} ${name}`);
  return hit?.id;
}
const originId = await resolveGeofenceId('G-ORIGIN');
const endId = await resolveGeofenceId('G-END');
const restrictedId = await resolveGeofenceId('G-RESTRICTED');
check('geofence ids resolved by name', !!(originId && endId && restrictedId), `${originId}/${endId}/${restrictedId}`);

// ── Trip with corridor + zone links ──────────────────────────────────────
const trip = await api(token, 'POST', '/api/v1/trips', {
  name,
  type: 0,
  vehicleId,
  driverId,
  waypoints: [
    { sequenceOrder: 1, legType: 0, waypointType: 4, name: 'Depot', latitude: 23.0, longitude: 72.5 },
    { sequenceOrder: 2, legType: 0, waypointType: 1, name: 'Customer', latitude: 23.2, longitude: 72.7 },
  ],
  geofenceLinks: [
    { geofenceId: originId, role: 2 },          // StartZone
    { geofenceId: endId, role: 3 },             // EndZone
    { geofenceId: restrictedId, role: 1 },      // RestrictedZone
  ],
  routeGeometry: JSON.stringify({
    type: 'LineString',
    coordinates: [[72.5, 23.0], [72.7, 23.2]],
  }),
  corridorEnabled: true,
  corridorBufferMeters: 500,
  deviationThresholdMinutes: 1,
});
check('trip created (corridor 500m / 1min threshold)', trip.status === 201, `status ${trip.status}`);
const tripId = trip.json?.data?.id;
check('trip id present', !!tripId, tripId);

const sch = await api(token, 'POST', `/api/v1/trips/${tripId}/status`, { status: 1 });
check('trip scheduled', sch.status === 200, `status ${sch.status}`);

// ── Drive: fixes through the real ingestion endpoint ─────────────────────
const fix = (ts, lat, lng, speed = 40) =>
  api(null, 'POST', '/api/v1/ingest/sample-json', { imei, ts, lat, lon: lng, speed });

const t0 = new Date();
const iso = (m) => new Date(t0.getTime() + m * 60_000).toISOString();

// Drive path (route line runs along lng = 72.5 + Δlat, i.e. (23.0,72.5)→(23.2,72.7)):
//  fix2 steps EAST off the line, fix3/fix4 stay ~2km off it (past the 500m buffer),
//  fix5 enters the restricted zone (which sits ON the line), fix6 ends the trip.
const f1 = await fix(iso(0), 23.0, 72.5);            // inside origin — baseline only
check('fix1 inside origin accepted (baseline)', f1.status === 200, `status ${f1.status}`);
const f2 = await fix(iso(0.5), 23.01, 72.5);         // exits origin → auto-start; starts corridor episode
check('fix2 outside origin accepted', f2.status === 200, `status ${f2.status}`);
const f3 = await fix(iso(2.5), 23.1, 72.62);         // ~2km off the line, 2min after episode start → alert fires
check('fix3 off-line accepted (past threshold)', f3.status === 200, `status ${f3.status}`);
const f4 = await fix(iso(4), 23.1, 72.63);           // still off-line — must NOT duplicate
check('fix4 off-line accepted', f4.status === 200, `status ${f4.status}`);
// The trip detail DTO does not expose DeviatedSince, so verify the episode
// directly in the DB while it is still active (fix5 returns toward the path
// and correctly resets it).
const { spawnSync } = await import('node:child_process');
const dbOut = spawnSync('docker',
  ['exec', 'freebuff-postgres', 'psql', '-U', 'postgres', '-d', 'freebuff_platform', '-t', '-A', '-c',
    `SELECT COALESCE("DeviatedSince" IS NOT NULL, false) FROM "Trips" WHERE "Id"='${tripId}'`],
  { encoding: 'utf8' });
check('DeviatedSince recorded while off-corridor (DB)', dbOut.status === 0 && dbOut.stdout.trim() === 't',
  `DB=${dbOut.stdout.trim() || dbOut.stderr.trim()}`);
const f5 = await fix(iso(5), 23.15, 72.65);           // enters restricted zone → violation alert
check('fix5 restricted-zone entry accepted', f5.status === 200, `status ${f5.status}`);
const f6 = await fix(iso(6), 23.2, 72.7);             // enters end zone → auto-complete
check('fix6 end-zone entry accepted', f6.status === 200, `status ${f6.status}`);

// ── Trip state after the drive ───────────────────────────────────────────
const det = await api(token, 'GET', `/api/v1/trips/${tripId}`);
check('trip detail 200', det.status === 200, `status ${det.status}`);
if (det.json?.data) {
  const d = det.json.data;
  check('trip auto-started then auto-completed', d.statusName === 'Completed', `statusName=${d.statusName}`);
  console.log(`      trip=${d.name} status=${d.statusName} vehicle=${d.vehicleId} driver=${d.driverId}`);
  const sources = (d.statusHistory || []).map((h) => h.source).join(',');
  check('transitions via geofence_event', sources.includes('geofence_event'), sources);
}

// ── Notifications: alert.fired must land for company admins ──────────────
const recent = await api(token, 'GET', '/api/v1/notifications/recent?limit=25');
check('notifications recent 200', recent.status === 200, `status ${recent.status}`);
const items = recent.json?.data?.items ?? [];
const fired = items.filter((n) => n.eventType === 'alert.fired' && (n.title || '').includes(name));
console.log(`      alert.fired notifications for this trip: ${fired.length}`);
for (const n of fired) console.log(`        - [${n.eventType}] ${n.title} (read=${n.isRead})`);
check('corridor notification present', fired.some((n) => /deviated from its route corridor/i.test(n.title)), 'corridor title');
check('restricted-zone notification present', fired.some((n) => /restricted zone/i.test(n.title)), 'restricted title');

const unread = await api(token, 'GET', '/api/v1/notifications/unread-count');
const unreadCount = unread.json?.data?.count ?? unread.json?.data ?? -1;
console.log(`      unread-count endpoint → ${unreadCount}`);

console.log('');
console.log(`SUMMARY vehicleId=${vehicleId} tripId=${tripId} imei=${imei}`);
console.log(`verification SQL hints:`);
console.log(`  SELECT "AlertType","Title" FROM "Alerts" WHERE "VehicleId"='${vehicleId}'::uuid ORDER BY "CreatedAt";`);
console.log(`  SELECT "EventType","Title","UserId" FROM "Notifications" WHERE "EventType"='alert.fired' AND "Title" LIKE '%${name}%';`);
process.exit(failures === 0 ? 0 : 1);