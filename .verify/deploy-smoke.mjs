// Deploy smoke guard — rerunnable production regression check for the
// real-time notification channel. Catches the two deploy failures that have
// actually happened:
//   1. Render serving a stale instance: /hubs/notifications/negotiate returns
//      404 instead of 401 (no token) / 200 (with token).
//   2. Vercel shipping a bundle whose socket URL is relative: the bell would
//      negotiate against the SPA host instead of the API. The bundle must
//      contain the absolute hub URL.
// Also verifies login and (unless SMOKE_SKIP_WS=1) a real WebSocket handshake.
//
// Usage (Node >= 22 — global fetch/WebSocket):
//   node .verify/deploy-smoke.mjs
// Env overrides: SMOKE_API_URL, SMOKE_WEB_URL, SMOKE_EMAIL, SMOKE_PASSWORD,
//                SMOKE_SKIP_WS=1
// Exit code 0 = all checks pass, 1 = any check failed.

const API = process.env.SMOKE_API_URL || 'https://fms-product-api.onrender.com';
const WEB = process.env.SMOKE_WEB_URL || 'https://fms-product.vercel.app';
const EMAIL = process.env.SMOKE_EMAIL || 'admin@freebuff.com'; // SeedData SuperAdmin
const PASS = process.env.SMOKE_PASSWORD || 'Admin@123';
const SKIP_WS = process.env.SMOKE_SKIP_WS === '1';
const HUB = `${API}/hubs/notifications`;

let failures = 0;
const failed = [];
let passed = 0;
function check(label, ok, detail) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${detail ? ` — ${detail}` : ''}`);
  if (ok) passed++;
  else { failures++; failed.push(label); }
}

async function login() {
  const r = await fetch(`${API}/api/v1/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASS }),
  });
  const j = await r.json();
  if (!j.success) throw new Error(`login failed: ${JSON.stringify(j)}`);
  return j.data.token;
}

async function safeFetch(url, opts) {
  try { return await fetch(url, opts); }
  catch (e) { return { status: 0, json: async () => null, text: async () => String(e.message) }; }
}

function wsHandshake(wsUrl) {
  return new Promise((resolve) => {
    const ws = new WebSocket(wsUrl);
    const timer = setTimeout(() => { ws.close(); resolve('timeout'); }, 12000);
    ws.onopen = () => ws.send(`{"protocol":"json","version":1}\x1e`);
    ws.onmessage = (ev) => {
      const data = String(ev.data);
      if (data.startsWith('{}')) console.log('      handshake OK (protocol json v1)');
      if (data.includes('connected')) {
        clearTimeout(timer);
        ws.close();
        resolve('connected');
      }
    };
    ws.onerror = () => { /* outcome arrives via onclose */ };
    ws.onclose = (e) => {
      clearTimeout(timer);
      if (e.code === 1000) resolve('connected'); // clean close after success
      else resolve(`closed code=${e.code} reason=${e.reason}`);
    };
  });
}

async function bundleCheck() {
  const html = await (await fetch(WEB)).text();
  const scripts = [...html.matchAll(/src="([^"]+\.js)"/g)].map(m => m[1]);
  check('deployed page references JS bundles', scripts.length > 0, scripts.join(', ') || 'no .js script src found');
  let bundle = '';
  for (const s of scripts) {
    const url = s.startsWith('http') ? s : `${WEB}${s.startsWith('/') ? '' : '/'}${s}`;
    const body = await (await fetch(url)).text();
    if (body.includes('/hubs/notifications')) { bundle = body; break; }
  }
  if (!bundle) {
    check('deployed bundle resolves hub to absolute API URL', false, 'no bundle references /hubs/notifications at all');
    return;
  }
  // Minifiers hoist string constants into variables and concatenate them at
  // runtime (e.g. `${origin}${path}`), so the contiguous literal never exists.
  // The reliable signal: the API origin must appear in the SAME minified
  // statement as the hub path. In the broken bundle the two were far apart
  // (the origin only existed in the axios base URL).
  const k = bundle.indexOf('/hubs/notifications');
  const origin = API.replace(/^https?:\/\//, '');
  const windowText = bundle.slice(Math.max(0, k - 400), k + 400);
  const originAdjacent = windowText.includes(API) || windowText.includes(origin);
  check('deployed bundle resolves hub to absolute API URL', originAdjacent,
    originAdjacent
      ? `hub path + ${origin} in the same statement`
      : `only a relative /hubs/notifications path near the socket — bell would hit ${WEB}`);
}

// ── 1. Hub route exists: negotiate without a token must be 401 (or 200),
//      never 404 — a 404 here means the serving instance predates SignalR. ──
{
  const r = await safeFetch(`${HUB}/negotiate?negotiateVersion=1`, { method: 'POST' });
  const detail = r.status === 0 ? `network error: ${await r.text()}` : `HTTP ${r.status}`;
  check('hub negotiate without token -> 401/200 (route exists)', r.status === 401 || r.status === 200,
    r.status === 404 ? `${detail} — STALE INSTANCE: trigger a Render redeploy` : detail);
  if (r.status !== 401 && r.status !== 200) { console.log('\nSMOKE SUMMARY: 0 passed, 1 failed'); process.exit(1); }
}

// ── 2. Login works on production (catches broken seed/creds after redeploy). ──
let token;
try {
  token = await login();
  check('production login', true, `as ${EMAIL}`);
} catch (e) {
  check('production login', false, e.message);
  console.log('\nSMOKE SUMMARY: login failed — fix credentials or seed before continuing');
  process.exit(1);
}

// ── 3. Authenticated negotiate -> 200 with a connection token. ──
let connToken;
{
  const r = await safeFetch(`${HUB}/negotiate?negotiateVersion=1&access_token=${token}`, { method: 'POST' });
  check('hub negotiate with token -> 200', r.status === 200, r.status === 0 ? 'network error' : `HTTP ${r.status}`);
  if (r.status === 200) {
    const j = await r.json();
    connToken = j.connectionToken ?? j.connectionId;
    check('negotiate payload carries connection token', !!connToken);
  }
}

// ── 4. Full WebSocket handshake (negotiate -> upgrade -> "connected" push). ──
if (SKIP_WS) {
  console.log('SKIP  websocket handshake (SMOKE_SKIP_WS=1)');
} else if (connToken) {
  const wsUrl = `${API.replace('https://', 'wss://')}/hubs/notifications?id=${connToken}&access_token=${token}`;
  const wsResult = await wsHandshake(wsUrl);
  check('websocket handshake + "connected" push', wsResult === 'connected', wsResult === 'connected' ? undefined : wsResult);
} else {
  check('websocket handshake + "connected" push', false, 'skipped: no connection token from negotiate');
}

// ── 5. Deployed bundle points the socket at the ABSOLUTE API hub URL. ──
await bundleCheck();

console.log(`\nSMOKE SUMMARY: ${passed} passed, ${failures} failed`);
if (failures > 0) {
  console.log('Failed checks:');
  for (const f of failed) console.log(`  - ${f}`);
  process.exit(1);
}
process.exit(0);