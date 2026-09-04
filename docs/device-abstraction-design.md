# Device Abstraction Layer — Design (review before build)

Status: **Design for review — no code written yet.**
Scope: a vendor-agnostic Device model + pluggable vendor adapters + normalized telemetry ingestion, replacing the current single-device-per-vehicle fields on `Vehicle`.

---

## 1. Current state (grounded in this repo)

The design must migrate *from* the code that exists today:

- **Single device embedded on `Vehicle`** (`src/Freebuff.Platform.Domain/Entities/Vehicle.cs`): `DeviceImei`, `DeviceType` (free string), `DeviceSerialNumber`, plus denormalized last-known telemetry (`LastLatitude/Longitude/Speed/Heading/LocationUpdate`, `IgnitionStatus`, `OdometerReading`, `EngineHours`). The DTOs (`VehicleDtos.cs`) and the frontend (`Vehicles.tsx`, `api.ts`) carry these same fields inline on vehicle create/edit. One vehicle = one device; no SIM, no vendor, no history.
- **No ingestion service exists.** There is no TCP/UDP/HTTP/MQTT receiver, no payload parser, no device-registration flow. Last-known telemetry is seeded demo data written directly to `Vehicle`.
- **Deployed backend is the monolith** `Freebuff.Platform.Api` (port 8080, one Postgres DB, `ApplicationDbContext` maps every Domain entity). A scaffolded `Freebuff.Platform.Api.Fleet` microservice exists with its own `FleetDbContext`/database (`freebuff_fleet`, `EnsureCreatedAsync`) and mirrors the same Domain entities (Vehicle/Driver/Trip/Geofence/Client). It is wired only into `docker-compose.microservices.yml` — an aspirational split, **not** the deployed path. Both contexts must map any new entity so they cannot drift.
- **Conventions to reuse:** entities in `Freebuff.Platform.Domain` inherit `BaseEntity` (TenantId, Created/Updated/audit, soft-delete `IsDeleted/DeletedAt/…`, optimistic `Version`, domain events). EF configs are `IEntityTypeConfiguration<T>` classes in `EntityConfigurations.cs`, applied by `ApplicationDbContext`. Post-initial-release tables are added to existing DBs via idempotent DDL in `SchemaBootstrap.cs` (because `EnsureCreatedAsync` is a no-op on existing databases). Filtered unique indexes + `HasQueryFilter(!IsDeleted)` are the established pattern.
- Platform-wide lookup data (Languages, Currencies, Modules, Pages) already exists as platform-level rows (TenantId null / no tenant filter); the vendor catalog should follow that precedent.

---

## 2. Target domain model

Five new aggregates/entities. All in `Freebuff.Platform.Domain.Entities`, tenant-scoped except `DeviceVendor` (platform-global catalog, like Language/Module).

```
DeviceVendor 1──* Device 1──* DeviceSim
                     * ──── *  Vehicle          (via VehicleDevice: role, from/to)
Device 1──* TelemetryEvent ── 1 RawPayload      (optional raw archive)
Vehicle 1──1 TelemetryState                     (materialized last-known, replaces Vehicle.Last*)
```

### 2.1 DeviceVendor — catalog + runtime registry metadata
Platform-level (TenantId null), seeded like Languages/Currencies.

| Field | Notes |
|---|---|
| Id | Guid |
| Code | unique stable key, e.g. `pictor`, `itriangle`, `streamax` — matches the adapter's `VendorCode` |
| Name | display name |
| AdapterVersion | e.g. `"1.2.0"` — contract version this vendor row expects |
| ProtocolType | enum: `TcpRaw \| Udp \| HttpWebhook \| Mqtt` (its *transport*) |
| PayloadFormat | string, e.g. `"pictor-binary-v2"`, `"jt808"`, `"json-webhook"` |
| Status | Active/Inactive |
| ListenerConfig | JSON — how this vendor is reached (port to bind, webhook path, MQTT topic prefix). Used at startup to bind receivers. |
| Capabilities | JSON — which canonical fields the vendor can produce (GPS, ignition, fuel, temperature, driver-ID…) for UI hints and validation |
| Metadata | JSON — per-vendor extra catalog attributes |

New vendor = new row (seeded or via a SuperAdmin "Vendor" screen later).

### 2.2 Device
Tenant-scoped. Identity is decoupled from the vendor so the same physical tracker keeps one row if re-vendored.

