using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Vendor-agnostic normalized telemetry row — everything downstream reads this
/// stream. DELIBERATELY lean (Id, TenantId, timestamps only — no soft-delete,
/// no Version, no audit): this is an append-only high-volume stream and the
/// BaseEntity baggage has no business value here. Vendor knowledge never lands in
/// this table; the adapter is the only place vendor parsing exists.
/// </summary>
public class TelemetryEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DeviceId { get; set; }

    /// <summary>Denormalized from the active assignment at write time (avoids a join on hot queries).</summary>
    public Guid? VehicleId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Device-reported event time when available/trustworthy, else receive time.</summary>
    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;

    // Position (first-class: every consumer needs these)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AltitudeM { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDeg { get; set; }
    public int? Satellites { get; set; }
    public double? Hdop { get; set; }

    // State
    public bool? Ignition { get; set; }
    public bool? EngineOn { get; set; }
    public double? FuelLevelPercent { get; set; }
    public double? FuelLevelLiters { get; set; }
    public double? OdometerKm { get; set; }
    public double? EngineHours { get; set; }
    public double? BatteryVoltage { get; set; }
    public string? DriverCardId { get; set; }

    /// <summary>JSON array of canonical alert codes (adapter-normalized).</summary>
    public string? AlertsJson { get; set; }

    /// <summary>JSON map of per-channel sensor readings not yet promoted to columns (e.g. {"temp1":23.1}).</summary>
    public string? SensorsJson { get; set; }

    /// <summary>JSON — anything else the adapter produced.</summary>
    public string? ExtrasJson { get; set; }

    public Guid? RawPayloadId { get; set; }
}

/// <summary>
/// Materialized last-known state per vehicle, updated on every accepted event.
/// Replaces the denormalized Vehicle.Last* columns so hot telemetry writes never
/// dirty the asset row and multiple devices/roles can be represented later.
/// Lean row, like TelemetryEvent.
/// </summary>
public class TelemetryState
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid DeviceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AltitudeM { get; set; }
    public double? SpeedKmh { get; set; }
    public double? HeadingDeg { get; set; }
    public int? Satellites { get; set; }
    public bool? Ignition { get; set; }
    public bool? EngineOn { get; set; }
    public double? FuelLevelPercent { get; set; }
    public double? FuelLevelLiters { get; set; }
    public double? OdometerKm { get; set; }
    public double? EngineHours { get; set; }
    public double? BatteryVoltage { get; set; }
    public string? DriverCardId { get; set; }
}

/// <summary>
/// Optional raw-payload archive (debugging/replay), written asynchronously and
/// independently of the normalized stream. Toggleable per vendor; off by default.
/// Lean row, like TelemetryEvent.
/// </summary>
public class RawPayload
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }       // null when the device could not be identified
    public Guid VendorId { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public string Channel { get; set; } = string.Empty; // endpoint/port/topic
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public string? ContentType { get; set; }
    public TelemetryParseStatus ParseStatus { get; set; } = TelemetryParseStatus.Unparsed;
    public string? FailureReason { get; set; }
}
