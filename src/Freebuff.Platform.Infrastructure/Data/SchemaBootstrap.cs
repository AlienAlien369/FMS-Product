using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Data;

/// <summary>
/// EnsureCreatedAsync creates the full schema only when the database is new.
/// On an EXISTING database (any deployment that predates a model addition) it is
/// a no-op, so tables added after the initial release never appear. This bootstrap
/// runs idempotent DDL to add exactly those tables, keeping legacy databases
/// upgradeable without a destructive drop/recreate.
/// </summary>
public static class SchemaBootstrap
{
    /// <summary>
    /// Tables added after the initial release. Each entry mirrors the shape EF
    /// itself would create on a fresh database (column names/types per the
    /// Npgsql provider defaults, PK, and convention FK indexes) so a migrated
    /// database stays structurally identical to a fresh one.
    /// </summary>
    private static readonly string[] Additions =
    {
        // Package ↔ Module grants (replaces the legacy Package ↔ Feature concept).
        """
        CREATE TABLE IF NOT EXISTS "PackageModules" (
            "Id" uuid NOT NULL,
            "PackageId" uuid NOT NULL,
            "ModuleId" uuid NOT NULL,
            "TenantId" uuid NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_PackageModules" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_PackageModules_PackageId" ON "PackageModules" ("PackageId");
        CREATE INDEX IF NOT EXISTS "IX_PackageModules_ModuleId" ON "PackageModules" ("ModuleId");
        """,

        // Page rows inside a Module — the DB-driven page/form registry (seeded
        // from PageRegistry, fully manageable by SuperAdmin).
        """
        CREATE TABLE IF NOT EXISTS "Pages" (
            "Id" uuid NOT NULL,
            "ModuleId" uuid NOT NULL,
            "Key" text NOT NULL,
            "Name" text NOT NULL,
            "Route" text NULL,
            "Icon" text NULL,
            "Nav" boolean NOT NULL,
            "AdminOnly" boolean NOT NULL,
            "Planned" boolean NOT NULL,
            "IsCore" boolean NOT NULL,
            "Status" integer NOT NULL,
            "DisplayOrder" integer NOT NULL,
            "Description" text NULL,
            "TenantId" uuid NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_Pages" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_Pages_ModuleId" ON "Pages" ("ModuleId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Pages_Key" ON "Pages" ("Key") WHERE "IsDeleted" = false;
        """,

        // Columns added to PRE-EXISTING tables after the initial release.
        // EnsureCreatedAsync does not add columns to an existing database, so
        // without this, legacy deployments 500 on every query touching them
        // (observed on production: "column g.ViolationCount does not exist").
        """
        ALTER TABLE "Geofences" ADD COLUMN IF NOT EXISTS "ViolationCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "Geofences" ADD COLUMN IF NOT EXISTS "LastViolationAt" timestamp with time zone NULL;
        ALTER TABLE "Geofences" ADD COLUMN IF NOT EXISTS "Geometry" text NULL;
        -- Canonical geometry: backfill legacy radius-based (circle) geofences as
        -- GeoJSON circles so every consumer branches on the single Geometry field.
        UPDATE "Geofences" SET "Geometry" =
            jsonb_build_object('type', 'circle',
                'center', jsonb_build_array("CenterLongitude", "CenterLatitude"),
                'radiusMeters', "Radius")::text
            WHERE "Geometry" IS NULL
              AND "CenterLatitude" IS NOT NULL AND "CenterLongitude" IS NOT NULL AND "Radius" IS NOT NULL;

        -- Legacy rectangle/polygon rows store their ring in Coordinates as an
        -- array of lat/lng objects (seed format predating GeoJSON). Convert
        -- them to canonical polygons — a rectangle is just a 4-point polygon,
        -- so type 1 rows become type 2 with the same corners, no data loss.
        UPDATE "Geofences" SET
            "Type" = 2,
            "Geometry" = jsonb_build_object('type', 'polygon', 'coordinates',
                (SELECT jsonb_agg(jsonb_build_array((e->>'lng')::float8, (e->>'lat')::float8) ORDER BY ord)
                 FROM jsonb_array_elements(NULLIF("Coordinates", '')::jsonb) WITH ORDINALITY AS t(e, ord)))::text,
            "Coordinates" = (SELECT jsonb_agg(jsonb_build_array((e->>'lng')::float8, (e->>'lat')::float8) ORDER BY ord)::text
                 FROM jsonb_array_elements(NULLIF("Coordinates", '')::jsonb) WITH ORDINALITY AS t(e, ord))
            WHERE "Geometry" IS NULL AND "Type" IN (1, 2)
              AND "Coordinates" IS NOT NULL AND "Coordinates" NOT IN ('', '[]')
              AND NULLIF("Coordinates", '')::jsonb IS NOT NULL
              AND (SELECT count(*) FROM jsonb_array_elements(NULLIF("Coordinates", '')::jsonb)) >= 3;

        -- Repair: a first-pass run of the conversion above emitted coordinate
        -- elements as strings (e->>'lng' is text). Canonical polygons require
        -- numeric [lng, lat] pairs — fix any row whose geometry holds string
        -- positions so parsers accept it.
        UPDATE "Geofences" SET
            "Geometry" = jsonb_build_object('type', 'polygon', 'coordinates',
                (SELECT jsonb_agg(jsonb_build_array((e->>0)::float8, (e->>1)::float8) ORDER BY ord)
                 FROM jsonb_array_elements("Coordinates"::jsonb) WITH ORDINALITY AS t(e, ord)))::text,
            "Coordinates" = (SELECT jsonb_agg(jsonb_build_array((e->>0)::float8, (e->>1)::float8) ORDER BY ord)::text
                 FROM jsonb_array_elements("Coordinates"::jsonb) WITH ORDINALITY AS t(e, ord))
            WHERE "Geometry" IS NOT NULL AND "Geometry" <> ''
              AND jsonb_typeof("Geometry"::jsonb -> 'coordinates' -> 0 -> 0) = 'string';
        """,

        // ── Route ↔ Geofence linking (route checkpoints / restricted zones) ──
        // Mirrors the shape EF creates on a fresh database. RouteGeofence rows
        // carry the semantic role of a geofence on a route; the partial unique
        // index forbids linking one geofence to one route twice.
        """
        CREATE TABLE IF NOT EXISTS "RouteGeofences" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "RouteId" uuid NOT NULL,
            "GeofenceId" uuid NOT NULL,
            "Role" integer NOT NULL,
            "SequenceOrder" integer NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_RouteGeofences" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_RouteGeofences_RouteId" ON "RouteGeofences" ("RouteId");
        CREATE INDEX IF NOT EXISTS "IX_RouteGeofences_GeofenceId" ON "RouteGeofences" ("GeofenceId");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_RouteGeofences_Route_Geofence" ON "RouteGeofences" ("RouteId", "GeofenceId") WHERE "IsDeleted" = false;
        ALTER TABLE "Routes" ADD COLUMN IF NOT EXISTS "PathSource" integer NOT NULL DEFAULT 0;
        ALTER TABLE "Routes" ADD COLUMN IF NOT EXISTS "CorridorEnabled" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Routes" ADD COLUMN IF NOT EXISTS "CorridorBufferMeters" double precision NULL;
        ALTER TABLE "Routes" ADD COLUMN IF NOT EXISTS "DeviationThresholdMinutes" integer NULL;
        """,

        // ── Device Abstraction Layer ──────────────────────────────────────────
        // New tables added after the initial release (DeviceVendors, Devices,
        // DeviceSims, VehicleDevices, TelemetryEvents, TelemetryStates,
        // RawPayloads). Mirrors the shape EF itself creates on a fresh database;
        // FK constraints are omitted deliberately (matching PackageModules/Pages
        // precedent — Npgsql has no ADD CONSTRAINT IF NOT EXISTS, and application
        // logic enforces referential integrity).
        """
        CREATE TABLE IF NOT EXISTS "DeviceVendors" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "Code" text NOT NULL,
            "Name" text NOT NULL,
            "Description" text NULL,
            "AdapterVersion" text NULL,
            "ProtocolType" integer NOT NULL,
            "PayloadFormat" text NULL,
            "Status" integer NOT NULL,
            "ListenerConfig" text NULL,
            "Capabilities" text NULL,
            "Metadata" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_DeviceVendors" PRIMARY KEY ("Id")
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_DeviceVendors_Code" ON "DeviceVendors" ("Code") WHERE "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "Devices" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "CompanyId" uuid NOT NULL,
            "VendorId" uuid NULL,
            "DeviceType" integer NOT NULL,
            "DeviceTypeOverride" text NULL,
            "IdentityType" integer NOT NULL,
            "IdentityValue" text NOT NULL,
            "Model" text NULL,
            "FirmwareVersion" text NULL,
            "Status" integer NOT NULL,
            "InstallDate" timestamp with time zone NULL,
            "ActivatedAt" timestamp with time zone NULL,
            "DeactivatedAt" timestamp with time zone NULL,
            "LastSeenAt" timestamp with time zone NULL,
            "RawMetadata" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_Devices" PRIMARY KEY ("Id")
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_Devices_Company_Identity" ON "Devices" ("CompanyId", "IdentityType", "IdentityValue") WHERE "IsDeleted" = false;
        CREATE INDEX IF NOT EXISTS "IX_Devices_VendorId" ON "Devices" ("VendorId");

        CREATE TABLE IF NOT EXISTS "DeviceSims" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "DeviceId" uuid NOT NULL,
            "Iccid" text NULL,
            "PhoneNumber" text NULL,
            "Carrier" text NULL,
            "Status" integer NOT NULL,
            "IsPrimary" boolean NOT NULL,
            "ActivatedAt" timestamp with time zone NULL,
            "DeactivatedAt" timestamp with time zone NULL,
            "RawMetadata" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_DeviceSims" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_DeviceSims_DeviceId" ON "DeviceSims" ("DeviceId");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_DeviceSims_ActivePrimary" ON "DeviceSims" ("DeviceId") WHERE "IsPrimary" = true AND "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "VehicleDevices" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "VehicleId" uuid NOT NULL,
            "DeviceId" uuid NOT NULL,
            "Role" integer NOT NULL,
            "AssignedFrom" timestamp with time zone NOT NULL,
            "AssignedTo" timestamp with time zone NULL,
            "UnassignReason" text NULL,
            "RawMetadata" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_VehicleDevices" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_VehicleDevices_VehicleId" ON "VehicleDevices" ("VehicleId");
        CREATE INDEX IF NOT EXISTS "IX_VehicleDevices_DeviceId" ON "VehicleDevices" ("DeviceId");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_VehicleDevices_Vehicle_Role_Active" ON "VehicleDevices" ("VehicleId", "Role") WHERE "AssignedTo" IS NULL AND "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "TelemetryEvents" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "DeviceId" uuid NOT NULL,
            "VehicleId" uuid NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "EventTimeUtc" timestamp with time zone NOT NULL,
            "Latitude" double precision NULL,
            "Longitude" double precision NULL,
            "AltitudeM" double precision NULL,
            "SpeedKmh" double precision NULL,
            "HeadingDeg" double precision NULL,
            "Satellites" integer NULL,
            "Hdop" double precision NULL,
            "Ignition" boolean NULL,
            "EngineOn" boolean NULL,
            "FuelLevelPercent" double precision NULL,
            "FuelLevelLiters" double precision NULL,
            "OdometerKm" double precision NULL,
            "EngineHours" double precision NULL,
            "BatteryVoltage" double precision NULL,
            "DriverCardId" text NULL,
            "AlertsJson" text NULL,
            "SensorsJson" text NULL,
            "ExtrasJson" text NULL,
            "RawPayloadId" uuid NULL,
            CONSTRAINT "PK_TelemetryEvents" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_TelemetryEvents_DeviceId" ON "TelemetryEvents" ("DeviceId");
        CREATE INDEX IF NOT EXISTS "IX_TelemetryEvents_Vehicle_Time" ON "TelemetryEvents" ("VehicleId", "EventTimeUtc");

        CREATE TABLE IF NOT EXISTS "DriverBehaviorEvents" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "DeviceId" uuid NOT NULL,
            "VehicleId" uuid NULL,
            "DriverId" uuid NULL,
            "EventType" integer NOT NULL,
            "Confidence" double precision NOT NULL DEFAULT 1,
            "EventTimeUtc" timestamp with time zone NOT NULL,
            "Latitude" double precision NULL,
            "Longitude" double precision NULL,
            "SpeedKmh" double precision NULL,
            "MediaUrl" text NULL,
            "TelemetryEventId" uuid NULL,
            CONSTRAINT "PK_DriverBehaviorEvents" PRIMARY KEY ("Id")
        );
        -- Scorecard groundwork: raw events are queryable by driver + date range
        -- without a backfill (see DriverBehaviorEvent docs).
        CREATE INDEX IF NOT EXISTS "IX_DriverBehaviorEvents_Driver_Time" ON "DriverBehaviorEvents" ("DriverId", "EventTimeUtc");
        CREATE INDEX IF NOT EXISTS "IX_DriverBehaviorEvents_Vehicle_Time" ON "DriverBehaviorEvents" ("VehicleId", "EventTimeUtc");

        CREATE TABLE IF NOT EXISTS "TelemetryStates" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "VehicleId" uuid NOT NULL,
            "DeviceId" uuid NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "EventTimeUtc" timestamp with time zone NOT NULL,
            "Latitude" double precision NULL,
            "Longitude" double precision NULL,
            "AltitudeM" double precision NULL,
            "SpeedKmh" double precision NULL,
            "HeadingDeg" double precision NULL,
            "Satellites" integer NULL,
            "Ignition" boolean NULL,
            "EngineOn" boolean NULL,
            "FuelLevelPercent" double precision NULL,
            "FuelLevelLiters" double precision NULL,
            "OdometerKm" double precision NULL,
            "EngineHours" double precision NULL,
            "BatteryVoltage" double precision NULL,
            "DriverCardId" text NULL,
            CONSTRAINT "PK_TelemetryStates" PRIMARY KEY ("Id")
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_TelemetryStates_VehicleId" ON "TelemetryStates" ("VehicleId");
        CREATE INDEX IF NOT EXISTS "IX_TelemetryStates_DeviceId" ON "TelemetryStates" ("DeviceId");

        CREATE TABLE IF NOT EXISTS "RawPayloads" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "VendorId" uuid NOT NULL,
            "DeviceId" uuid NULL,
            "ReceivedAtUtc" timestamp with time zone NOT NULL,
            "Channel" text NOT NULL,
            "Payload" bytea NULL,
            "ContentType" text NULL,
            "ParseStatus" integer NOT NULL,
            "FailureReason" text NULL,
            CONSTRAINT "PK_RawPayloads" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_RawPayloads_ReceivedAt" ON "RawPayloads" ("ReceivedAtUtc");
        CREATE INDEX IF NOT EXISTS "IX_RawPayloads_Vendor_Device" ON "RawPayloads" ("VendorId", "DeviceId");
        """,

        // ── Trip module ──────────────────────────────────────────────────────
        // Trip orchestrates Vehicle + Driver + Route (optional) + Geofences
        // (mandatory before scheduling). Waypoints are first-class rows (round
        // trips use a per-waypoint leg flag); TripGeofence carries the same
        // checkpoint/restricted role model as RouteGeofence; StatusHistory is
        // the audit trail of every transition. No FK constraints (matching the
        // PackageModules/Pages precedent — application logic enforces integrity).
        """
        CREATE TABLE IF NOT EXISTS "TripWaypoints" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "TripId" uuid NOT NULL,
            "SequenceOrder" integer NOT NULL,
            "LegType" integer NOT NULL,
            "WaypointType" integer NOT NULL,
            "Name" text NOT NULL,
            "Latitude" double precision NOT NULL,
            "Longitude" double precision NOT NULL,
            "Address" text NULL,
            "ExpectedArrival" timestamp with time zone NULL,
            "ActualArrival" timestamp with time zone NULL,
            "LinkedGeofenceId" uuid NULL,
            "ProofOfCompletion" text NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_TripWaypoints" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_TripWaypoints_TripId" ON "TripWaypoints" ("TripId");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_TripWaypoints_Trip_Seq" ON "TripWaypoints" ("TripId", "SequenceOrder") WHERE "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "TripGeofences" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "TripId" uuid NOT NULL,
            "GeofenceId" uuid NOT NULL,
            "Role" integer NOT NULL,
            "SequenceOrder" integer NULL,
            "Visited" boolean NULL,
            "VisitedAt" timestamp with time zone NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_TripGeofences" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_TripGeofences_TripId" ON "TripGeofences" ("TripId");
        CREATE INDEX IF NOT EXISTS "IX_TripGeofences_GeofenceId" ON "TripGeofences" ("GeofenceId");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_TripGeofences_Trip_Geofence" ON "TripGeofences" ("TripId", "GeofenceId") WHERE "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "TripStatusHistories" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NULL,
            "TripId" uuid NOT NULL,
            "FromStatus" integer NOT NULL,
            "ToStatus" integer NOT NULL,
            "Reason" text NULL,
            "Source" text NOT NULL,
            "ChangedAt" timestamp with time zone NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            "UpdatedBy" text NULL,
            "IsDeleted" boolean NOT NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL,
            CONSTRAINT "PK_TripStatusHistories" PRIMARY KEY ("Id")
        );
        CREATE INDEX IF NOT EXISTS "IX_TripStatusHistories_TripId" ON "TripStatusHistories" ("TripId");

        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "Type" integer NOT NULL DEFAULT 0;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "IsDelayed" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "DelayReason" text NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "CancelReason" text NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "RouteId" uuid NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "RouteGeometry" text NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "CorridorEnabled" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "CorridorBufferMeters" double precision NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "DeviatedSince" timestamp with time zone NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "CorridorAlerted" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "DeviationThresholdMinutes" integer NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "FuelUsedLiters" numeric NULL;
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "IdleMinutes" integer NULL;
        -- Proof-of-delivery policy: nullable per-trip override of the company default.
        ALTER TABLE "Trips" ADD COLUMN IF NOT EXISTS "RequirePodForDelivery" boolean NULL;
        -- Waypoint customer contact (OTP delivery at the stop) + POD evidence.
        ALTER TABLE "TripWaypoints" ADD COLUMN IF NOT EXISTS "CustomerPhone" text NULL;
        ALTER TABLE "TripWaypoints" ADD COLUMN IF NOT EXISTS "CustomerEmail" text NULL;

        -- ── Proof of Delivery ────────────────────────────────────────────
        CREATE TABLE IF NOT EXISTS "ProofOfDeliveries" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NULL,
            "TripId" uuid NOT NULL REFERENCES "Trips"("Id"),
            "WaypointId" uuid NOT NULL REFERENCES "TripWaypoints"("Id"),
            "CompanyId" uuid NOT NULL REFERENCES "Companies"("Id"),
            "Type" integer NOT NULL,
            "SignatureSvg" text NULL,
            "ImageUrl" text NULL,
            "OtpCodeHash" text NULL,
            "OtpVerifiedAt" timestamp with time zone NULL,
            "OtpFailedAttempts" integer NOT NULL DEFAULT 0,
            "CapturedBy" text NOT NULL,
            "CapturedAt" timestamp with time zone NOT NULL,
            "Latitude" double precision NULL,
            "Longitude" double precision NULL,
            "LocationMismatch" boolean NULL,
            "LocationMismatchDetail" text NULL,
            "Notes" text NULL,
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NULL,
            "UpdatedBy" text NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL DEFAULT 0
        );
        -- Column added after the table shipped — existing deployments get it via ALTER.
        ALTER TABLE "ProofOfDeliveries" ADD COLUMN IF NOT EXISTS "OtpFailedAttempts" integer NOT NULL DEFAULT 0;
        CREATE INDEX IF NOT EXISTS "IX_ProofOfDeliveries_TripId" ON "ProofOfDeliveries" ("TripId");
        CREATE INDEX IF NOT EXISTS "IX_ProofOfDeliveries_WaypointId" ON "ProofOfDeliveries" ("WaypointId");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProofOfDeliveries_Waypoint_Type_Active"
            ON "ProofOfDeliveries" ("WaypointId", "Type") WHERE "IsDeleted" = false;

        -- ── Customer Tracking Link share tokens ────────────────────────────
        -- Public access primitive: possession of the random token alone grants
        -- read-only access to ONE trip. Never routes through RBAC. Unique token
        -- index keeps guessing infeasible AND unambiguous.
        CREATE TABLE IF NOT EXISTS "TripShareLinks" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NULL,
            "TripId" uuid NOT NULL REFERENCES "Trips"("Id"),
            "CompanyId" uuid NOT NULL REFERENCES "Companies"("Id"),
            "Token" text NOT NULL,
            "ExpiresAt" timestamp with time zone NULL,
            "IsRevoked" boolean NOT NULL DEFAULT false,
            "CreatedByUserId" text NOT NULL DEFAULT '',
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NULL,
            "UpdatedBy" text NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL DEFAULT 0
        );
        ALTER TABLE "TripShareLinks" ADD COLUMN IF NOT EXISTS "CreatedByUserId" text NOT NULL DEFAULT '';
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_TripShareLinks_Token" ON "TripShareLinks" ("Token");
        CREATE INDEX IF NOT EXISTS "IX_TripShareLinks_TripId" ON "TripShareLinks" ("TripId");

        -- ── Sensor telemetry: speed governor + TPMS ────────────────────────
        ALTER TABLE "TelemetryEvents" ADD COLUMN IF NOT EXISTS "SpeedGovernorLimitKmh" double precision NULL;
        ALTER TABLE "TelemetryStates" ADD COLUMN IF NOT EXISTS "SpeedGovernorLimitKmh" double precision NULL;
        -- Per-vehicle sensor policy overrides (null = follow company fleet default).
        ALTER TABLE "Vehicles" ADD COLUMN IF NOT EXISTS "SpeedPolicyMaxKmh" double precision NULL;
        ALTER TABLE "Vehicles" ADD COLUMN IF NOT EXISTS "TyrePressureMinBar" double precision NULL;
        ALTER TABLE "Vehicles" ADD COLUMN IF NOT EXISTS "TyrePressureMaxBar" double precision NULL;

        -- Per-tyre pressure readings, child of a telemetry snapshot (one-to-many).
        CREATE TABLE IF NOT EXISTS "TyrePressureReadings" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "TelemetryEventId" uuid NOT NULL REFERENCES "TelemetryEvents"("Id") ON DELETE CASCADE,
            "VehicleId" uuid NOT NULL,
            "Position" integer NOT NULL,
            "PressureBar" double precision NOT NULL,
            "TemperatureC" double precision NULL,
            "EventTimeUtc" timestamp with time zone NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS "IX_TyrePressureReadings_Vehicle_Time" ON "TyrePressureReadings" ("VehicleId", "EventTimeUtc");

        -- Hourly min/max/avg rollup of raw sensor readings (retention path: raw
        -- ~30 days → folded into one row per vehicle+sensor+hour → kept 12 months).
        CREATE TABLE IF NOT EXISTS "TelemetryRollupsHourly" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "VehicleId" uuid NOT NULL,
            "SensorType" text NOT NULL,
            "TyrePosition" integer NULL,
            "HourBucketUtc" timestamp with time zone NOT NULL,
            "MinValue" double precision NOT NULL,
            "MaxValue" double precision NOT NULL,
            "AvgValue" double precision NOT NULL,
            "ReadingCount" integer NOT NULL DEFAULT 0,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_TelemetryRollups_Vehicle_Sensor_Hour"
            ON "TelemetryRollupsHourly" ("VehicleId", "SensorType", "TyrePosition", "HourBucketUtc");

        -- Registry data migration: the trip page shipped in this release
        -- (PageRegistry now marks it live with a real route). Flip the DB row
        -- once so tenants see it in the catalog/sidebar; harmless re-run.
        UPDATE "Pages" SET "Planned" = false, "Nav" = true, "Route" = '/trips'
            WHERE "Key" = 'trip' AND "IsDeleted" = false AND "Planned" = true;

        -- Same flip for the notification page (personal in-app inbox, shipped
        -- with the notification system). New pages (notificationsettings) are
        -- created by the seed on next boot — only existing rows need the flip.
        UPDATE "Pages" SET "Planned" = false, "Nav" = true, "Route" = '/notifications'
            WHERE "Key" = 'notification' AND "IsDeleted" = false AND "Planned" = true;

        -- ── Alert type registry (three-tier control) ──────────────────────
        CREATE TABLE IF NOT EXISTS "AlertTypes" (
            "Id" uuid PRIMARY KEY,
            "Code" text NOT NULL,
            "Name" text NOT NULL,
            "Description" text NULL,
            "Category" text NOT NULL,
            "DefaultSeverity" integer NOT NULL DEFAULT 2,
            "DisplayOrder" integer NOT NULL DEFAULT 0,
            "Status" integer NOT NULL DEFAULT 0,
            "NonMutablePriority" boolean NOT NULL DEFAULT false,
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "UpdatedAt" timestamp with time zone NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_AlertTypes_Code" ON "AlertTypes" ("Code") WHERE "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "CompanyAlertSubscriptions" (
            "Id" uuid PRIMARY KEY,
            "CompanyId" uuid NOT NULL REFERENCES "Companies"("Id"),
            "AlertTypeId" uuid NOT NULL REFERENCES "AlertTypes"("Id"),
            "Enabled" boolean NOT NULL DEFAULT true,
            "Source" text NOT NULL DEFAULT 'package_default',
            "Status" integer NOT NULL DEFAULT 0,
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "UpdatedAt" timestamp with time zone NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_CompanyAlertSubscriptions_CompanyId_AlertTypeId"
            ON "CompanyAlertSubscriptions" ("CompanyId", "AlertTypeId") WHERE "IsDeleted" = false;

        CREATE TABLE IF NOT EXISTS "RoleAlertVisibilities" (
            "Id" uuid PRIMARY KEY,
            "CompanyId" uuid NOT NULL REFERENCES "Companies"("Id"),
            "RoleId" uuid NOT NULL REFERENCES "Roles"("Id"),
            "AlertTypeId" uuid NOT NULL REFERENCES "AlertTypes"("Id"),
            "Visible" boolean NOT NULL DEFAULT true,
            "Status" integer NOT NULL DEFAULT 0,
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "UpdatedAt" timestamp with time zone NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_RoleAlertVisibilities_CompanyId_RoleId_AlertTypeId"
            ON "RoleAlertVisibilities" ("CompanyId", "RoleId", "AlertTypeId") WHERE "IsDeleted" = false;

        -- ── Registry-table BaseEntity repair ────────────────────────────────
        -- The alert/notification registry tables above ship a REDUCED column set
        -- (for legacy DBs that never went through EnsureCreated). EF's model maps
        -- the full BaseEntity (TenantId, CreatedBy, UpdatedBy, DeletedAt,
        -- DeletedBy, DeletionReason, Version), so inserts on a legacy DB fail with
        -- "column X does not exist". These idempotent ALTERs bring every registry
        -- table up to the exact shape EnsureCreated would have produced.
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "CreatedBy" text NULL;
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "UpdatedBy" text NULL;
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "DeletedBy" text NULL;
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "DeletionReason" text NULL;
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;
        -- Emergency-signal flag (driver.panic_button): when true the pipeline
        -- bypasses role-visibility narrowing and per-user notification preferences.
        ALTER TABLE "AlertTypes" ADD COLUMN IF NOT EXISTS "NonMutablePriority" boolean NOT NULL DEFAULT false;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "CreatedBy" text NULL;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "UpdatedBy" text NULL;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "DeletedBy" text NULL;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "DeletionReason" text NULL;
        ALTER TABLE "CompanyAlertSubscriptions" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "CreatedBy" text NULL;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "UpdatedBy" text NULL;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "DeletedBy" text NULL;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "DeletionReason" text NULL;
        ALTER TABLE "RoleAlertVisibilities" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;

        -- ── Notification system (event-type registry + per-user preferences) ──
        -- Notifications table itself predates the bootstrap; add the registry-
        -- linked columns idempotently (DeliveryChannel reserved for email/SMS phase).
        ALTER TABLE "Notifications" ADD COLUMN IF NOT EXISTS "EventType" text NULL;
        ALTER TABLE "Notifications" ADD COLUMN IF NOT EXISTS "Severity" integer NOT NULL DEFAULT 2;
        ALTER TABLE "Notifications" ADD COLUMN IF NOT EXISTS "RelatedEntityType" text NULL;
        ALTER TABLE "Notifications" ADD COLUMN IF NOT EXISTS "RelatedEntityId" uuid NULL;
        ALTER TABLE "Notifications" ADD COLUMN IF NOT EXISTS "DeliveryChannel" text NULL;

        CREATE TABLE IF NOT EXISTS "NotificationEventTypes" (
            "Id" uuid PRIMARY KEY,
            "Code" text NOT NULL,
            "Name" text NOT NULL,
            "Description" text NULL,
            "Category" text NOT NULL,
            "DefaultSeverity" integer NOT NULL DEFAULT 2,
            "DisplayOrder" integer NOT NULL DEFAULT 0,
            "Status" integer NOT NULL DEFAULT 0,
            "TenantId" uuid NULL,
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NULL,
            "UpdatedBy" text NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NotificationEventTypes_Code" ON "NotificationEventTypes" ("Code") WHERE "IsDeleted" = false;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "CreatedBy" text NULL;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "UpdatedBy" text NULL;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "DeletedBy" text NULL;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "DeletionReason" text NULL;
        ALTER TABLE "NotificationEventTypes" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;

        CREATE TABLE IF NOT EXISTS "NotificationPreferences" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL REFERENCES "Users"("Id"),
            "CompanyId" uuid NOT NULL REFERENCES "Companies"("Id"),
            "EventType" text NOT NULL,
            "Enabled" boolean NOT NULL DEFAULT true,
            "Status" integer NOT NULL DEFAULT 0,
            "TenantId" uuid NULL,
            "IsDeleted" boolean NOT NULL DEFAULT false,
            "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
            "CreatedBy" text NULL,
            "UpdatedAt" timestamp with time zone NULL,
            "UpdatedBy" text NULL,
            "DeletedAt" timestamp with time zone NULL,
            "DeletedBy" text NULL,
            "DeletionReason" text NULL,
            "Version" integer NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_NotificationPreferences_UserId_EventType"
            ON "NotificationPreferences" ("UserId", "EventType") WHERE "IsDeleted" = false;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "TenantId" uuid NULL;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "CreatedBy" text NULL;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "UpdatedBy" text NULL;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "DeletedBy" text NULL;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "DeletionReason" text NULL;
        ALTER TABLE "NotificationPreferences" ADD COLUMN IF NOT EXISTS "Version" integer NOT NULL DEFAULT 0;
        """
    };

    public static async Task EnsureSchemaAsync(ApplicationDbContext db)
    {
        if (!db.Database.IsRelational())
        {
            // In-memory providers model every entity; nothing to bootstrap.
            return;
        }
        foreach (var ddl in Additions)
        {
            await db.Database.ExecuteSqlRawAsync(ddl);
        }
    }
}
