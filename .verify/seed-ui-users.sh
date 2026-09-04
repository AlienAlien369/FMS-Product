#!/usr/bin/env bash
# Seeds restricted roles + users into the LOCAL dev API (port 8080) through the
# app's own API surface, so the live-preview UI leg can be driven as a
# restricted user. Idempotent: skips users that already exist.
set -euo pipefail
BASE=${BASE:-http://localhost:8080/api/v1}
HERE="$(cd "$(dirname "$0")" && pwd)"

TOKEN=$(curl -s -X POST "$BASE/auth/login" -H "Content-Type: application/json" \
  -d '{"email":"admin@demofleet.com","password":"Admin@123"}' \
  | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>console.log(JSON.parse(d).data.token))")

curl -s "$BASE/permissions?pageSize=500" -H "Authorization: Bearer $TOKEN" > "$HERE/perms.json"

# Fleet Only: full 6-action on dashboard + fleet pages, NOTHING on organization.
FLEET_IDS=$(node -e "
const d = require('./.verify/perms.json').data.items;
const byCode = Object.fromEntries(d.map(p => [p.code, p.id]));
const ids = ['dashboard','vehicle','driver','geofence','route']
  .flatMap(k => ['view','create','update','delete','export','import'].map(a => byCode[k+'.'+a]));
console.log(JSON.stringify(ids));
")

# Read Only: view-only on all 10 live pages.
VIEW_IDS=$(node -e "
const d = require('./.verify/perms.json').data.items;
const byCode = Object.fromEntries(d.map(p => [p.code, p.id]));
const ids = ['dashboard','vehicle','driver','geofence','route','company','user','role','localization','settings']
  .map(k => byCode[k+'.view']);
console.log(JSON.stringify(ids));
")

create_role() { # name permission_ids_json
  curl -s -X POST "$BASE/roles" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"name\":\"$1\",\"description\":\"UI verification role\",\"permissionIds\":$2}"
}

create_user() { # email role_id
  curl -s -X POST "$BASE/users" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"email\":\"$1\",\"password\":\"Admin@123\",\"firstName\":\"UI\",\"lastName\":\"Tester\",\"roleIds\":[\"$2\"]}"
}

FLEET_ROLE_ID=$(create_role "Fleet Only (UI)" "$FLEET_IDS" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{const j=JSON.parse(d);console.log(j.data&&j.data.id?j.data.id:'')})")
VIEW_ROLE_ID=$(create_role "Read Only (UI)" "$VIEW_IDS" | node -e "let d='';process.stdin.on('data',c=>d+=c).on('end',()=>{const j=JSON.parse(d);console.log(j.data&&j.data.id?j.data.id:'')})")

if [ -n "$FLEET_ROLE_ID" ]; then
  echo "fleetonly role: $FLEET_ROLE_ID"
  create_user "fleetonly@ui.test" "$FLEET_ROLE_ID" >/dev/null
fi
if [ -n "$VIEW_ROLE_ID" ]; then
  echo "readonly role: $VIEW_ROLE_ID"
  create_user "readonly@ui.test" "$VIEW_ROLE_ID" >/dev/null
fi
echo "done"