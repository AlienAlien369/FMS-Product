using System.Text.Json;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Ingestion.Contracts;
using Freebuff.Platform.Ingestion.Registry;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Freebuff.Platform.Infrastructure.Services;

public sealed record IngestResult(bool Accepted, string Code, string Message, string? DeviceId = null, string? VehicleId = null)
{
    public static IngestResult Ok(string deviceId, string? vehicleId) => new(true, "ACCEPTED", "Telemetry accepted", deviceId, vehicleId);
    public static IngestResult Reject(string code, string message) => new(false, code, message);
}

/// <summary>
/// Vendor-agnostic ingestion pipeline: raw payload in → vendor/adapter resolved →
/// adapter normalizes to the common schema → normalized event + last-known state
/// persisted. All vendor knowledge lives in the adapter; nothing here branches on
/// vendor. A malformed payload for one vendor never affects another vendor's
/// devices — parse errors return a rejection, never an exception.
/// </summary>
public class DeviceIngestionService
{
    private readonly ApplicationDbContext _db;
    private readonly IVendorAdapterRegistry _registry;
    private readonly ILogger<DeviceIngestionService> _logger;
    private readonly TripGeofenceEventProducer _zoneEvents;

    public DeviceIngestionService(ApplicationDbContext db, IVendorAdapterRegistry registry,
        ILogger<DeviceIngestionService> logger, TripGeofenceEventProducer zoneEvents)
    {
        _db = db;
        _registry = registry;
        _logger = logger;
        _zoneEvents = zoneEvents;
    }

    public async Task<IngestResult> IngestAsync(string vendorCode, string channel, byte[] payload, string? contentType, string? ingestKey)
    {
        if (payload.Length == 0) return IngestResult.Reject("EMPTY_PAYLOAD", "Payload is empty");

        var adapter = _registry.Get(vendorCode);
        if (adapter == null)
            return IngestResult.Reject("UNKNOWN_VENDOR", $"No adapter registered for vendor '{vendorCode}'");

        var vendor = await _db.DeviceVendors.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Code == vendorCode && !v.IsDeleted);
        if (vendor == null || vendor.Status != DeviceStatus.Active)
            return IngestResult.Reject("VENDOR_INACTIVE", $"Vendor '{vendorCode}' is not active");

        var archiveRaw = ReadArchiveFlag(vendor.Metadata);
        var secret = ReadIngestKey(vendor.Metadata);
        if (!string.IsNullOrEmpty(secret) && !string.Equals(secret, ingestKey, StringComparison.Ordinal))
            return IngestResult.Reject("UNAUTHORIZED", "Invalid ingest key");

        // ── Parse (never throws — adapters return ParseRejected on garbage) ──
        ParseResult parseResult;
        try
        {
            parseResult = adapter.Parse(payload, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adapter {Vendor} threw while parsing — vendor defect, not payload result", vendorCode);
            if (archiveRaw) await ArchiveAsync(vendor, null, channel, payload, contentType, TelemetryParseStatus.Failed, "adapter threw: " + ex.Message);
            return IngestResult.Reject("ADAPTER_ERROR", "Adapter failed to parse payload");
        }

        if (parseResult is NeedsMoreData)
        {
            if (archiveRaw) await ArchiveAsync(vendor, null, channel, payload, contentType, TelemetryParseStatus.Unparsed, "needs more data");
            return IngestResult.Reject("NEEDS_MORE_DATA", "Incomplete frame; more data required");
        }

        if (parseResult is ParseRejected rejected)
        {
            _logger.LogWarning("Vendor {Vendor} rejected payload: {Reason}", vendorCode, rejected.Reason);
            if (archiveRaw) await ArchiveAsync(vendor, null, channel, payload, contentType, TelemetryParseStatus.Failed, rejected.Reason);
            return IngestResult.Reject("PARSE_REJECTED", rejected.Reason);
        }

        var ok = (ParseOk)parseResult;
        var telemetry = ok.Telemetry;

        // ── Resolve device (trusted server-side lookup; identity from the frame) ──
        if (!Enum.TryParse<DeviceIdentityType>(telemetry.Device.IdentityType, ignoreCase: true, out var identityType))
            return IngestResult.Reject("UNKNOWN_IDENTITY", $"Unsupported identity type '{telemetry.Device.IdentityType}'");

        var device = await _db.Devices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.VendorId == vendor.Id
                && d.IdentityType == identityType
                && d.IdentityValue == telemetry.Device.IdentityValue
                && !d.IsDeleted);

        if (device == null)
            return IngestResult.Reject("DEVICE_NOT_REGISTERED",
                $"No device registered for vendor '{vendorCode}' with {identityType} '{telemetry.Device.IdentityValue}'");
        if (device.Status != DeviceStatus.Active)
            return IngestResult.Reject("DEVICE_INACTIVE", $"Device is not active (status {device.Status})");

        // ── Resolve vehicle from the active assignment ──
        var assignment = await _db.VehicleDevices.AsNoTracking()
            .FirstOrDefaultAsync(vd => vd.DeviceId == device.Id && vd.AssignedTo == null && !vd.IsDeleted);

        var now = DateTime.UtcNow;
        var eventTime = telemetry.EventTimeUtc ?? now;
        device.LastSeenAt = now;

