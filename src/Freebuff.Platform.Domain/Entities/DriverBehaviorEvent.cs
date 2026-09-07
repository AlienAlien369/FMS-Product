using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// A single normalized driver-behavior / DMS event (harsh braking, drowsiness,
/// distraction, SOS press, …) extracted from a telemetry payload by a vendor
/// adapter. Lean append-only row like TelemetryEvent — deliberately no
/// BaseEntity baggage (no soft-delete/audit) because this is a high-volume
/// stream, not a mutable record.
///
/// Scorecard-ready by design: the row is denormalized with BOTH DriverId and
/// VehicleId at write time (DriverId resolved from the vehicle's active
/// in-progress trip), and indexed on (DriverId, EventTimeUtc) so the future
/// Driver Scorecard aggregates raw events by driver + date range without ever
/// needing to backfill history.
/// </summary>
public class DriverBehaviorEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DeviceId { get; set; }

    /// <summary>Vehicle at write time (from the active device assignment).</summary>
    public Guid? VehicleId { get; set; }

    /// <summary>
    /// Driver at write time — resolved from the vehicle's active InProgress
    /// trip when one exists, else null (vehicle driving with no dispatched trip,
    /// or the event arrived outside any trip window). Never backfilled later;
    /// scorecard aggregates join on this.
    /// </summary>
    public Guid? DriverId { get; set; }

    public DriverBehaviorEventType EventType { get; set; }

    /// <summary>0..1 — AI-detected events carry a model confidence; discrete sensor events default to 1.</summary>
    public double Confidence { get; set; } = 1.0;

    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;

    // Context at the moment of the event (from the same normalized fix).
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKmh { get; set; }

    /// <summary>Dashcam snapshot/clip reference at the moment of the event (URL, never inline binary).</summary>
    public string? MediaUrl { get; set; }

    /// <summary>Link back to the normalized telemetry row that carried this event.</summary>
    public Guid? TelemetryEventId { get; set; }

    // Navigation (projection convenience; no FK cascade — rows are append-only).
    public Vehicle? Vehicle { get; set; }
    public Driver? Driver { get; set; }
}

/// <summary>
/// Single source of truth mapping canonical behavior-event codes ↔ event types ↔
/// alert-registry specs. The adapter registry produces canonical codes; the
/// alert pipeline consumes the specs — the catalog is the only place the two
/// meet, so vendor logic can never leak into the pipeline.
/// </summary>
public sealed record DriverBehaviorSpec(
    string CanonicalCode,
    string AlertTypeCode,
    string AlertName,
    string AlertTitle,
    AlertSeverity Severity,
    bool NonMutablePriority,
    int ThrottleMinutes,
    string AlertRecordType)
{
}

public static class DriverBehaviorCatalog
{
    public static readonly IReadOnlyDictionary<string, DriverBehaviorEventType> EventTypeByCode =
        new Dictionary<string, DriverBehaviorEventType>(StringComparer.OrdinalIgnoreCase)
        {
            ["harsh_braking"] = DriverBehaviorEventType.HarshBraking,
            ["harsh_acceleration"] = DriverBehaviorEventType.HarshAcceleration,
            ["harsh_cornering"] = DriverBehaviorEventType.HarshCornering,
            ["excessive_idling"] = DriverBehaviorEventType.ExcessiveIdling,
            ["drowsiness"] = DriverBehaviorEventType.Drowsiness,
            ["distraction"] = DriverBehaviorEventType.Distraction,
            ["phone_usage"] = DriverBehaviorEventType.PhoneUsage,
            ["sos_triggered"] = DriverBehaviorEventType.SosTriggered
        };

    public static readonly IReadOnlyDictionary<DriverBehaviorEventType, DriverBehaviorSpec> SpecByType =
        new Dictionary<DriverBehaviorEventType, DriverBehaviorSpec>
        {
            [DriverBehaviorEventType.HarshBraking] = new(
                "harsh_braking", "driver.harsh_braking", "Harsh Braking", "Harsh braking detected",
                AlertSeverity.Medium, NonMutablePriority: false, ThrottleMinutes: 2, "DriverHarshBraking"),
            [DriverBehaviorEventType.HarshAcceleration] = new(
                "harsh_acceleration", "driver.harsh_acceleration", "Harsh Acceleration", "Harsh acceleration detected",
                AlertSeverity.Medium, NonMutablePriority: false, ThrottleMinutes: 2, "DriverHarshAcceleration"),
            [DriverBehaviorEventType.HarshCornering] = new(
                "harsh_cornering", "driver.harsh_cornering", "Harsh Cornering", "Harsh cornering detected",
                AlertSeverity.Medium, NonMutablePriority: false, ThrottleMinutes: 2, "DriverHarshCornering"),
            [DriverBehaviorEventType.ExcessiveIdling] = new(
                "excessive_idling", "driver.excessive_idling", "Excessive Idling", "Excessive idling detected",
                AlertSeverity.Low, NonMutablePriority: false, ThrottleMinutes: 10, "DriverExcessiveIdling"),
            [DriverBehaviorEventType.Drowsiness] = new(
                "drowsiness", "driver.drowsiness", "Drowsiness Detected", "Driver drowsiness detected",
                AlertSeverity.High, NonMutablePriority: false, ThrottleMinutes: 5, "DriverDrowsiness"),
            [DriverBehaviorEventType.Distraction] = new(
                "distraction", "driver.distraction", "Driver Distracted", "Driver distraction detected",
                AlertSeverity.High, NonMutablePriority: false, ThrottleMinutes: 5, "DriverDistraction"),
            [DriverBehaviorEventType.PhoneUsage] = new(
                "phone_usage", "driver.phone_usage", "Phone Usage While Driving", "Phone usage while driving detected",
                AlertSeverity.Medium, NonMutablePriority: false, ThrottleMinutes: 2, "DriverPhoneUsage"),
            [DriverBehaviorEventType.SosTriggered] = new(
                "sos_triggered", "driver.panic_button", "Panic Button (SOS)", "Panic button pressed",
                AlertSeverity.Critical, NonMutablePriority: true, ThrottleMinutes: 5, "DriverPanicButton")
        };

    /// <summary>Canonical adapter code → event type. Unknown codes return false (adapters may carry non-behavior extras).</summary>
    public static bool TryGetEventType(string code, out DriverBehaviorEventType eventType)
        => EventTypeByCode.TryGetValue(code.Trim(), out eventType);

    public static bool TryGetSpec(string code, out DriverBehaviorSpec spec)
    {
        if (TryGetEventType(code, out var eventType) && SpecByType.TryGetValue(eventType, out var s))
        {
            spec = s;
            return true;
        }
        spec = null!;
        return false;
    }

    public static DriverBehaviorSpec Spec(DriverBehaviorEventType eventType) => SpecByType[eventType];
}