| Field | Notes |
|---|---|
| Id, TenantId, BaseEntity audit/soft-delete | |
| VendorId (FK) | nullable = "Unknown/unidentified" (legacy rows, see §8) |
| DeviceType | enum: `GpsTracker, Dashcam, Adas, FuelSensor, TemperatureSensor, DualCamera, Other` |
| DeviceTypeOverride | string — original free-text value kept during migration |
| IdentityType | enum: `Imei \| Serial \| Mac \| PhoneNumber` |
| IdentityValue | the identifier the device transmits (IMEI is globally unique; serial unique per vendor) |
| Model, FirmwareVersion | |
| Status | enum: `Active, Inactive, Retired, Lost, AwaitingVendor` |
| InstallDate, ActivatedAt, DeactivatedAt, LastSeenAt | |
| RawMetadata | JSON — vendor-specific extra attributes that don't fit the common schema (no schema migration per vendor quirk) |
| CompanyId denormalized for tenant queries | derived from the owning company (device may be "owned" before install) |

Unique constraint: `(CompanyId, IdentityType, IdentityValue)` where `IsDeleted = false` — one device per identifier per tenant. No `VehicleId` on Device: the vehicle link lives on the assignment (a device can move vehicles, or be stock before install).

### 2.3 DeviceSim — one-to-many from Device
| Field | Notes |
|---|---|
| Id, TenantId | |
| DeviceId (FK) | |
| SimNumber / ICCID | |
| PhoneNumber (MSISDN) | |
| Carrier | |
| Status | `Active, Failover, Blocked, Retired` |
| IsPrimary | filtered unique: one active primary per device |
| ActivatedAt / DeactivatedAt | |
| RawMetadata JSON | APN, IP, data plan, roaming flags |

### 2.4 VehicleDevice — the many-to-many assignment
What replaces the embedded `DeviceImei` fields and makes Vehicle ↔ Device M:N with history.

| Field | Notes |
|---|---|
| Id, TenantId | |
| VehicleId (FK), DeviceId (FK) | |
| Role | enum: `PrimaryTracker, SecondaryTracker, Dashcam, Adas, FuelSensor, TemperatureSensor, Spare` |
| AssignedFrom, AssignedTo (nullable) | history; active = AssignedTo null |
| RawMetadata JSON | e.g. mounting position, sensor channel config for that vehicle |

Constraint: **one active PrimaryTracker per vehicle** and **one active row per (Vehicle, Role)** — filtered unique indexes (`WHERE IsDeleted = false AND "AssignedTo" IS NULL`). Reading a device's "current vehicle" = active assignment lookup.

### 2.5 TelemetryEvent — the normalized, vendor-agnostic stream (high volume)
| Field | Notes |
|---|---|
| Id (Guid), TenantId | |
| DeviceId (FK, indexed) | |
| VehicleId (nullable, denormalized at write from active assignment — avoids a join on hot queries) | |
| ReceivedAtUtc, EventTimeUtc | device clock if trustworthy, else receive time |
| Position: Lat, Lon, AltitudeM, SpeedKmh, HeadingDeg, Satellites, Hdop | first-class columns (every consumer needs them) |
| Ignition (bool?), EngineOn (bool?) | |
| FuelLevelPercent, FuelLevelLiters | nullable; fuel sensor present only on capable devices |
| OdometerKm, EngineHours, BatteryVoltage | nullable |
| DriverCardId | optional (scanned / driver-ID field) |
| AlertsJson | canonical alert codes array — normalized by the adapter |
| SensorsJson | per-channel readings the schema hasn't promoted yet (e.g. `{"temp1": 23.1, "temp2": 24.0}`), so a new sensor type needs **no migration** until it needs indexed querying |
| ExtrasJson | anything else the adapter produced |
| RawPayloadId (FK, nullable) | link to the archived raw frame |

Design rule: promote to columns only what is queried/indexed or drives logic; everything else rides in JSON until a feature justifies a column. **No vendor fields ever live here** — the adapter is the only place vendor knowledge exists.

### 2.6 RawPayload — optional raw archive (debugging/replay)
| Field | Notes |
|---|---|
| Id, TenantId (if identifiable) | |
| VendorId, DeviceId (nullable — may be unidentifiable garbage) | |
| ReceivedAtUtc, Channel | endpoint/port/topic |
| Payload (bytea or text), ContentType | |
| ParseStatus | `Parsed, Unparsed, Failed` + `FailureReason` |

Written **async/batched** so it never blocks the ingest path. Retention job can purge later (partition by month). Disabled in production by default if storage is a concern; the normalized stream never depends on it.

