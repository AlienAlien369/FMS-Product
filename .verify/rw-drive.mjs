// Real-width responsive drive (no new toolchain: Node 22 global WebSocket + CDP).
// Verifies the mobile pass at 375 / 390 / 768 / 1024 against the local dev stack
// (vite :5173 -> api :8080), logged in as the seeded SuperAdmin (cross-tenant, so
// the company-scope selector is exercised too).
// Run:  node .verify/rw-drive.mjs
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const CHROME = process.env.CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const API = process.env.API || 'http://localhost:8080';
const APP = process.env.APP || 'http://localhost:5173';
const EMAIL = process.env.EMAIL || 'admin@freebuff.com';
const PASS = process.env.PASSWORD || 'Admin@123';
const DPORT = Number(process.env.DPORT || 9333);

const sleep = (ms) => new Promise(r => setTimeout(r, ms));
let failures = 0; const failed = [];
function check(label, ok, detail) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}${detail ? ` — ${detail}` : ''}`);
  if (!ok) { failures++; failed.push(label); }
}
async function waitHttp(url, tries = 40) {
  for (let i = 0; i < tries; i++) {
    try { const r = await fetch(url); if (r.ok) return; } catch {}
    await sleep(250);
  }
  throw new Error(`timed out waiting for ${url}`);
}

// ── 1. Login via API (dev) ────────────────────────────────────────────────
const lr = await fetch(`${API}/api/v1/auth/login`, {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: EMAIL, password: PASS }),
});
const lj = await lr.json();
if (!lj.success) throw new Error(`login failed: ${JSON.stringify(lj)}`);
const { token, refreshToken, user } = lj.data;
console.log(`logged in as ${EMAIL} (roles: ${(user?.roles || []).join(',')})`);

// ── 2. Launch headless Chrome ─────────────────────────────────────────────
const profile = join(tmpdir(), `rw-drive-${Date.now()}`);
const proc = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${DPORT}`, `--user-data-dir=${profile}`,
  '--no-first-run', '--no-default-browser-check', '--hide-scrollbars',
  '--window-size=1280,900', `${APP}/login`,
], { stdio: 'ignore' });
try {
  await waitHttp(`http://127.0.0.1:${DPORT}/json/version`);
  const targets = await (await fetch(`http://127.0.0.1:${DPORT}/json/list`)).json();
  const page = targets.find(t => t.type === 'page');
  if (!page) throw new Error('no page target');
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(res => { const i = ++id; pending.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  const ev = async (expr) => {
    const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.result?.exceptionDetails) return `EXC: ${r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text}`;
    return r.result?.result?.value;
  };
  const nav = async (url, settle = 3500) => { await send('Page.navigate', { url }); await sleep(settle); };

  // Seed auth then boot the app
  await nav(`${APP}/`, 1500);
  await ev(`localStorage.setItem('token', ${JSON.stringify(token)});
    localStorage.setItem('refreshToken', ${JSON.stringify(refreshToken || '')});
    localStorage.setItem('user', ${JSON.stringify(JSON.stringify(user))});
    'seeded'`);
  await nav(`${APP}/trips`, 5000);

  const WIDTHS = (process.env.WIDTHS || '375,390,768,1024').split(',').map(Number);
  mkdirSync('.verify/shots', { recursive: true });

  for (const w of WIDTHS) {
    console.log(`\n═══ width ${w}px ═══`);
    await send('Emulation.setDeviceMetricsOverride', { width: w, height: 800, deviceScaleFactor: 1, mobile: false });
    await nav(`${APP}/trips`, 4500);
    const iw = await ev('window.innerWidth');
    check(`[${w}] viewport applied`, iw === w, `innerWidth=${iw}`);

    // ── Navigation / drawer ──
    const hamburger = await ev(`(() => { const b = document.querySelector('button[aria-label="Open menu"]'); return b ? getComputedStyle(b).display !== 'none' : false; })()`);
    check(`[${w}] hamburger ${w < 1024 ? 'visible' : 'hidden'}`, hamburger === (w < 1024), `shown=${hamburger}`);
    if (w < 1024) {
      const drawer = await ev(`(() => { const b = document.querySelector('button[aria-label="Open menu"]'); b.click();
        return new Promise(r => setTimeout(() => {
          const locked = document.body.style.overflow === 'hidden';
          const asideIn = !document.querySelector('aside').className.includes('-translate-x-full');
          document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
          setTimeout(() => r({ locked, asideIn, unlocked: document.body.style.overflow === '' }), 100);
        }, 150)); })()`);
      check(`[${w}] drawer opens with body scroll lock`, drawer.locked && drawer.asideIn, JSON.stringify(drawer));
      check(`[${w}] Escape closes + unlocks scroll`, drawer.unlocked === true, `unlocked=${drawer.unlocked}`);
    } else {
      const aside = await ev(`getComputedStyle(document.querySelector('aside')).position`);
      check(`[${w}] sidebar is static (persistent)`, aside === 'static', `position=${aside}`);
    }

    // ── Table vs stacked cards ──
    const tableOn = await ev(`(() => { const t = document.querySelector('table'); if (!t) return false; let a = t; while (a && !a.className.includes('md:block')) a = a.parentElement; return a ? getComputedStyle(a).display !== 'none' : false; })()`);
    check(`[${w}] desktop table ${w >= 768 ? 'visible' : 'hidden'}`, tableOn === (w >= 768), `tableOn=${tableOn}`);
    const cardsOn = await ev(`(() => { const c = document.querySelector('[class~="md:hidden"]'); return c ? getComputedStyle(c).display !== 'none' : false; })()`);
    check(`[${w}] stacked cards ${w < 768 ? 'visible' : 'hidden'}`, cardsOn === (w < 768), `cardsOn=${cardsOn}`);

    // ── Bell: dropdown (>=640) vs bottom sheet (<640) ──
    const bell = await ev(`(() => { const b = document.querySelector('button[aria-label="Notifications"]'); if (!b) return 'no bell'; b.click();
      return new Promise(r => setTimeout(() => {
        const p = document.querySelector('[class~="sm:w-96"]'); if (!p) return r('no panel');
        const s = getComputedStyle(p); const rc = p.getBoundingClientRect();
        const out = { pos: s.position, w: Math.round(rc.width), h: Math.round(rc.height), bottom: Math.round(rc.bottom), vw: window.innerWidth, vh: window.innerHeight };
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));          setTimeout(() => r({ ...out, closed: !document.querySelector('[class~="sm:w-96"]') }), 100);
      }, 400)); })()`);
    if (typeof bell === 'object') {
      if (w < 640) {
        check(`[${w}] bell is a fixed bottom sheet`, bell.pos === 'fixed' && bell.w === w && bell.bottom === bell.vh, JSON.stringify(bell));
      } else {
        check(`[${w}] bell is an anchored dropdown with content`, bell.pos === 'absolute' && bell.h > 100, JSON.stringify(bell));
      }
      check(`[${w}] bell closes on Escape`, bell.closed === true);
    } else check(`[${w}] bell panel`, false, String(bell));

    // ── Company scope selector (SuperAdmin is cross-tenant) ──
    const scope = await ev(`(() => { const b = document.querySelector('button[title="View data for which companies?"]'); if (!b) return 'no scope';
      const labelHidden = getComputedStyle(b.querySelector('span')).display === 'none';
      b.click();
      return new Promise(r => setTimeout(() => {
        const p = document.querySelector('[class~="sm:w-72"]'); if (!p) return r({ labelHidden, panel: 'none' });
        const s = getComputedStyle(p); const rc = p.getBoundingClientRect();
        const out = { labelHidden, pos: s.position, w: Math.round(rc.width), bottom: Math.round(rc.bottom), vw: window.innerWidth, vh: window.innerHeight };
        document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        setTimeout(() => r({ ...out, closed: !document.querySelector('[class~="sm:w-72"]') }), 100);
      }, 400)); })()`);
    if (typeof scope === 'object' && !('panel' in scope)) {
      if (w < 640) {
        check(`[${w}] scope label icon-only on mobile`, scope.labelHidden === true);
        check(`[${w}] scope is a fixed bottom sheet`, scope.pos === 'fixed' && scope.w === w && scope.bottom === scope.vh, JSON.stringify(scope));
      } else {
        check(`[${w}] scope label visible on desktop`, scope.labelHidden === false);
        check(`[${w}] scope is an anchored dropdown`, scope.pos === 'absolute', JSON.stringify(scope));
      }
      check(`[${w}] scope closes on Escape`, scope.closed === true);
    } else check(`[${w}] scope selector`, false, String(scope));

    // ── Overflow menu (card view only, <768): RBAC + status rule + not clipped ──
    // Edit is product-gated to status 0 (Draft) / 1 (Scheduled); an In Progress
    // (2) card must NOT expose Edit. Assert both sides so the mobile layout
    // preserves RBAC narrowing exactly.
    if (w < 768) {
      const openMenu = `(card) => { if (!card) return null; card.scrollIntoView({ block: 'center' });
        // OverflowMenu closes itself on scroll (menu would be misplaced); settle
        // after scrollIntoView BEFORE clicking, or the open menu vanishes.
        return new Promise(r => setTimeout(() => {
          const b = card.querySelector('button[aria-label="Row actions"]'); if (!b) return r(null);
          b.click();
          setTimeout(() => {
            const m = document.querySelector('div.fixed.z-30.w-44'); if (!m) return r(null);
            const rc = m.getBoundingClientRect();
            const items = [...m.querySelectorAll('button')].map(x => x.textContent.trim());
            const inside = rc.top >= 0 && rc.bottom <= window.innerHeight && rc.right <= window.innerWidth && rc.left >= 0;
            const rect = [Math.round(rc.top), Math.round(rc.bottom), Math.round(rc.left), Math.round(rc.right)];
            document.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
            setTimeout(() => r({ items, inside, rect }), 80);
          }, 300);
        }, 350)); }`;
      const menu = await ev(`(() => { const cards = [...document.querySelectorAll('[class~="md:hidden"] .rounded-xl')];
        const card = cards.find(c => /Draft|Scheduled/.test(c.innerText.slice(0, 200)));
        return (${openMenu})(card); })()`);
      if (menu) {
        check(`[${w}] overflow menu entirely inside viewport (not clipped)`, menu.inside, JSON.stringify(menu.rect));
        const want = ['View / Track', 'Edit', 'Delete'];
        check(`[${w}] Draft/Scheduled card menu has exactly the RBAC-permitted actions`, JSON.stringify(menu.items) === JSON.stringify(want), menu.items.join(' | '));
      } else check(`[${w}] overflow menu (editable card)`, false, 'no Draft/Scheduled card or menu');
      const neg = await ev(`(() => { const cards = [...document.querySelectorAll('[class~="md:hidden"] .rounded-xl')];
        const card = cards.find(c => /In Progress/.test(c.innerText.slice(0, 200)));
        return (${openMenu})(card); })()`);
      if (neg) {
        check(`[${w}] In Progress card menu hides Edit (status rule holds)`, !neg.items.includes('Edit') && neg.items.includes('Delete'), neg.items.join(' | '));
      } else check(`[${w}] overflow menu (in-progress card)`, false, 'no In Progress card');
    } else {
      const icons = await ev(`(() => {
        const rows = [...document.querySelectorAll('tbody tr')];
        const btns = r => r ? r.querySelectorAll('button').length : -1;
        const hasEdit = r => r ? !!r.querySelector('button[title="Edit"]') : null;
        const editRow = rows.find(r => /Draft|Scheduled/.test(r.innerText.slice(0, 200)));
        const ipRow = rows.find(r => /In Progress/.test(r.innerText.slice(0, 200)));
        return JSON.stringify({ editRow: btns(editRow), editHasEdit: hasEdit(editRow), ipRow: btns(ipRow), ipHasEdit: hasEdit(ipRow) });
      })()`);
      const i = JSON.parse(icons || '{}');
      check(`[${w}] Draft/Scheduled row has Edit + Delete (RBAC intact)`, i.editRow >= 3 && i.editHasEdit === true, `editRow=${i.editRow}`);
      check(`[${w}] In Progress row hides Edit (status rule holds)`, i.ipRow >= 2 && i.ipHasEdit === false, `ipRow=${i.ipRow}`);
    }

    // ── Trip modal: full-screen sheet (<768) vs centered dialog (>=768) ──
    const modal = await ev(`(() => {
      const open = () => { const b = document.querySelector('button[aria-label="Row actions"], tbody button'); if (!b) return 'no trigger'; b.click(); return null; };
      const clickView = () => new Promise(res => setTimeout(() => {
        const m = document.querySelector('div.fixed.z-30.w-44'); if (m) { const v = [...m.querySelectorAll('button')].find(x => x.textContent.includes('View')); if (v) v.click(); }
        res(null);
      }, 250));
      const first = open(); if (first) return first;
      return clickView().then(() => new Promise(res => setTimeout(() => {
        const overlay = [...document.querySelectorAll('div.fixed.inset-0')].find(o => o.querySelector('div.bg-white') && (o.querySelector('h2, h3') || o.querySelector('div.max-w-3xl')));
        const panel = overlay?.querySelector('div.bg-white');
        if (!panel) return res('no modal panel');
        const rc = panel.getBoundingClientRect();
        const out = { w: Math.round(rc.width), vw: window.innerWidth, rounded: getComputedStyle(panel).borderRadius };
        const close = [...panel.querySelectorAll('button')].find(x => x.getAttribute('aria-label') === 'Close' || x.textContent.trim() === 'Close');
        if (close) close.click();
        setTimeout(() => res({ ...out, closed: !overlay.isConnected }), 250);
      }, 600)));
    })()`);
    if (typeof modal === 'object') {
      if (w < 768) check(`[${w}] trip modal is a full-screen sheet`, modal.w === modal.vw, JSON.stringify(modal));
      else check(`[${w}] trip modal is a centered dialog`, modal.w <= 768 && modal.rounded !== '0px', JSON.stringify(modal));
      check(`[${w}] trip modal has a reachable close`, modal.closed !== false);
    } else check(`[${w}] trip modal`, false, String(modal));

    // Screenshot
    const shot = await send('Page.captureScreenshot', { format: 'png' });
    if (shot?.result?.data) writeFileSync(`.verify/shots/rw-${w}.png`, Buffer.from(shot.result.data, 'base64'));
    console.log(`      screenshot -> .verify/shots/rw-${w}.png`);
  }

  // ── Dashboard reflow (stat grid + charts) at each width ──
  for (const w of WIDTHS) {
    await send('Emulation.setDeviceMetricsOverride', { width: w, height: 800, deviceScaleFactor: 1, mobile: false });
    await nav(`${APP}/`, 4500);
    const dash = await ev(`(() => {
      const main = document.querySelector('main'); if (!main) return 'no main';
      const grid = main.querySelector('.grid'); if (!grid) return 'no grid';
      const cols = getComputedStyle(grid).gridTemplateColumns.split(' ').length;
      const charts = [...document.querySelectorAll('.recharts-responsive-container')].map(g => Math.round(g.getBoundingClientRect().width));
      const mw = Math.round(main.getBoundingClientRect().width);
      return { cols, chartCount: charts.length, chartsFit: charts.every(x => x <= mw + 2), mainW: mw };
    })()`);
    const expectedCols = w < 640 ? 1 : w < 1024 ? 2 : 3;
    check(`[${w}] dashboard stat grid ${expectedCols}-across`, typeof dash === 'object' && dash.cols === expectedCols, JSON.stringify(dash));
    check(`[${w}] dashboard charts fit container`, typeof dash === 'object' && dash.chartsFit, JSON.stringify(dash));
  }

  // ── Geofences: map-over-form stacking inside the modal at 390 ──
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 800, deviceScaleFactor: 1, mobile: false });
  await nav(`${APP}/geofences`, 4500);
  const gf = await ev(`(() => { const add = [...document.querySelectorAll('button')].find(b => b.textContent.includes('Add Geofence')); if (!add) return 'no add btn'; add.click();
    return new Promise(r => setTimeout(() => {
      const overlay = [...document.querySelectorAll('div.fixed.inset-0')].find(o => o.querySelector('div.bg-white'));
      const panel = overlay?.querySelector('div.bg-white'); if (!panel) return r('no panel');
      const drawTab = [...panel.querySelectorAll('button')].find(b => b.textContent.includes('Draw on Map'));
      if (drawTab) drawTab.click();
      setTimeout(() => {
        const pane = panel.querySelector('[style*="380"]');
        const pRect = panel.getBoundingClientRect(); const paneRect = pane ? pane.getBoundingClientRect() : null;
        const paneInView = paneRect ? paneRect.left >= 0 && paneRect.right <= window.innerWidth && paneRect.width > 100 : null;
        const noHScroll = document.documentElement.scrollWidth <= window.innerWidth;
        const close = [...panel.querySelectorAll('button')].find(b => b.textContent.trim() === 'Close'); if (close) close.click();
        r({ sheet: pRect.width === window.innerWidth, paneInView, noHScroll, scrollable: getComputedStyle(panel).overflowY, paneFound: !!pane });
      }, 500);
    }, 700)); })()`);
  if (typeof gf === 'object') {
    check('[390] geofence modal is a full-screen sheet', gf.sheet === true, JSON.stringify(gf));
    check('[390] map pane renders inside the viewport (no overflow)', gf.paneInView === true, JSON.stringify(gf));
    check('[390] no page-level horizontal scroll', gf.noHScroll === true, JSON.stringify(gf));
  } else check('[390] geofence modal', false, String(gf));

  // ── 44px touch-target sweep: every button below md must be ≥44px tall ──
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 800, deviceScaleFactor: 1, mobile: false });
  const small = new Map();
  for (const page of ['trips', 'vehicles', 'geofences']) {
    await nav(`${APP}/${page}`, 4000);
    const res = await ev(`(() => {
      const out = [];
      document.querySelectorAll('button').forEach(b => {
        const r = b.getBoundingClientRect();
        if (r.height > 0 && r.height < 44) out.push({ t: (b.textContent || b.getAttribute('aria-label') || b.title || '').trim().slice(0, 24), h: Math.round(r.height), on: (b.closest('[class~="md:hidden"]') || !b.closest('table')) ? 'mobile' : 'table' });
      });
      return out;
    })()`);
    for (const s of res || []) {
      const k = `${page}:${s.t}`;
      if (!small.has(k) || s.h < small.get(k).h) small.set(k, s.h);
    }
  }
  const offenders = [...small.entries()].filter(([, h]) => h < 44);
  check('[390] every button is ≥44px tall (touch-target sweep)', offenders.length === 0,
    offenders.length ? offenders.map(([k, h]) => `${k}=${h}px`).join('; ') : 'no sub-44px buttons on trips/vehicles/geofences');

  ws.close();
  console.log(`\n${failures === 0 ? 'ALL GREEN' : `${failures} FAILURE(S):\n  ${failed.join('\n  ')}`}`);
  process.exit(failures === 0 ? 0 : 1);
} finally {
  try { proc.kill(); } catch {}
  await sleep(300);
}