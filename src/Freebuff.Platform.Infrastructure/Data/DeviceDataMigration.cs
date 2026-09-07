using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.Data;

/// <summary>
/// Idempotent data migration for the Device Abstraction Layer, run on every
/// startup AFTER schema bootstrap and seed. Two jobs:
///  1. Seed the platform DeviceVendor catalog rows (matching registered adapters).
///  2. Backfill Device / VehicleDevice / TelemetryState from the legacy embedded
///     Vehicle.DeviceImei / DeviceType / DeviceSerialNumber / Last* columns —
///     WITHOUT data loss: the legacy strings are preserved verbatim in
///     Device.DeviceTypeOverride, legacy devices get VendorId = null +
///     Status = AwaitingVendor (their traffic is rejected until a vendor is
///     identified), and existing vehicles resolve to a PrimaryTracker assignment.
/// The legacy Vehicle columns are intentionally kept for one release (rollback).
/// </summary>
public static class DeviceDataMigration
{
    private static readonly (string Code, string Name, string Protocol, string? Format, string? Description, string? Capabilities)[] Vendors =
    {
        ("sample-json", "Sample JSON (webhook)", "http", "json-webhook",
            "Reference JSON-webhook vendor that proves the ingestion pipeline end-to-end. Active.",
            "[\"gps\",\"speed\",\"heading\",\"ignition\",\"engine\",\"fuel\",\"odometer\",\"engineHours\",\"battery\",\"driverId\",\"sensors\",\"alerts\",\"behaviorEvents\"]"),
        ("pictor", "Pictor", "http", "pictor-json-v1",
            "Pictor tracking + DMS. JSON-webhook adapter (documented mock format) with driver-behavior event codes (hb/accel/corner/idle/drowsy/distracted/phone/sos). Active.",
            "[\"gps\",\"speed\",\"dms\"]"),
        ("itriangle", "iTriangle", "http", "itriangle-json-v1",
            "iTriangle tracking + DMS. JSON-webhook adapter (documented mock format) with driver-behavior event codes (HARSH_BRAKE/RAPID_ACCEL/SHARP_TURN/IDLING/DROWSY/DISTRACTED/PHONE_USE/PANIC). Active.",
            "[\"gps\",\"speed\",\"dms\"]"),
        ("streamax", "Streamax", "http", "streamax-json-v1",
            "Streamax tracking + DMS. JSON-webhook adapter (documented mock format) with numeric ADAS alarm codes (1=drowsiness … 8=SOS). Active.",
            "[\"gps\",\"speed\",\"dms\"]")
    };

    public static async Task EnsureAsync(ApplicationDbContext db, ILogger logger)
    {
        await SeedVendorsAsync(db, logger);
        await BackfillLegacyDevicesAsync(db, logger);
    }

    private static async Task SeedVendorsAsync(ApplicationDbContext db, ILogger logger)
    {
        var seeded = 0;
        var reactivated = 0;
        foreach (var (code, name, protocol, format, description, capabilities) in Vendors)
        {
            var existing = await db.DeviceVendors.FirstOrDefaultAsync(v => v.Code == code && !v.IsDeleted);
            if (existing == null)
            {
                db.DeviceVendors.Add(new DeviceVendor
                {
                    Id = Guid.NewGuid(),
                    Code = code,
                    Name = name,
                    Description = description,
                    AdapterVersion = "1.1.0",
                    ProtocolType = ParseProtocol(protocol),
                    PayloadFormat = format,
                    // All four registered adapters are real parsers now — active.
                    Status = DeviceStatus.Active,
                    ListenerConfig = code == "sample-json" ? "{\"path\":\"api/v1/ingest/sample-json\"}" : null,
                    Capabilities = capabilities,
                    Metadata = "{\"archiveRaw\":false}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "system:migration"
                });
                seeded++;
                continue;
            }

            // Re-activate rows created by the old placeholder migration now that
            // real DMS-capable adapters exist (pictor/itriangle were seeded Inactive).
            if (existing.Status != DeviceStatus.Active || existing.AdapterVersion != "1.1.0")
            {
                existing.Status = DeviceStatus.Active;
                existing.AdapterVersion = "1.1.0";
                existing.PayloadFormat = format;
                existing.Description = description;
                existing.Capabilities = capabilities;
                existing.ProtocolType = ParseProtocol(protocol);
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = "system:migration";
                reactivated++;
            }
        }

        if (seeded + reactivated > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("DeviceDataMigration: seeded {Count} vendor row(s), reactivated {Reactivated}", seeded, reactivated);
        }
    }

    private static async Task BackfillLegacyDevicesAsync(ApplicationDbContext db, ILogger logger)
    {
        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.DeviceImei != null && v.DeviceImei != "")
            .Select(v => new { v.Id, v.CompanyId, v.CreatedAt, v.DeviceImei, v.DeviceType, v.DeviceSerialNumber,
                v.LastLatitude, v.LastLongitude, v.LastSpeed, v.LastHeading, v.LastLocationUpdate, v.IgnitionStatus,
                v.OdometerReading, v.EngineHours })
            .ToListAsync();
        if (vehicles.Count == 0) return;

