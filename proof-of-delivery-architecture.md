# Proof of Delivery — architecture notes

How the POD feature is structured after the extraction pass. Build with these
boundaries, not against them.

## Layers and ownership

```
HTTP surface      ProofOfDeliveryController   (src/Freebuff.Platform.Api/Controllers)
                  - one route block per POD action, all under api/v1/trips/{id}/…
                  - ONE tenant guard (OwnsTripAsync) shared by all six endpoints;
                    no endpoint re-derives ownership
                  - permission pair: pod.view (viewing), pod.create (capture)
                  - stays thin: ownership + DTO mapping only

Policy + storage  ProofOfDeliveryService      (Infrastructure/Services)
                  - capture rules (one active record per waypoint+type),
                    OTP hashing/verification/expiry, location-mismatch flagging
                  - assumes the controller already verified ownership

Rule (shared)     ProofOfDelivery.HasVerifiedEvidenceExpr  (Domain/Entities)
                  - THE single definition of "evidence complete for its type"
                  - used by: entity IsVerified property, service
                    HasVerifiedEvidenceAsync, TripLifecycleService arrival gate
                  - change the rule in ONE place; never inline a copy

Arrival gate      TripLifecycleService.RecordWaypointArrivalAsync
                  - delivery waypoints optionally require verified evidence
                  - policy: company Configuration row
                    (fleet.require_pod_for_delivery) with per-trip override
                    (Trip.RequirePodForDelivery); gate only reports, never mutates

UI (frontend)
  WaypointPodPanel   one owner per waypoint's evidence: fetches its own records,
                     owns the capture modal + capture button + evidence render
  PodCaptureModal    signature pad (strokes → SVG), photo (image reference),
                     OTP issue/verify; attaches browser geolocation
  PodEvidence        auth-free, data-driven evidence viewer — the SAME component
                     serves the admin trip detail and the future customer
                     tracking link (no auth/tenant logic inside)
```

## State owners (one owner each)

| State | Owner |
|---|---|
| POD records for a waypoint | `WaypointPodPanel` (fetches `/trips/{id}/waypoints/{wpId}/pod`) |
| Capture modal open/closed | `WaypointPodPanel` |
| Evidence-verified rule | `ProofOfDelivery.HasVerifiedEvidenceExpr` |
| Trip detail + live + status | `TripModal` (Trips.tsx) — **not** POD records |

## Data flow

- `WaypointPodPanel` ← `TripModal` via props: tripId, waypoint facts, permissions
  (`podView`/`podCreate`), trip status, `refreshTick`.
- `TripModal` bumps `refreshTick` on every view-mode refresh; panels re-fetch
  their own evidence — the only cross-component signal, so there is no second
  polling loop and no evidence state held above the panel.
- Capture flows `panel → PodCaptureModal → API → panel.setRecords`; the panel
  prepends the returned record (no full refetch needed after its own capture).

## Conventions to keep

- New POD endpoints go in `ProofOfDeliveryController`, not TripsController.
  TripsController owns trip lifecycle (status, waypoints, zones, live/replay);
  anything evidence-shaped is the POD controller's.
- Permission codes are `pod.view` / `pod.create` (+ the standard 6-action set
  minted from PageRegistry key `pod`).
- Captured photo is a stored *reference*; data URL is the dev-mode stand-in for
  object storage — keep the column as a reference, never inline binaries.
- OTP: only the SHA-256 hash is stored; the plaintext code is dev-relayed only.
- Location mismatch is a data-quality/fraud flag, never a hard block.