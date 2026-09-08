using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Services;
using Xunit;

namespace Freebuff.Platform.Tests.DriverScorecard;

/// <summary>
/// The Driver Scorecard composite scoring math, pinned to exact numbers:
///   Safety      = 100 − round(Σ deductions(type)×(0.5+0.5×confidence) × 10 / max(1, completedTrips))
///                 (rate-based per 10-trip baseline — a driver who drives 10× more
///                  must not look worse purely on event volume)
///   Compliance  = round(0.5×license + 0.5×checkpoint)
///   Punctuality = round((0.7×onTimeRate + 0.3×completionRate) × 100)
///   Behavior    = round(clamp(100 − idlePenalty − fuelPenalty, 0, 100))
///   Composite   = round(wS·safety + wC·compliance + wP·punctuality + wB·behavior), weights normalized
///   Insufficient data: fewer than 3 completed trips in the window → all categories null.
/// </summary>
public class DriverScorecardMathTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static DriverScorecardMath.TripActivity Trip(TripStatus status, int? idleMinutes = null, int? durationMin = null,
        decimal? fuel = null, decimal? km = null, int? checkpointsVisited = null, int? checkpointsExpected = null)
        => new(status, idleMinutes, durationMin.HasValue ? TimeSpan.FromMinutes(durationMin.Value) : null,
            fuel, km, checkpointsVisited, checkpointsExpected);

    private static DriverScorecardMath.WaypointTiming Wp(DateTime? expected, DateTime? actual)
        => new(expected, actual);

    private static DateTime T(int daysAgo, int minutesOffset = 0)
        => Now.AddDays(-daysAgo).AddMinutes(minutesOffset);

    // One drowsiness (10) + one harsh braking (5), both confidence 1.0 → 15 deductions.
    private static DriverScorecardMath.EventDeduction[] TwoEvents() =>
    [
        new(DriverBehaviorEventType.Drowsiness, 1.0),
        new(DriverBehaviorEventType.HarshBraking, 1.0),
    ];

    [Fact]
    public void Composite_Matches_Weighted_Average_Math()
    {
        // 3 completed trips: safety = 100 − round(15×10/3) = 50
        // license null → 100; no checkpoints → 100 → compliance 100
        // waypoints 1 on-time / 1 late / 1 no-data → onTime 0.5; completion 3/3 → punctuality (0.35+0.30)×100 = 65
        // idle 60min/300min = 0.2 → idlePenalty min(30, (0.2−0.05)×150)=22.5 → behavior 100−22.5 = 77.5 → 78
        var r = DriverScorecardMath.Compute(
            TwoEvents(),
            new[]
            {
                Trip(TripStatus.Completed, 60, 300),
                Trip(TripStatus.Completed, 60, 300),
                Trip(TripStatus.Completed, 60, 300),
            },
            new[] { Wp(T(1, -5), T(1, -6)), Wp(T(1, 0), T(1, 5)), Wp(null, T(1, 0)) },
            licenseExpiry: null, now: Now,
            safetyWeight: 0.40, complianceWeight: 0.25, punctualityWeight: 0.20, behaviorWeight: 0.15);

        Assert.False(r.InsufficientData);
        Assert.Equal(50m, r.Safety);
        Assert.Equal(100m, r.Compliance);
        Assert.Equal(65m, r.Punctuality);
        Assert.Equal(78m, r.Behavior);
        // 0.4×50 + 0.25×100 + 0.2×65 + 0.15×78 = 20 + 25 + 13 + 11.7 = 69.7 → 70
        Assert.Equal(70m, r.Composite);
        Assert.Equal(3, r.TripCount);
        Assert.Equal(2, r.EventCount);
    }

    [Fact]
    public void Safety_Is_Rate_Based_Not_Event_Volume()
    {
        // Same 2 events over 3 trips → 50; over 9 trips → 100 − round(15×10/9) = 100 − 17 = 83.
        var few = DriverScorecardMath.Compute(TwoEvents(),
            [Trip(TripStatus.Completed), Trip(TripStatus.Completed), Trip(TripStatus.Completed)],
            Array.Empty<DriverScorecardMath.WaypointTiming>(), null, Now,
            0.4, 0.25, 0.2, 0.15);
        var many = DriverScorecardMath.Compute(TwoEvents(),
            Enumerable.Range(0, 9).Select(_ => Trip(TripStatus.Completed)).ToArray(),
            Array.Empty<DriverScorecardMath.WaypointTiming>(), null, Now,
            0.4, 0.25, 0.2, 0.15);

        Assert.Equal(50m, few.Safety);
        Assert.Equal(83m, many.Safety);
        Assert.True(many.Safety > few.Safety, "more trips at the same event count must score HIGHER (rate-based)");
    }

    [Fact]
    public void Confidence_Weights_Deductions()
    {
        // Drowsiness at 0.5 confidence → 10 × (0.5+0.5×0.5) = 7.5; over 3 trips: 100 − round(7.5×10/3 = 25) = 75.
        var r = DriverScorecardMath.Compute(
            [new DriverScorecardMath.EventDeduction(DriverBehaviorEventType.Drowsiness, 0.5)],
            [Trip(TripStatus.Completed), Trip(TripStatus.Completed), Trip(TripStatus.Completed)],
            Array.Empty<DriverScorecardMath.WaypointTiming>(), null, Now, 0.4, 0.25, 0.2, 0.15);
        Assert.Equal(75m, r.Safety);
    }

    [Fact]
    public void Insufficient_Data_Below_Minimum_Activity_Suppresses_Scores()
    {
        var r = DriverScorecardMath.Compute(
            TwoEvents(),
            [Trip(TripStatus.Completed), Trip(TripStatus.Completed)], // only 2 trips
            Array.Empty<DriverScorecardMath.WaypointTiming>(), null, Now, 0.4, 0.25, 0.2, 0.15);

        Assert.True(r.InsufficientData);
        Assert.Null(r.Safety);
        Assert.Null(r.Compliance);
        Assert.Null(r.Punctuality);
        Assert.Null(r.Behavior);
        Assert.Null(r.Composite);
    }

    [Fact]
    public void Expired_License_Drags_Compliance()
    {
        // license expired → 40; no checkpoints → 100 → compliance = round(0.5×40+0.5×100) = 70
        var r = DriverScorecardMath.Compute(
            Array.Empty<DriverScorecardMath.EventDeduction>(),
            [Trip(TripStatus.Completed), Trip(TripStatus.Completed), Trip(TripStatus.Completed)],
            Array.Empty<DriverScorecardMath.WaypointTiming>(),
            licenseExpiry: T(10), now: Now, 0.4, 0.25, 0.2, 0.15);
        Assert.Equal(70m, r.Compliance);
    }

    [Fact]
    public void Missed_Checkpoints_Drag_Compliance()
    {
        // license null → 100; 3 of 4 checkpoints visited across 3 trips → 75 → compliance = round(87.5) = 88 (AwayFromZero)
        var r = DriverScorecardMath.Compute(
            Array.Empty<DriverScorecardMath.EventDeduction>(),
            [Trip(TripStatus.Completed, checkpointsVisited: 1, checkpointsExpected: 2),
             Trip(TripStatus.Completed, checkpointsVisited: 2, checkpointsExpected: 2),
             Trip(TripStatus.Completed)],
            Array.Empty<DriverScorecardMath.WaypointTiming>(), null, Now, 0.4, 0.25, 0.2, 0.15);
        Assert.Equal(88m, r.Compliance);
    }

    [Fact]
    public void Weight_Override_Changes_Composite_Without_Rewriting_Inputs()
    {
        var events = TwoEvents();
        var trips = new[]
        {
            Trip(TripStatus.Completed, 60, 300),
            Trip(TripStatus.Completed, 60, 300),
            Trip(TripStatus.Completed, 60, 300),
        };
        var wps = new[] { Wp(T(1, -5), T(1, -6)), Wp(T(1, 0), T(1, 5)), Wp(null, T(1, 0)) };

        var def = DriverScorecardMath.Compute(events, trips, wps, null, Now, 0.40, 0.25, 0.20, 0.15);
        // Hazardous-cargo weighting: safety 0.70, compliance 0.10, punctuality 0.10, behavior 0.10
        var hazmat = DriverScorecardMath.Compute(events, trips, wps, null, Now, 0.70, 0.10, 0.10, 0.10);

        Assert.Equal(70m, def.Composite);
        // 0.7×50 + 0.1×100 + 0.1×65 + 0.1×78 = 35 + 10 + 6.5 + 7.8 = 59.3 → 59
        Assert.Equal(59m, hazmat.Composite);
        // Raw category inputs are untouched by the weight change — only the blend moves.
        Assert.Equal(def.Safety, hazmat.Safety);
        Assert.Equal(def.Compliance, hazmat.Compliance);
    }

    [Fact]
    public void Score_Drop_And_Critical_Thresholds_Trigger_Alert()
    {
        Assert.True(DriverScorecardMath.ShouldAlertScoreDrop(previousComposite: 90m, currentComposite: 40m));
        Assert.True(DriverScorecardMath.ShouldAlertScoreDrop(previousComposite: 60m, currentComposite: 45m)); // drop = 15
        Assert.False(DriverScorecardMath.ShouldAlertScoreDrop(previousComposite: 55m, currentComposite: 45m)); // drop = 10, not critical
        Assert.True(DriverScorecardMath.ShouldAlertScoreDrop(previousComposite: null, currentComposite: 35m)); // critical alone
        Assert.False(DriverScorecardMath.ShouldAlertScoreDrop(previousComposite: null, currentComposite: 70m));
    }
}