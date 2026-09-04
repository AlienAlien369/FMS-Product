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
