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

    /// <summary>Hardware speed-governor limit reported by the device (informational — policy alerting uses VehicleSpeedPolicy).</summary>
    public double? SpeedGovernorLimitKmh { get; set; }

    /// <summary>JSON array of canonical alert codes (adapter-normalized).</summary>
    public string? AlertsJson { get; set; }

    /// <summary>JSON map of per-channel sensor readings not yet promoted to columns (e.g. {"temp1":23.1}).</summary>
    public string? SensorsJson { get; set; }

    /// <summary>JSON — anything else the adapter produced.</summary>
    public string? ExtrasJson { get; set; }

    public Guid? RawPayloadId { get; set; }

    public ICollection<TyrePressureReading> TyrePressureReadings { get; set; } = new List<TyrePressureReading>();
}

/// <summary>
/// One tyre's pressure reading within a telemetry snapshot — a vehicle has
/// multiple tyres, so this is a one-to-many child of TelemetryEvent, never a
/// flat field. Position is the canonical TyrePosition; pressure is always bar.
/// </summary>
public class TyrePressureReading
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TelemetryEventId { get; set; }
    public TelemetryEvent TelemetryEvent { get; set; } = null!;

    /// <summary>Denormalized from the telemetry event (hot queries avoid the join).</summary>
    public Guid VehicleId { get; set; }

    public TyrePosition Position { get; set; }
    public double PressureBar { get; set; }
    public double? TemperatureC { get; set; }

    /// <summary>Same as the parent event's time (device-reported or receive time).</summary>
    public DateTime EventTimeUtc { get; set; }
}

/// <summary>
/// Hourly min/max/avg rollup of raw sensor readings (speed, per-tyre pressure)
/// — the retention path: raw rows live ~30 days, then are folded into one row
/// per vehicle+sensor+hour and the raw rows are dropped. Kept ~12 months.
/// </summary>
public class TelemetryRollupHourly
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid VehicleId { get; set; }

    /// <summary>"speed" | "tyre" — which sensor stream this bucket aggregates.</summary>
    public string SensorType { get; set; } = "speed";

    /// <summary>Set when SensorType = "tyre"; null for vehicle-level speed.</summary>
    public TyrePosition? TyrePosition { get; set; }

    public DateTime HourBucketUtc { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double AvgValue { get; set; }
    public int ReadingCount { get; set; }
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

    /// <summary>Hardware speed-governor limit (informational; policy alerting uses VehicleSpeedPolicy).</summary>
    public double? SpeedGovernorLimitKmh { get; set; }
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
