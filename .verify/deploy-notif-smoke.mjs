// Live E2E smoke: cross-user notification delivery on the running dev stack.
// SuperAdmin acts (alert entitlement toggle) -> CompanyAdmin must receive a bell notification.
const BASE = process.env.BASE ?? 'http://localhost:8080';

async function login(email, password) {
  const r = await fetch(`${BASE}/api/v1/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  });
  const j = await r.json();
  if (!j.success) throw new Error(`login failed for ${email}: ${JSON.stringify(j)}`);
  return j.data.token;
}

function tenantIdFromToken(token) {
  const payload = token.split('.')[1];
  const json = JSON.parse(Buffer.from(payload, 'base64url').toString('utf8'));
  return json['tenant_id'];
}

async function api(token, method, path, body) {
  const r = await fetch(`${BASE}${path}`, {
    method,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: body ? JSON.stringify(body) : undefined,
  });
  return { status: r.status, json: await r.json() };
}

const checks = [];
function check(name, ok, detail = '') {
  checks.push({ name, ok, detail });
  console.log(`[${ok ? 'PASS' : 'FAIL'}] ${name}${detail ? ' — ' + detail : ''}`);
}

const sa = await login('admin@freebuff.com', 'Admin@123');
const ca = await login('admin@demofleet.com', 'Admin@123');

// 1. Find demo company id (CompanyAdmin's tenant claim).
const companyId = tenantIdFromToken(ca);
check('company admin identity resolves company', !!companyId, companyId ?? '');

// 2. SuperAdmin lists the company's alert subscriptions.
const subs = await api(sa, 'GET', `/api/v1/companies/${companyId}/alert-subscriptions`);
const subList = subs.json?.data ?? [];
check('alert subscriptions list for company', Array.isArray(subList) && subList.length > 0, `${subList.length} subs`);
const corridor = subList.find(s => s.alertTypeCode === 'route.corridor_deviation');
check('corridor_deviation sub exists', !!corridor, corridor?.alertTypeCode ?? 'missing');

// 3. Baseline BEFORE toggling.
const before = await api(ca, 'GET', '/api/v1/notifications/unread-count');
const preCount = before.json?.data?.count ?? -1;
check('CA unread count baseline fetched', preCount >= 0, `count=${preCount}`);

// 4. Toggle corridor OFF -> CompanyAdmin should get a company.config_changed notification.
const offRes = await api(sa, 'PUT', `/api/v1/companies/${companyId}/alert-subscriptions/${corridor.alertTypeId}`, { enabled: false });
check('toggle OFF succeeds', offRes.status === 200, `status ${offRes.status}`);

// 5. Toggle back ON (restore state) -> second notification.
const onRes = await api(sa, 'PUT', `/api/v1/companies/${companyId}/alert-subscriptions/${corridor.alertTypeId}`, { enabled: true });
check('toggle ON succeeds', onRes.status === 200, `status ${onRes.status}`);

const after = await api(ca, 'GET', '/api/v1/notifications/unread-count');
const postCount = after.json?.data?.count ?? -1;
check('CA received 2 notifications from toggles', postCount === preCount + 2, `before=${preCount} after=${postCount}`);

const recent = await api(ca, 'GET', '/api/v1/notifications/recent?limit=5');
const items = recent.json?.data?.items ?? [];
const configEvents = items.filter(i => i.eventType === 'company.config_changed');
check('recent list shows config_changed events', configEvents.length >= 2, `${configEvents.length} events`);

// 6. Mark-read round trip.
const first = items[0];
if (first) {
  const mr = await api(ca, 'PUT', `/api/v1/notifications/${first.id}/read`);
  check('mark-read succeeds', mr.status === 200, `status ${mr.status}`);
  const afterRead = await api(ca, 'GET', '/api/v1/notifications/unread-count');
  check('unread count drops after mark-read', afterRead.json?.data?.count === postCount - 1, `count=${afterRead.json?.data?.count}`);
}

// 7. Preferences endpoint sanity.
const prefs = await api(ca, 'GET', '/api/v1/notifications/preferences');
check('preferences list returns catalog', Array.isArray(prefs.json?.data) && prefs.json.data.length >= 5, `${prefs.json?.data?.length ?? 0} event types`);

// 8. Event types catalog (SuperAdmin manages).
const et = await api(sa, 'GET', '/api/v1/notifications/event-types');
check('event-types catalog accessible', Array.isArray(et.json?.data) && et.json.data.length >= 5, `${et.json?.data?.length ?? 0} types`);

// 9. CompanyAdmin permission boundary: CA must NOT read another company's subscriptions.
// (Demo Fleet company id is the CA's own; Freebuff Platform is the other one.)
const other = '45e3d99b-711e-4fd6-b2f6-ce3e55ef1593';
const denied = await api(ca, 'GET', `/api/v1/companies/${other}/alert-subscriptions`);
check('CA cannot read another company subscriptions', denied.status === 403 || denied.status === 401 || (denied.json?.success === false), `status ${denied.status}`);

const failed = checks.filter(c => !c.ok);
console.log(`\n${checks.length - failed.length}/${checks.length} checks passed`);
process.exit(failed.length ? 1 : 0);