namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// Position sample in the common internal schema. Every vendor adapter must
/// translate its raw payload into this shape — consumer code never sees vendor
/// formats. Not all vendors fill all fields; null = not provided by this vendor.
/// </summary>
public sealed class NormalizedTelemetry
{
    public DeviceIdentity Device { get; init; }

    /// <summary>Device-reported event time (UTC). Null = use receive time.</summary>
    public DateTime? EventTimeUtc { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? AltitudeM { get; init; }
    public double? SpeedKmh { get; init; }
    public double? HeadingDeg { get; init; }
    public int? Satellites { get; init; }
    public double? Hdop { get; init; }

    public bool? Ignition { get; init; }
    public bool? EngineOn { get; init; }
    public double? FuelLevelPercent { get; init; }
    public double? FuelLevelLiters { get; init; }
    public double? OdometerKm { get; init; }
    public double? EngineHours { get; init; }
    public double? BatteryVoltage { get; init; }
    public string? DriverCardId { get; init; }

    /// <summary>Canonical alert codes ONLY (e.g. "overspeed", "geofence-exit") — the adapter normalizes vendor alerts.</summary>
    public IReadOnlyList<string> Alerts { get; init; } = Array.Empty<string>();

    /// <summary>Per-channel sensor readings not in the common schema (e.g. temperature channels, aux inputs).</summary>
    public IReadOnlyDictionary<string, double> Sensors { get; init; } = new Dictionary<string, double>();

    /// <summary>Anything else the vendor provided that the schema doesn't promote yet.</summary>
    public IReadOnlyDictionary<string, object?> Extras { get; init; } = new Dictionary<string, object?>();
}
