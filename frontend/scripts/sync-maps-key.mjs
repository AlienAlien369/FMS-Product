#!/usr/bin/env node
/**
 * sync-maps-key.mjs — dev-only helper (runs via the `predev` npm hook).
 *
 * If frontend/.env.local does not already have a VITE_GOOGLE_MAPS_API_KEY,
 * pull the key out of the deployed Vercel production bundle and write it
 * there, so local dev uses the same Maps key as production without anyone
 * pasting secrets around. Fails soft (exit 0): an offline machine or a
 * changed bundle simply keeps the keyless fallback.
 *
 * The key is extracted by regex over the minified JS. Google Maps JS API
 * keys are AIzaSy + 33 chars; we prefer a template-literal assignment
 * (minifiers emit `KEY_VAR=`AIza...``) and fall back to the first bare key
 * literal in the bundle.
 */
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const ENV_PATH = join(dirname(fileURLToPath(import.meta.url)), '..', '.env.local');
const PROD_URL = process.env.FMS_PROD_URL || 'https://fms-product.vercel.app';
const KEY_RE = /AIzaSy[A-Za-z0-9_-]{33}/;
const ASSIGN_RE = /=\x60(AIzaSy[A-Za-z0-9_-]{33})\x60/;

function readEnv() {
  try {
    return existsSync(ENV_PATH) ? readFileSync(ENV_PATH, 'utf8') : '';
  } catch {
    return '';
  }
}

function currentKey(env) {
  const m = env.match(/^\s*VITE_GOOGLE_MAPS_API_KEY\s*=\s*([^\r\n]*)/m);
  return m ? m[1].trim() : '';
}

async function fetchKey() {
  const html = await (await fetch(PROD_URL + '/', { signal: AbortSignal.timeout(15000) })).text();
  const asset = html.match(/assets\/[^"' ]+\.js/)?.[0];
  if (!asset) return null;
  const js = await (await fetch(PROD_URL + '/' + asset, { signal: AbortSignal.timeout(30000) })).text();
  const assigned = js.match(ASSIGN_RE);
  const any = js.match(KEY_RE);
  const key = (assigned?.[1] ?? any?.[0] ?? '').trim();
  return key.length === 39 ? key : null;
}

const env = readEnv();
if (currentKey(env)) {
  console.log('[sync-maps-key] VITE_GOOGLE_MAPS_API_KEY already set in .env.local — skipping.');
  process.exit(0);
}

let key = null;
try {
  key = await fetchKey();
} catch (e) {
  console.warn('[sync-maps-key] Could not reach the production bundle (' + e.message + ') — map panes will keep the keyless fallback.');
  process.exit(0);
}

if (!key) {
  console.warn('[sync-maps-key] Could not extract a Maps key from the production build — map panes will keep the keyless fallback.');
  process.exit(0);
}

const eol = env.includes('\r\n') ? '\r\n' : '\n';
const lines = env.split(/\r?\n/);
let replaced = false;
const out = lines
  .map((l) => {
    if (!replaced && /^\s*VITE_GOOGLE_MAPS_API_KEY\s*=/.test(l)) {
      replaced = true;
      return 'VITE_GOOGLE_MAPS_API_KEY=' + key;
    }
    return l;
  })
  .join(eol);
const final = replaced
  ? out
  : (env.trimEnd() ? env.trimEnd() + eol : '') + 'VITE_GOOGLE_MAPS_API_KEY=' + key + eol;
writeFileSync(ENV_PATH, final);
console.log('[sync-maps-key] Wrote VITE_GOOGLE_MAPS_API_KEY to .env.local (from ' + PROD_URL + '). Restart the dev server if it was already running.');