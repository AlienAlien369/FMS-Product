namespace Freebuff.Platform.Ingestion.Contracts;

/// <summary>
/// One normalized driver-behavior / DMS event in the common schema. The vendor
/// adapter maps its own event codes onto <see cref="EventType"/> (a canonical
/// code from DriverBehaviorCatalog — e.g. "harsh_braking", "drowsiness",
/// "sos_triggered"); consumer code never sees vendor vocabulary.
/// </summary>
public sealed class NormalizedBehaviorEvent
{
    /// <summary>Canonical event code — see DriverBehaviorCatalog.EventTypeByCode.</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>0..1 model confidence for AI-detected events (defaults to 1 for discrete sensor events).</summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Optional dashcam snapshot/clip reference at the moment of the event (URL, not inline binary).</summary>
    public string? MediaUrl { get; init; }
}