### 2.7 TelemetryState — materialized last-known (replaces Vehicle.Last*)
`Vehicle.LastLatitude/Longitude/Speed/…` move here (keyed by VehicleId, updated on every accepted event, throttled write when values don't change beyond thresholds). Vehicle list/detail pages join TelemetryState. Keeps `Vehicle` a pure asset record and stops hot telemetry writes from dirtying the asset row. *(Decision fork C — see §9.)*

---

## 3. Adapter contract — the fixed interface every vendor implements

New project `Freebuff.Platform.Ingestion` (contracts + adapters + pipeline) referenced by the monolith now and by the fleet microservice later — the shared home so the aspirational split can't drift.

### 3.1 Vendor adapter (payload layer — stateless)
```csharp
public interface IVendorAdapter
{
    string VendorCode { get; }              // == DeviceVendor.Code ("pictor", …)
    DeviceProtocolType ProtocolType { get; } // transport the vendor uses
    string PayloadFormat { get; }            // e.g. "pictor-binary-v2"

    // Identify a device from a complete frame — used for auto-detection and
    // for the "which vendor is this?" suggestion helper at registration.
    bool TryExtractDeviceId(byte[] frame, out DeviceIdentity identity);

    bool Validate(byte[] frame, out string? error);       // cheap structural check
    ParseResult Parse(DeviceIdentity identity, byte[] frame, DateTime receivedAtUtc);
}

public abstract record ParseResult;
public sealed record ParseOk(NormalizedTelemetry Telemetry) : ParseResult;
public sealed record ParseRejected(string Reason) : ParseResult;   // logged, dead-lettered
public sealed record NeedsMoreData() : ParseResult;                // streamed/segmented protocols
```

### 3.2 Normalized telemetry — the universal output shape
```csharp
public sealed record NormalizedTelemetry
{
    public required DeviceIdentity Device;         // vendor code + identity value
    public DateTime? EventTimeUtc;                 // device clock when available
    public GeoPoint? Position;                     // lat/lon/altitude/hdop/satellites
    public double? SpeedKmh, HeadingDeg, BatteryVoltage;
    public bool? Ignition, EngineOn;
    public double? FuelLevelPercent, FuelLevelLiters, OdometerKm, EngineHours;
    public string? DriverCardId;
    public IReadOnlyList<string> Alerts = [];      // canonical alert codes ONLY
    public IReadOnlyDictionary<string, double> Sensors = {}; // temp channels, aux inputs…
    public IDictionary<string, object?> Extras = new Dictionary<string, object?>();
}
```
Every adapter translates its raw payload into this shape. Consumer code (trip builder, geofence engine, alerts, dashboards) reads only `NormalizedTelemetry`/`TelemetryEvent` — zero vendor branching.

### 3.3 Transport layer — separate axis (protocol ≠ payload format)
A vendor's *transport* and its *payload* are independent (a vendor can ship JSON over TCP *and* MQTT). So framing is a separate pluggable piece:
```csharp
public interface ITransportCodec          // one per protocol family, shared by vendors
{
    DeviceProtocolType ProtocolType { get; }
    // Incremental feed → complete frames; handles binary framing, JT808-style
    // segmentation, length prefixes, checksums. One codec serves many vendors.
    IEnumerable<byte[]> Frame(ReadOnlySpan<byte> chunk, ref FrameContext ctx);
}
```
Receiver bindings (see §5) are driven by `DeviceVendor.ListenerConfig` at startup: TCP/UDP port per vendor (or shared codec port + frame-level detection), HTTP route `/ingest/{vendorCode}`, MQTT topic prefix. Adding a vendor with an existing transport = config only.

### 3.4 Registry — no if/else, ever
```csharp
public interface IVendorAdapterRegistry
{
    IVendorAdapter? Get(string vendorCode);                    // by DB row Code
    IEnumerable<IVendorAdapter> ForTransport(DeviceProtocolType t);
}
```
Built by DI scan: adapters carry `[VendorAdapter("pictor")]`; the registry is populated once at startup and cross-checked against `DeviceVendor` rows (a row with no adapter class = surfaced config error at boot; an adapter with no row is inert until seeded). **New vendor = new class + new DB row. Core pipeline and business logic are untouched.**

---

## 4. Ingestion pipeline & where vendor detection happens

```
Transport receiver (bound from DeviceVendor.ListenerConfig)
   TCP/UDP per-vendor port | HTTP /ingest/{vendorCode} | MQTT topic prefix
        │  (shared port → frame-level detection as fallback)
        ▼
[1] Device auth      — per-device token / IMEI allow-list / webhook HMAC
        ▼
[2] Vendor resolve   — explicit: binding route/port/topic ⇒ vendorCode
                        inferred: adapter.TryExtractDeviceId ⇒ Device row ⇒ VendorId
        ▼
[3] ITransportCodec.Frame  (stream → complete frames)
        ▼
[4] IVendorAdapter.Parse   → ParseOk(NormalizedTelemetry) | Rejected | NeedsMoreData
        ▼
[5] Enrich            — Device lookup (cache): TenantId, active VehicleDevice ⇒ VehicleId
        ▼
[6] Persist (batched) — TelemetryEvent  •  TelemetryState update (throttled)
                         • RawPayload archive (async, optional)
        ▼
[7] Domain events     — alerts/geofence/trip consumers (later: RabbitMQ)
```

**Vendor detection happens at three distinct layers**, in priority order:
1. **Explicit by binding** — a TCP listener bound on vendor X's port, or the webhook path `/ingest/{vendorCode}`, or an MQTT topic under the vendor's prefix. Zero ambiguity; this is the primary path.
2. **Inferred by frame content** — when receivers are shared (one port, many vendors), `TryExtractDeviceId` pulls the IMEI/serial and the Device lookup returns the VendorId before parsing. Needed only for shared-port deployments.
3. **Never in business logic** — downstream consumers see only normalized data.

---

## 5. Vendor onboarding checklist (the "no core changes" claim, concretely)

To add vendor *Acme*:
1. Seed `DeviceVendor` row (Code `acme`, ProtocolType, ListenerConfig, Capabilities) — or via a future SuperAdmin screen.
2. Write `AcmeAdapter : IVendorAdapter` (extract-id, validate, parse) in the Ingestion project.
3. One registration line or the `[VendorAdapter("acme")]` attribute (auto-scanned).
4. Only if Acme uses a genuinely new transport: a new `ITransportCodec` implementation.
5. Only when a business feature needs an *indexed* Acme field: promote it to a `TelemetryEvent` column. Until then `SensorsJson`/`ExtrasJson` absorbs it.

Nothing in the ingestion pipeline, vehicle logic, DTOs, or UI is edited.

---

## 6. Data migration from the current model (idempotent, in `SchemaBootstrap` style)

The deployed DB is upgraded by idempotent DDL + a data backfill — never a destructive recreate:

1. Create the six new tables (fresh-DB shape mirrored, like `SchemaBootstrap.Additions`).
2. Backfill **Device**: for every vehicle with a `DeviceImei`, create one Device row
   (`CompanyId = Vehicle.CompanyId`, `IdentityType = Imei`, `IdentityValue = DeviceImei`,
   `DeviceType = map(DeviceType string) or Other` + `DeviceTypeOverride = original string`,
   `VendorId = null`, `Status = AwaitingVendor`).
3. Backfill **VehicleDevice**: active `PrimaryTracker` assignment for each such vehicle.
4. Backfill **TelemetryState** from `Vehicle.Last*` columns.
5. Keep the old `Vehicle` columns for one release (DTOs still serialize them from TelemetryState via the join) so rollback is trivial; a follow-up migration drops them and removes the fields from DTOs/UI.

No data is destroyed; existing device-strings are preserved verbatim in `DeviceTypeOverride`.

---

## 7. Trade-offs called out for review

1. **Vendor detection: explicit-at-registration vs auto-detect (the one you flagged).**
   - *Explicit (recommended as primary):* devices are provisioned into a `Device` row under a chosen vendor before first contact; first contact matches identity → Device → Vendor. Pros: zero ambiguity, works offline, immune to identifier-prefix collisions between vendors (IMEI TAC prefixes mostly disambiguate; *serials do not*), prevents a device impersonating a vendor. Cons: operator must know/choose the vendor at registration.
   - *Auto-detect only:* parse by trying adapters against the frame until one validates. Pros: zero-config first contact. Cons: you cannot tenant-scope or authorize an *unregistered* device anyway — so auto-detect cannot fully replace registration; and a wrong-guess parse can corrupt ordering (some protocols parse "successfully" as garbage). Prefix tables also need maintenance.
   - **Recommended hybrid:** explicit vendor is required on the Device row; a helper endpoint ("suggest vendor by IMEI/paste a sample frame") uses `TryExtractDeviceId` across adapters to *recommend* the vendor during registration. `Status = AwaitingVendor` parks legacy/unassigned devices so their traffic is rejected loudly rather than misparsed.
2. **Single canonical telemetry table vs JSON-only.** Promoted columns are worth it only for query/indexed fields; everything else stays JSON until a feature justifies a column (§2.5 rule). Reversible either way (JSON→column is a data backfill, not a redesign).
3. **Last-known telemetry: TelemetryState table (recommended) vs keeping `Vehicle.Last*`.** TelemetryState keeps hot writes off the asset row and supports multi-device (per-role last-known) later; costs one join on read. Keeping on Vehicle is simpler but forces telemetry to update the asset row and cannot represent multiple devices.
4. **Where ingestion lives: monolith vs the scaffolded fleet microservice.** Today only the monolith is deployed and nothing receives telemetry. Recommend: contracts/adapters/pipeline in `Freebuff.Platform.Ingestion`, hosted in the monolith now (hosted TCP listener + HTTP route under `/ingest`), with the fleet microservice able to host the same pipeline later without code changes. Building out fleet-service *now* (its own DB with cross-DB device↔vehicle lookups, RabbitMQ fan-out) is a much bigger step that the product has not needed yet.
5. **Raw archive default.** Retaining every raw frame costs storage and write volume; normalized data is what everything reads. Recommend archive off by default (toggle per vendor), on for early vendor integration/debugging.
6. **BaseEntity cost on TelemetryEvent.** Soft-delete + Version + audit on a high-volume append-only stream is overhead with no business value. Recommendation: TelemetryEvent/RawPayload use a lean row (Id, TenantId, timestamps) *without* soft-delete/concurrency columns, deliberately diverging from `BaseEntity` — call this out because it breaks the repo convention by design.

---

## 8. Out of scope (named, so future work is not mistaken for gaps)

- Trip/geofence/alert *consumers* of TelemetryEvent (the event is designed to feed them; they are separate features).
- Live map/streaming read paths (WebSocket/Server-Sent Events over TelemetryEvent).
- Vendor-specific protocol specifications (Pictor/iTriangle/Streamax adapters are placeholders until their real SDK docs/feeds are available — adapter skeleton + a sample JSON-webhook vendor will prove the contract).
- Device firmware OTA, remote immobilization command *downlink* (the model only needs to not block it: add a `DeviceCommand` table later with a vendor-adapter `SendCommand` member — reserved, not built).
- Multi-SIM failover *switching logic* (data modeled now; the SIM-failover daemon is a later feature).

---

## 9. Decisions locked (reviewed, 2026-09-04)

- **A. Hosting:** monolith-hosted ingestion — adapters/contracts/pipeline in a new `Freebuff.Platform.Ingestion` project, hosted by `Freebuff.Platform.Api`; the fleet microservice can host the same pipeline later unchanged.
- **B. Vendor identification:** explicit vendor chosen at device registration (primary); a suggestion helper (paste IMEI/sample frame → adapter `TryExtractDeviceId` suggests vendor) assists provisioning; legacy/unassigned devices park as `Status = AwaitingVendor` and their traffic is rejected loudly.
- **C. Last-known state:** new `TelemetryState` table keyed by VehicleId, updated on ingest; `Vehicle.Last*` columns migrate out in a later drop (kept one release for rollback).
- **D. Column-vs-JSON rule:** promoted columns for position/ignition/fuel/odometer/engine-hours; per-channel sensors and vendor extras in JSON until a feature justifies a column.
- **E. Raw archive:** off by default, toggleable per vendor (on for early vendor integration).
- **F. Lean telemetry rows:** `TelemetryEvent`/`RawPayload` diverge from `BaseEntity` (Id, TenantId, timestamps only — no soft-delete/Version) — a deliberate, documented convention break for the high-volume stream.
- **G. First adapter:** a sample JSON-webhook vendor proves the full pipeline end-to-end; Pictor/iTriangle/Streamax adapters follow when real protocol docs/feeds are available.

### Build phases (post-review)

1. **Phase 1 — Schema + contracts:** entities + EF configurations (both DbContexts share `IEntityTypeConfiguration<T>`) + `SchemaBootstrap` DDL + `Freebuff.Platform.Ingestion` project with contracts (adapter, registry, normalized telemetry) + seed sample vendor row.
2. **Phase 2 — Data migration:** backfill Device/VehicleDevice/TelemetryState from legacy `Vehicle.Device*`/`Last*` columns; legacy columns kept until verified.
3. **Phase 3 — Ingestion pipeline:** JSON-webhook receive endpoint (`/ingest/{vendorCode}` + auth), adapter registry, parse→enrich→persist (TelemetryEvent + TelemetryState), raw-archive toggle.
4. **Phase 4 — Management UI/API:** device CRUD + SIMs + vehicle assignment endpoints and frontend (device section replaces the embedded fields on the vehicle form), vendor suggestion helper.
5. **Phase 5 — Prove & test:** end-to-end tests through a real HTTP ingest against real Postgres (matrix + edge style already used in this repo), plus regression on vehicle pages.