        var existingDevices = await db.Devices.AsNoTracking()
            .Where(d => d.IdentityType == DeviceIdentityType.Imei && !d.IsDeleted)
            .Select(d => new { d.CompanyId, d.IdentityValue, d.Id })
            .ToDictionaryAsync(d => (d.CompanyId, d.IdentityValue));

        var devicesAdded = 0;
        var assignmentsAdded = 0;
        var statesAdded = 0;

        foreach (var v in vehicles)
        {
            var imei = v.DeviceImei!.Trim();
            if (!existingDevices.TryGetValue((v.CompanyId, imei), out var deviceRef))
            {
                var device = new Device
                {
                    Id = Guid.NewGuid(),
                    TenantId = v.CompanyId,
                    CompanyId = v.CompanyId,
                    VendorId = null, // legacy — vendor unknown until identified
                    DeviceType = MapLegacyDeviceType(v.DeviceType),
                    DeviceTypeOverride = v.DeviceType, // preserve original text verbatim
                    IdentityType = DeviceIdentityType.Imei,
                    IdentityValue = imei,
                    Status = DeviceStatus.AwaitingVendor,
                    CreatedAt = v.CreatedAt,
                    UpdatedAt = v.CreatedAt,
                    CreatedBy = "system:migration"
                };
                db.Devices.Add(device);
                deviceRef = new { CompanyId = v.CompanyId, IdentityValue = imei, Id = device.Id };
                existingDevices[(v.CompanyId, imei)] = deviceRef;
                devicesAdded++;
            }

            // Active PrimaryTracker assignment
            var hasAssignment = await db.VehicleDevices.AsNoTracking()
                .AnyAsync(vd => vd.VehicleId == v.Id && vd.Role == VehicleDeviceRole.PrimaryTracker
                    && vd.AssignedTo == null && !vd.IsDeleted);
            if (!hasAssignment)
            {
                db.VehicleDevices.Add(new VehicleDevice
                {
                    Id = Guid.NewGuid(),
                    TenantId = v.CompanyId,
                    VehicleId = v.Id,
                    DeviceId = deviceRef.Id,
                    Role = VehicleDeviceRole.PrimaryTracker,
                    AssignedFrom = v.CreatedAt,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "system:migration"
                });
                assignmentsAdded++;
            }

            // Last-known telemetry state (only when legacy last position exists)
            var hasState = await db.TelemetryStates.AsNoTracking().AnyAsync(s => s.VehicleId == v.Id);
            if (!hasState)
            {
                db.TelemetryStates.Add(new TelemetryState
                {
                    Id = Guid.NewGuid(),
                    TenantId = v.CompanyId,
                    VehicleId = v.Id,
                    DeviceId = deviceRef.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = v.LastLocationUpdate ?? DateTime.UtcNow,
                    EventTimeUtc = v.LastLocationUpdate ?? v.CreatedAt,
                    Latitude = v.LastLatitude,
                    Longitude = v.LastLongitude,
                    SpeedKmh = v.LastSpeed,
                    HeadingDeg = v.LastHeading,
                    Ignition = v.IgnitionStatus,
                    OdometerKm = v.OdometerReading,
                    EngineHours = v.EngineHours
                });
                statesAdded++;
            }
        }

        if (devicesAdded + assignmentsAdded + statesAdded > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("DeviceDataMigration: backfilled {Devices} devices, {Assignments} PrimaryTracker assignments, {States} telemetry states from legacy Vehicle columns",
                devicesAdded, assignmentsAdded, statesAdded);
        }
    }

    private static DeviceType MapLegacyDeviceType(string? legacy)
    {
        if (string.IsNullOrWhiteSpace(legacy)) return DeviceType.GpsTracker;
        var text = legacy.ToLowerInvariant();
        if (text.Contains("gps") || text.Contains("tracker")) return DeviceType.GpsTracker;
        if (text.Contains("camera") || text.Contains("dash")) return DeviceType.Dashcam;
        if (text.Contains("adas")) return DeviceType.Adas;
        if (text.Contains("fuel")) return DeviceType.FuelSensor;
        if (text.Contains("temp")) return DeviceType.TemperatureSensor;
        if (text.Contains("dual") || text.Contains("360")) return DeviceType.DualCamera;
        return DeviceType.Other;
    }

    private static DeviceProtocolType ParseProtocol(string protocol) => protocol switch
    {
        "tcp" => DeviceProtocolType.TcpRaw,
        "udp" => DeviceProtocolType.Udp,
        "mqtt" => DeviceProtocolType.Mqtt,
        _ => DeviceProtocolType.HttpWebhook
    };
}
