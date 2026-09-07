namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// One tyre's pressure reading within a telemetry snapshot (canonical shape —
/// consumer code never sees vendor formats). Position is a canonical label
/// (front_left, front_right, rear_left, rear_right, spare); adapters map their
/// vendor labels onto it. Pressure is always bar.
/// </summary>
public sealed class NormalizedTyrePressure
{
    /// <summary>Canonical position: front_left | front_right | rear_left | rear_right | spare.</summary>
    public string Position { get; init; } = string.Empty;

    public double PressureBar { get; init; }

    /// <summary>Optional — reported only by sensors that include a temperature channel.</summary>
    public double? TemperatureC { get; init; }

    /// <summary>Sensor-reported time; null = use the parent telemetry event's time.</summary>
    public DateTime? TimestampUtc { get; init; }
}