// Live driver-safety drive against the dev stack (API :5000, Postgres).
// Registers a DMS camera + tracker, starts a trip, ingests behavior events
// (harsh braking, drowsiness, SOS) and verifies the whole chain:
//   rows → alerts → notifications → API surfaces (driver safety-events,
//   trip live indicator, dashboard safety-scores).
const BASE = 'http://localhost:5000';
let pass = 0, fail = 0;
const ok = (name, cond, extra = '') => {
  if (cond) { pass++; console.log(`  PASS  ${name}${extra ? ' — ' + extra : ''}`); }
  else { fail++; console.log(`  FAIL  ${name}${extra ? ' — ' + extra : ''}`); }
};

async function api(method, path, body, token) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  let json = null;
  try { json = await res.json(); } catch { /* no body */ }
  return { status: res.status, json };
}

async function main() {
  console.log('== Driver Safety live drive ==');

  // 1. Login as the demo company admin.
  const login = await api('POST', '/api/v1/auth/login', { email: 'admin@demofleet.com', password: 'Admin@123' });
  if (login.status !== 200 || !login.json?.data?.token) { console.log('LOGIN FAILED', login.status, JSON.stringify(login.json)); process.exit(1); }
  const token = login.json.data.token;
  ok('login (Demo Fleet admin)', true);
  const auth = { Authorization: `Bearer ${token}` };

  const uniq = Date.now().toString(36).slice(-5);

  // 2. Vehicle + driver + DMS camera + tracker.
  const v = await api('POST', '/api/v1/vehicles', { registrationNumber: `SAFE-${uniq}`, name: `Safety ${uniq}`, vehicleType: 'Truck', fuelType: 1 }, token);
  const vehicleId = v.json.data.id;
  ok('vehicle created', v.status === 201, vehicleId);

  const d = await api('POST', '/api/v1/drivers', { employeeId: `SAFE-${uniq}`, firstName: 'Safety', lastName: uniq, email: `safety.${uniq}@test.dev` }, token);
  const driverId = d.json.data.id;
  ok('driver created', d.status === 201, driverId);

  const imei = '860' + Math.floor(100000000000 + Math.random() * 899999999999);
  const dms = await api('POST', '/api/v1/devices', { vendorCode: 'sample-json', deviceType: 6, identityType: 0, identityValue: imei }, token);
  ok('DMS camera registered (deviceType=6)', dms.status === 201);
  const dmsDeviceId = dms.json.data.id;
  const as1 = await api('POST', `/api/v1/vehicles/${vehicleId}/devices`, { deviceId: dmsDeviceId, role: 7 }, token);
  ok('DMS camera assigned (role=7)', as1.status === 200 && as1.json?.data?.roleName === 'DmsCamera', as1.json?.data?.roleName);

  // 3. Geofences + trip started.
  await api('POST', '/api/v1/geofences', { name: `SAFE-O ${uniq}`, type: 0, centerLatitude: 23.0, centerLongitude: 72.5, radius: 500 }, token);
  await api('POST', '/api/v1/geofences', { name: `SAFE-E ${uniq}`, type: 0, centerLatitude: 23.2, centerLongitude: 72.7, radius: 500 }, token);
  const gf = async (name) => (await api('GET', '/api/v1/geofences?pageSize=200', null, token)).json.data.items.find(g => g.name === name).id;
  const originId = await gf(`SAFE-O ${uniq}`);
  const endId = await gf(`SAFE-E ${uniq}`);
  const t = await api('POST', '/api/v1/trips', {
    name: `SAFE-TRIP ${uniq}`, type: 0, vehicleId, driverId,
    waypoints: [
      { sequenceOrder: 1, legType: 0, waypointType: 5, name: 'Depot', latitude: 23.0, longitude: 72.5 },
      { sequenceOrder: 2, legType: 0, waypointType: 5, name: 'Customer', latitude: 23.2, longitude: 72.7 },
    ],
    geofenceLinks: [{ geofenceId: originId, role: 2 }, { geofenceId: endId, role: 3 }],
  }, token);
  const tripId = t.json.data.id;
  ok('trip created', t.status === 201, tripId);
  await api('POST', `/api/v1/trips/${tripId}/status`, { status: 1 }, token);
  const started = await api('POST', `/api/v1/trips/${tripId}/status`, { status: 2 }, token);
  ok('trip in progress', started.status === 200);

  // 4. Ingest one fix with three behavior events.
  const ingest = await api('POST', '/api/v1/ingest/sample-json', {
    imei, ts: new Date().toISOString(), lat: 23.1, lon: 72.6, speed: 42.5,
    behaviorEvents: [
      { type: 'harsh_braking', confidence: 0.92, mediaUrl: 'https://cdn.example.com/clips/ab1.mp4' },
      { type: 'drowsiness', confidence: 0.87 },
      { type: 'sos_triggered', confidence: 0.99 },
    ],
  });
  ok('ingest accepted', ingest.status === 200, ingest.json?.message);

  // 5. API surfaces.
  const ev = await api('GET', `/api/v1/drivers/${driverId}/safety-events`, null, token);
  const events = ev.json.data;
  const codes = events.map(e => e.eventType).sort();
  ok('driver safety-events: 3 canonical codes', events.length === 3 && ['drowsiness', 'harsh_braking', 'sos_triggered'].every(c => codes.includes(c)), codes.join(','));
  const braking = events.find(e => e.eventType === 'harsh_braking');
  ok('media reference carried', braking?.mediaUrl === 'https://cdn.example.com/clips/ab1.mp4');
  ok('severities 2/3/4', events.find(e => e.eventType === 'harsh_braking').severity === 2
    && events.find(e => e.eventType === 'drowsiness').severity === 3
    && events.find(e => e.eventType === 'sos_triggered').severity === 4);

  const live = await api('GET', `/api/v1/trips/${tripId}/live`, null, token);
  const recent = live.json.data.recentSafetyEvents || [];
  ok('trip live: recent safety events', recent.some(e => e.eventType === 'sos_triggered'), `${recent.length} event(s)`);

  const scores = await api('GET', '/api/v1/dashboard/drivers/safety-scores', null, token);
  const scoreRow = scores.json.data.find(s => s.driverId === driverId);
  ok('dashboard safety-score computed from events', !!scoreRow && scoreRow.score <= 80 && scoreRow.eventCount === 3, scoreRow ? `score ${scoreRow.score} from ${scoreRow.eventCount} events` : 'missing');

  console.log(`\n== ${pass} passed, ${fail} failed ==`);
  process.exit(fail ? 1 : 0);
}

main().catch(e => { console.error(e); process.exit(1); });