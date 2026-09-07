// Live drive of the Proof-of-Delivery flow against the dev API + dev DB.
// Mirrors exactly what the Trips UI + PodCaptureModal call.
const BASE = 'http://localhost:8080/api/v1';
let passed = 0, failed = 0;
const ok = (cond, label, extra = '') => {
  if (cond) { passed++; console.log(`  PASS  ${label}${extra ? ' — ' + extra : ''}`); }
  else { failed++; console.log(`  FAIL  ${label}${extra ? ' — ' + extra : ''}`); }
};

async function api(method, path, body, token) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let json = null;
  try { json = await res.json(); } catch { /* empty */ }
  return { status: res.status, json };
}

const uid = Math.random().toString(36).slice(2, 8);

// 1. Login
const login = await api('POST', '/auth/login', { email: 'admin@demofleet.com', password: 'Admin@123' });
ok(login.status === 200 && login.json?.data?.token, 'login admin@demofleet.com');
const token = login.json?.data?.token;
if (!token) { console.log(`\n${passed} passed, ${failed} failed`); process.exit(1); }

// 2. Fleet pair
const veh = await api('POST', '/vehicles', { registrationNumber: `POD-${uid}`, name: `POD Vehicle ${uid}`, vehicleType: 'Truck', make: 'Tata', model: 'Prima', year: 2023, fuelType: 1 }, token);
const vehicleId = veh.json?.data?.id;
const drv = await api('POST', '/drivers', { employeeId: `POD-${uid}`, firstName: 'POD', lastName: `Driver ${uid}`, email: `live.pod.${uid}@test.dev` }, token);
const driverId = drv.json?.data?.id;
ok(vehicleId && driverId, 'fleet pair created');

// 3. Delivery trip with require-POD
const fence = await (async () => {
  const { json } = await api('GET', '/geofences?pageSize=100', undefined, token);
  return json?.data?.items?.[0]?.id;
})();
const trip = await api('POST', '/trips', {
  name: `POD Live ${uid}`, type: 0, vehicleId, driverId, requirePodForDelivery: true,
  waypoints: [
    { sequenceOrder: 1, legType: 0, waypointType: 0, name: 'Depot', latitude: 23.0225, longitude: 72.5714 },
    { sequenceOrder: 2, legType: 0, waypointType: 1, name: 'Customer A', latitude: 23.05, longitude: 72.62, customerPhone: '+919876543210', customerEmail: 'cust@example.com' },
  ],
  geofenceLinks: [{ geofenceId: fence, role: 0, sequenceOrder: 1 }],
}, token);
const tripId = trip.json?.data?.id;
ok(trip.status === 201 && tripId, 'delivery trip created (requirePod)');

// 4. Start the trip
for (const s of [1, 2]) {
  const r = await api('POST', `/trips/${tripId}/status`, { status: s }, token);
  ok(r.status === 200, `status → ${s}`);
}

// 5. Detail shows the resolved policy
const detail = await api('GET', `/trips/${tripId}`, undefined, token);
const wp = detail.json?.data?.waypoints?.find(w => w.waypointType === 1);
ok(detail.json?.data?.podRequired === true, 'trip detail podRequired=true');
ok(detail.json?.data?.requirePodForDelivery === true, 'trip detail requirePodForDelivery=true');
ok(wp?.customerPhone === '+919876543210', 'waypoint customer contact projected');

// 6. Arrival blocked before evidence
const blocked = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/arrive`, {}, token);
ok(blocked.status === 400 && /proof of delivery/i.test(blocked.json?.message ?? ''), 'arrival blocked without POD → 400');

// 7. Signature capture (far from waypoint → mismatch flag)
const sig = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/pod/signature`, {
  signatureSvg: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 40"><path d="M 10 30 L 30 10 L 50 25" stroke="#000" fill="none"/></svg>`,
  latitude: 22.5, longitude: 72.57, notes: 'signed at gate',
}, token);
ok(sig.status === 200 && sig.json?.data?.verified === true, 'signature captured + verified');
ok(sig.json?.data?.locationMismatch === true, 'signature location mismatch flagged (~50 km)');

// 8. OTP issue + verify
const otp = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/pod/otp`, {}, token);
ok(otp.status === 200 && /^[0-9]{6}$/.test(otp.json?.data?.otp ?? ''), `OTP issued (dev relay: ${otp.json?.data?.otp})`);
const ver = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/pod/otp/verify`, { code: otp.json?.data?.otp, latitude: 23.05, longitude: 72.62 }, token);
ok(ver.status === 200 && ver.json?.data?.otpVerified === true, 'OTP verified → evidence recorded');

// 9. Photo capture (at waypoint → no mismatch)
const photo = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/pod/photo`, { imageUrl: `https://cdn.example/pod/${uid}.jpg`, latitude: 23.05, longitude: 72.62 }, token);
ok(photo.status === 200 && photo.json?.data?.locationMismatch === false, 'photo captured at waypoint, no mismatch');

// 10. Arrival now allowed
const arrived = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/arrive`, {}, token);
ok(arrived.status === 200, 'arrival allowed after evidence');

// 11. Evidence panels (admin + future customer-link surface use the same endpoints)
const tripPod = await api('GET', `/trips/${tripId}/pod`, undefined, token);
ok(tripPod.status === 200 && tripPod.json?.data?.length === 3, `trip evidence panel: ${tripPod.json?.data?.length} records`);
const wpPod = await api('GET', `/trips/${tripId}/waypoints/${wp.id}/pod`, undefined, token);
ok(wpPod.json?.data?.every(r => r.waypointName === 'Customer A'), 'per-waypoint evidence + waypointName');

// 12. Wrong OTP rejected (no double-verify)
const wrong = await api('POST', `/trips/${tripId}/waypoints/${wp.id}/pod/otp/verify`, { code: '000000' }, token);
ok(wrong.status === 400, 'wrong OTP rejected');

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);