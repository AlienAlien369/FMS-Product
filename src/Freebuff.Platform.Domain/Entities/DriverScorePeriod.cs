namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// A materialized Driver Scorecard period — one row per (DriverId, Window,
/// AnchorDate). Windows are rolling ("30d", "90d", "all") anchored at a compute
/// day, so the trend is the recent AnchorDate rows for a window, not a single
/// ever-changing live number.
///
/// Scores are ALWAYS re-derivable from raw data (DriverBehaviorEvents, Trips,
/// TripWaypoints, TripGeofences, Driver.LicenseExpiry) — this row is a cache,
/// never the source of truth. Recompute after a weight-config change updates
/// only TODAY's anchor; historical anchors stay untouched until an explicit
/// recompute of that period (see DriverScorecardService).
///
/// Null category scores = insufficient data (below the minimum-activity
/// threshold) — a low-activity driver must never show a misleading number.
/// Lean row (no BaseEntity), like DriverBehaviorEvent: high write volume,
/// recomputed in place, no soft-delete semantics.
/// </summary>
public class DriverScorePeriod
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DriverId { get; set; }

    /// <summary>Rolling window this period covers: "30d" | "90d" | "all".</summary>
    public string Window { get; set; } = "30d";

    /// <summary>Day the computation anchored on (trend axis).</summary>
    public DateTime AnchorDate { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    // 0–100 each; null = insufficient data for this window.
    public decimal? SafetyScore { get; set; }
    public decimal? ComplianceScore { get; set; }
    public decimal? PunctualityScore { get; set; }
    public decimal? BehaviorScore { get; set; }
    public decimal? CompositeScore { get; set; }

    /// <summary>Activity evidence behind the scores (rate normalization + the insufficient-data gate).</summary>
    public int TripCount { get; set; }
    public int EventCount { get; set; }
    public decimal? DistanceKm { get; set; }

    public DateTime ComputedAt { get; set; }

    // Navigation (projection convenience only).
    public Driver? Driver { get; set; }
    public Company? Company { get; set; }
}