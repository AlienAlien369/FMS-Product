// Live drive of Speed Governor + TPMS against the dev API + dev DB.
// Exercises the exact surfaces the UI calls: fleet policies, sensors,
// ingest (policy-vs-governor + rapid loss), dashboard fleet health.
const BASE = 'http://localhost:8080';

let pass = 0, fail = 0;
const ok = (cond, msg) => { if (cond) { pass++; console.log('PASS  ' + msg); } else { fail++; console.log('FAIL  ' + msg); } };

async function json(method, path, body, token) {
  const res = await fetch(BASE + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: 'Bearer ' + token } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { /* non-json */ }
  return { status: res.status, data };
}

const uniq = () => Math.random().toString(36).slice(2, 8);
const imei = () => '860' + String(Math.floor(100000000000 + Math.random() * 899999999999));

async function main() {
  // Login as demo company admin
  const login = await json('POST', '/api/v1/auth/login', { email: 'admin@demofleet.com', password: 'Admin@123' });
  ok(login.status === 200 && login.data?.data?.token, 'admin login');
  const token = login.data.data.token;

  // 1. Fleet policies: set a 60 km/h speed policy + tyre 5.0–7.0 bar
  let r = await json('GET', '/api/v1/tenant/fleet-policies', null, token);
  ok(r.status === 200, 'GET fleet policies');
  r = await json('PUT', '/api/v1/tenant/fleet-policies', { speedPolicyMaxKmh: 60, tyrePressureMinBar: 5.0, tyrePressureMaxBar: 7.0 }, token);
  ok(r.status === 200, 'PUT fleet policies (speed 60, tyre 5–7 bar)');

  // 2. Vehicle + sample-json device
  r = await json('POST', '/api/v1/vehicles', { registrationNumber: 'SENS-LIVE-' + uniq(), name: 'Sensor Live', vehicleType: 'Truck', make: 'Tata', model: 'Prima', year: 2023, fuelType: 1 }, token);
  ok(r.status === 200 || r.status === 201, 'create vehicle');
  const vehicleId = r.data.data.id;
  const deviceImei = imei();
  r = await json('POST', '/api/v1/devices', { vendorCode: 'sample-json', deviceType: 0, identityType: 0, identityValue: deviceImei }, token);
  ok(r.status === 200 || r.status === 201, 'create device');
  const deviceId = r.data.data.id;
  r = await json('POST', `/api/v1/vehicles/${vehicleId}/devices`, { deviceId, role: 1 }, token);
  ok(r.status === 200 || r.status === 201, 'assign device to vehicle');

  // 3. Ingest 70 km/h with a 80 km/h governor → policy breach (60)
  r = await json('POST', '/api/v1/ingest/sample-json', { imei: deviceImei, ts: new Date().toISOString(), lat: 23.1, lon: 72.6, speed: 70, governorLimit: 80 });
  ok(r.status === 200, 'ingest speed 70 (governor 80)');

  // 4. Sensors surface: over policy, governor shown, not-supports accurate
  r = await json('GET', `/api/v1/vehicles/${vehicleId}/sensors`, null, token);
  const s = r.data?.data;
  ok(r.status === 200, 'GET sensors');
  ok(s?.speedStatus === 'over', `speedStatus=over (got ${s?.speedStatus})`);
  ok(s?.speedKmh === 70 && s?.policySpeedMaxKmh === 60, 'speed 70 vs policy 60');
  ok(s?.speedGovernorLimitKmh === 80 && s?.governorSupported === true, 'governor 80 reported + supported');

  // 5. Alert row + notification for the policy breach (High = 3)
  const alerts1 = await json('GET', `/api/v1/alerts?search=vehicle.speed_limit_exceeded&vehicleId=${vehicleId}`, null, token);
  const speedAlerts = (alerts1.data?.data?.items ?? alerts1.data?.data ?? []).filter(a => a.alertType === 'vehicle.speed_limit_exceeded');
  ok(speedAlerts.length >= 1, `speed alert raised (${speedAlerts.length})`);

  // 6. Rapid tyre loss: 6.0 bar then 4.4 bar 1 min later → critical despite 12% static
  const t0 = new Date();
  r = await json('POST', '/api/v1/ingest/sample-json', { imei: deviceImei, ts: t0.toISOString(), lat: 23.1, lon: 72.6, speed: 40, tyrePressures: [{ position: 'front_left', pressureBar: 6.0, temperatureC: 35 }] });
  ok(r.status === 200, 'ingest tyre 6.0 bar');
  await new Promise(res => setTimeout(res, 1500));
  const t1 = new Date(t0.getTime() + 60000);
  r = await json('POST', '/api/v1/ingest/sample-json', { imei: deviceImei, ts: t1.toISOString(), lat: 23.1, lon: 72.6, speed: 40, tyrePressures: [{ position: 'front_left', pressureBar: 4.4 }] });
  ok(r.status === 200, 'ingest tyre 4.4 bar (1 min later)');

  // 7. Sensors surface reflects critical (same rule as alert)
  r = await json('GET', `/api/v1/vehicles/${vehicleId}/sensors`, null, token);
  const tyre = r.data?.data?.tyres?.find(t => t.positionName === 'Front-left');
  ok(tyre?.status === 'critical', `live tyre status=critical (got ${tyre?.status})`);

  const alerts2 = await json('GET', `/api/v1/alerts?search=vehicle.tyre_pressure_anomaly&vehicleId=${vehicleId}`, null, token);
  const tyreAlerts = (alerts2.data?.data?.items ?? alerts2.data?.data ?? []).filter(a => a.alertType === 'vehicle.tyre_pressure_anomaly');
  ok(tyreAlerts.length >= 1 && tyreAlerts.some(a => (a.message ?? '').includes('rapid pressure loss')), `rapid-loss alert raised (${tyreAlerts.length})`);

  // 8. Dashboard fleet health widget counts the over-speed vehicle
  r = await json('GET', '/api/v1/dashboard/fleet-health', null, token);
  ok(r.status === 200 && r.data?.data != null, 'GET dashboard fleet-health');
  ok(r.data.data.overSpeedCount >= 1, `overSpeedCount >= 1 (got ${r.data.data.overSpeedCount})`);

  console.log(`\n${pass} passed, ${fail} failed`);
  process.exit(fail ? 1 : 0);
}

main().catch(e => { console.error(e); process.exit(1); });