        var telemetryEvent = new TelemetryEvent
        {
            Id = Guid.NewGuid(),
            TenantId = device.CompanyId,
            DeviceId = device.Id,
            VehicleId = assignment?.VehicleId,
            CreatedAt = now,
            EventTimeUtc = eventTime,
            Latitude = telemetry.Latitude,
            Longitude = telemetry.Longitude,
            AltitudeM = telemetry.AltitudeM,
            SpeedKmh = telemetry.SpeedKmh,
            HeadingDeg = telemetry.HeadingDeg,
            Satellites = telemetry.Satellites,
            Hdop = telemetry.Hdop,
            Ignition = telemetry.Ignition,
            EngineOn = telemetry.EngineOn,
            FuelLevelPercent = telemetry.FuelLevelPercent,
            FuelLevelLiters = telemetry.FuelLevelLiters,
            OdometerKm = telemetry.OdometerKm,
            EngineHours = telemetry.EngineHours,
            BatteryVoltage = telemetry.BatteryVoltage,
            DriverCardId = telemetry.DriverCardId,
            AlertsJson = telemetry.Alerts.Count > 0 ? JsonSerializer.Serialize(telemetry.Alerts) : null,
            SensorsJson = telemetry.Sensors.Count > 0 ? JsonSerializer.Serialize(telemetry.Sensors) : null,
            ExtrasJson = telemetry.Extras.Count > 0 ? JsonSerializer.Serialize(telemetry.Extras) : null
        };
        _db.TelemetryEvents.Add(telemetryEvent);

        if (assignment != null)
        {
            var state = await _db.TelemetryStates.FirstOrDefaultAsync(s => s.VehicleId == assignment.VehicleId);
            if (state == null)
            {
                state = new TelemetryState
                {
                    Id = Guid.NewGuid(),
                    TenantId = device.CompanyId,
                    VehicleId = assignment.VehicleId,
                    DeviceId = device.Id,
                    CreatedAt = now,
                    UpdatedAt = now,
                    EventTimeUtc = eventTime
                };
                _db.TelemetryStates.Add(state);
            }
            else
            {
                state.UpdatedAt = now;
                state.EventTimeUtc = eventTime;
            }
            CopyToState(state, telemetry, device.Id);
        }

        if (archiveRaw)
            await ArchiveAsync(vendor, device.Id, channel, payload, contentType, TelemetryParseStatus.Parsed, null);

        // Trip automation: a position fix on an assigned vehicle may cross a
        // linked geofence boundary — the producer fires entry/exit zone events
        // (auto-start/complete, checkpoint visits, restricted-zone alerts) into
        // the same context so the fix and its effects commit atomically.
        if (assignment != null && telemetry.Latitude.HasValue && telemetry.Longitude.HasValue)
            await _zoneEvents.ProcessPositionAsync(assignment.VehicleId, telemetry.Latitude.Value, telemetry.Longitude.Value, eventTime);

        await _db.SaveChangesAsync();
        return IngestResult.Ok(device.Id.ToString(), assignment?.VehicleId.ToString());
    }

    private async Task ArchiveAsync(DeviceVendor vendor, Guid? deviceId, string channel, byte[] payload,
        string? contentType, TelemetryParseStatus status, string? failure)
    {
        _db.RawPayloads.Add(new RawPayload
        {
            Id = Guid.NewGuid(),
            TenantId = deviceId.HasValue ? await GetDeviceTenantAsync(deviceId.Value) : null,
            VendorId = vendor.Id,
            DeviceId = deviceId,
            ReceivedAtUtc = DateTime.UtcNow,
            Channel = channel,
            Payload = payload,
            ContentType = contentType,
            ParseStatus = status,
            FailureReason = failure
        });
    }

    private async Task<Guid?> GetDeviceTenantAsync(Guid deviceId)
        => (await _db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => (Guid?)d.CompanyId).FirstOrDefaultAsync());

    private static void CopyToState(TelemetryState state, NormalizedTelemetry t, Guid deviceId)
    {
        state.DeviceId = deviceId;
        // Only fields the payload actually carries — a heartbeat frame without a
        // position must NOT wipe the last-known good fix (observed live: state
        // lat went null after a position-less event).
        if (t.Latitude != null) state.Latitude = t.Latitude;
        if (t.Longitude != null) state.Longitude = t.Longitude;
        if (t.AltitudeM != null) state.AltitudeM = t.AltitudeM;
        if (t.SpeedKmh != null) state.SpeedKmh = t.SpeedKmh;
        if (t.HeadingDeg != null) state.HeadingDeg = t.HeadingDeg;
        if (t.Satellites != null) state.Satellites = t.Satellites;
        if (t.Ignition != null) state.Ignition = t.Ignition;
        if (t.EngineOn != null) state.EngineOn = t.EngineOn;
        if (t.FuelLevelPercent != null) state.FuelLevelPercent = t.FuelLevelPercent;
        if (t.FuelLevelLiters != null) state.FuelLevelLiters = t.FuelLevelLiters;
        if (t.OdometerKm != null) state.OdometerKm = t.OdometerKm;
        if (t.EngineHours != null) state.EngineHours = t.EngineHours;
        if (t.BatteryVoltage != null) state.BatteryVoltage = t.BatteryVoltage;
        if (t.DriverCardId != null) state.DriverCardId = t.DriverCardId;
    }

    private static bool ReadArchiveFlag(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return false;
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            return doc.RootElement.TryGetProperty("archiveRaw", out var el) && el.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadIngestKey(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadata);
            return doc.RootElement.TryGetProperty("ingestKey", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
