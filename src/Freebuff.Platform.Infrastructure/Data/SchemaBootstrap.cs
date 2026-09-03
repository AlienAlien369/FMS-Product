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
