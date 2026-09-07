using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Application.DTOs;

/// <summary>
/// Event-derived safety scoring (scorecard groundwork v1): start at 100 and
/// deduct per raw DriverBehaviorEvent by type. Defined here so the Dashboard
/// endpoint and any future Driver Scorecard share one formula.
/// </summary>
public static class DriverSafetyScoring
{
    public static int Deduction(DriverBehaviorEventType type) => type switch
    {
        DriverBehaviorEventType.Drowsiness => 10,
        DriverBehaviorEventType.Distraction => 8,
        DriverBehaviorEventType.PhoneUsage => 6,
        DriverBehaviorEventType.HarshBraking => 5,
        DriverBehaviorEventType.HarshCornering => 5,
        DriverBehaviorEventType.HarshAcceleration => 4,
        DriverBehaviorEventType.ExcessiveIdling => 2,
        DriverBehaviorEventType.SosTriggered => 15,
        _ => 0
    };

    public static int Score(IEnumerable<DriverBehaviorEventType> eventTypes)
    {
        var total = 0;
        foreach (var t in eventTypes) total += Deduction(t);
        return Math.Max(0, 100 - total);
    }
}

/// <summary>One normalized driver-behavior / DMS event as the API projects it.</summary>
public class DriverBehaviorEventDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public Guid? DriverId { get; set; }

    /// <summary>Canonical event code (harsh_braking, drowsiness, sos_triggered, …).</summary>
    public string EventType { get; set; } = string.Empty;
    public string EventTypeName { get; set; } = string.Empty;

    /// <summary>0..1 AI confidence for detected events.</summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>AlertSeverity value this event maps to (for UI chips).</summary>
    public int Severity { get; set; }

    public DateTime EventTimeUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKmh { get; set; }

    /// <summary>Dashcam snapshot/clip reference at the moment of the event, when the vendor provided one.</summary>
    public string? MediaUrl { get; set; }
}

/// <summary>
/// One driver's event-derived safety score — computed from raw DriverBehaviorEvent
/// records (scorecard groundwork): 100 minus per-event deductions by type.
/// </summary>
public class DriverSafetyScoreDto
{
    public Guid DriverId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; } = 100;
    public int EventCount { get; set; }

    /// <summary>Count per canonical event code for the period.</summary>
    public Dictionary<string, int> Breakdown { get; set; } = new();
}