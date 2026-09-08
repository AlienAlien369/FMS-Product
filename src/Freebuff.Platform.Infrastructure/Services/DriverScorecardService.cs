using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Pure composite scoring math for the Driver Scorecard — no I/O, fully
/// deterministic, unit-tested to exact numbers.
///
///   Safety      = clamp(100 − round(Σ deduction(type)×(0.5+0.5×confidence) × 10 / max(1, completedTrips)), 0, 100)
///                 Rate-based per 10-trip baseline: a driver who drives 10× more
///                 must not look worse purely on event volume. Deductions reuse
///                 DriverSafetyScoring (one table, one owner).
///   Compliance  = round(0.5 × licenseScore + 0.5 × checkpointScore)
///                 license: 100 (none / >30d) · 70 (≤30d) · 40 (expired)
///                 checkpoint: 100 when nothing expected, else 100 × visited/expected
///   Punctuality = round((0.7 × onTimeRate + 0.3 × completionRate) × 100)
///                 onTimeRate over waypoints with BOTH expected and actual arrival
///                 (no comparable data → neutral 100); completion = completed / (completed+cancelled+aborted)
///   Behavior    = round(clamp(100 − idlePenalty − fuelPenalty, 0, 100))
///                 idlePenalty = min(30, max(0, idleRatio − 0.05) × 150); fuelPenalty above 30 L/100km
///   Composite   = round((wS·safety + wC·compliance + wP·punctuality + wB·behavior) / Σweights)
///   Insufficient data: fewer than MinActivityTrips completed trips in the window
///                 → every category null. A low-activity driver must never show a
///                 misleadingly confident number.
/// </summary>
public static class DriverScorecardMath
{
    public const int MinActivityTrips = 3;
    public const double DefaultSafetyWeight = 0.40;
    public const double DefaultComplianceWeight = 0.25;
    public const double DefaultPunctualityWeight = 0.20;
    public const double DefaultBehaviorWeight = 0.15;
    public const double ScoreDropThreshold = 15.0;
    public const double CriticalCompositeThreshold = 40.0;

    public sealed record EventDeduction(DriverBehaviorEventType Type, double Confidence);
    public sealed record TripActivity(TripStatus Status, int? IdleMinutes, TimeSpan? ActualDuration,
        decimal? FuelUsedLiters, decimal? ActualDistance, int? CheckpointsVisited, int? CheckpointsExpected);
    public sealed record WaypointTiming(DateTime? ExpectedArrival, DateTime? ActualArrival);

    public sealed record CategoryScores(
        decimal? Safety, decimal? Compliance, decimal? Punctuality, decimal? Behavior, decimal? Composite,
        int TripCount, int EventCount, bool InsufficientData);

    public static CategoryScores Compute(
        IReadOnlyList<EventDeduction> events,
        IReadOnlyList<TripActivity> trips,
        IReadOnlyList<WaypointTiming> waypoints,
        DateTime? licenseExpiry,
        DateTime now,
        double safetyWeight, double complianceWeight, double punctualityWeight, double behaviorWeight)
    {
        var completed = trips.Where(t => t.Status == TripStatus.Completed).ToList();
        var tripCount = completed.Count;
        var eventCount = events.Count;

        if (tripCount < MinActivityTrips)
            return new CategoryScores(null, null, null, null, null, tripCount, eventCount, InsufficientData: true);

        // ── Safety (rate-based) ────────────────────────────────────────────
        double totalDeduction = 0;
        foreach (var e in events)
            totalDeduction += DriverSafetyScoring.Deduction(e.Type) * (0.5 + 0.5 * Math.Clamp(e.Confidence, 0, 1));
        var safety = Math.Clamp(100 - Round(totalDeduction * 10.0 / Math.Max(1, tripCount)), 0, 100);

        // ── Compliance ─────────────────────────────────────────────────────
        var licenseScore = licenseExpiry switch
        {
            null => 100.0,
            var d when d <= now => 40.0,
            var d when d <= now.AddDays(30) => 70.0,
            _ => 100.0
        };
        var visited = completed.Sum(t => t.CheckpointsVisited ?? 0);
        var expected = completed.Sum(t => t.CheckpointsExpected ?? 0);
        var checkpointScore = expected == 0 ? 100.0 : 100.0 * visited / expected;
        var compliance = Round(0.5 * licenseScore + 0.5 * checkpointScore);

        // ── Punctuality ────────────────────────────────────────────────────
        var comparable = waypoints.Where(w => w.ExpectedArrival.HasValue && w.ActualArrival.HasValue).ToList();
        var onTime = comparable.Count(w => w.ActualArrival!.Value <= w.ExpectedArrival!.Value);
        var onTimeRate = comparable.Count == 0 ? 1.0 : (double)onTime / comparable.Count;
        var nonCompleted = trips.Count(t => t.Status is TripStatus.Cancelled or TripStatus.Aborted);
        var completionRate = (tripCount + nonCompleted) == 0 ? 1.0 : (double)tripCount / (tripCount + nonCompleted);
        var punctuality = Round((0.7 * onTimeRate + 0.3 * completionRate) * 100);

        // ── Behavior ───────────────────────────────────────────────────────
        var idleMinutes = completed.Sum(t => t.IdleMinutes ?? 0);
        var durationMinutes = completed.Sum(t => t.ActualDuration?.TotalMinutes ?? 0);
        var idleRatio = durationMinutes <= 0 ? 0.0 : idleMinutes / durationMinutes;
        var idlePenalty = Math.Min(30, Math.Max(0, (idleRatio - 0.05) * 150));
        var fuelLiters = completed.Sum(t => t.FuelUsedLiters ?? 0);
        var km = completed.Sum(t => t.ActualDistance ?? 0);
        double fuelPenalty = 0;
        if (fuelLiters > 0 && km > 0)
        {
            var efficiencyLPer100Km = (double)fuelLiters / (double)km * 100;
            if (efficiencyLPer100Km > 30) fuelPenalty = Math.Min(15, (efficiencyLPer100Km - 30) / 2);
        }
        var behavior = Math.Clamp(Round(100 - idlePenalty - fuelPenalty), 0, 100);

        // ── Composite (weighted average, weights normalized) ───────────────
        // All-zero weights are treated as "unconfigured" → the built-in defaults
        // apply wholesale, never a divide-by-zero or a silent zero score.
        var weightSum = safetyWeight + complianceWeight + punctualityWeight + behaviorWeight;
        if (weightSum <= 0)
        {
            safetyWeight = DefaultSafetyWeight;
            complianceWeight = DefaultComplianceWeight;
            punctualityWeight = DefaultPunctualityWeight;
            behaviorWeight = DefaultBehaviorWeight;
            weightSum = safetyWeight + complianceWeight + punctualityWeight + behaviorWeight;
        }
        var composite = Round((safetyWeight * safety + complianceWeight * compliance
            + punctualityWeight * punctuality + behaviorWeight * behavior) / weightSum);

        return new CategoryScores((decimal)safety, (decimal)compliance, (decimal)punctuality,
            (decimal)behavior, (decimal)composite, tripCount, eventCount, InsufficientData: false);
    }

    /// <summary>Whether a score drop (or a critically low score) warrants an alert.</summary>
    public static bool ShouldAlertScoreDrop(decimal? previousComposite, decimal currentComposite,
        double threshold = ScoreDropThreshold, double critical = CriticalCompositeThreshold)
        => currentComposite <= (decimal)critical
           || (previousComposite.HasValue && previousComposite.Value - currentComposite >= (decimal)threshold);

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Owns Driver Scorecard state: materialized DriverScorePeriod rows (compute-if-
/// stale, explicit recompute), ScoreWeightConfig resolution (company override →
/// platform default → built-ins), the fleet ranking, and the driver.score_drop
/// alert + company-admin notification (entitlement-gated like every alert type).
///
/// Scores are a CACHE over raw data (DriverBehaviorEvents, Trips, waypoints,
/// checkpoints, license expiry) — recompute touches only today's anchor, so a
/// weight-config change never silently rewrites history.
/// </summary>
public class DriverScorecardService
{
    public const string AlertTypeCode = "driver.score_drop";
    public const string AlertRecordType = "DriverScoreDrop";
    public static readonly TimeSpan Staleness = TimeSpan.FromHours(12);
    public static readonly string[] Windows = { "30d", "90d", "all" };

    private readonly ApplicationDbContext _db;
    private readonly IAlertTypeEnforcement _alertEnforcement;
    private readonly INotificationService? _notificationService;
    private readonly Func<DateTime> _clock;

    public DriverScorecardService(ApplicationDbContext db, IAlertTypeEnforcement alertEnforcement,
        INotificationService? notificationService = null, Func<DateTime>? clock = null)
    {
        _db = db;
        _alertEnforcement = alertEnforcement;
        _notificationService = notificationService;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public static (DateTime Start, DateTime End) WindowBounds(string window, DateTime now) => window switch
    {
        "30d" => (now.AddDays(-30), now),
        "90d" => (now.AddDays(-90), now),
        _ => (now.AddYears(-50), now) // "all"
    };

    // ── Weights ────────────────────────────────────────────────────────────
    public async Task<ScoreWeightConfigDto> GetWeightsAsync(Guid? companyId)
    {
        if (companyId.HasValue)
        {
            var company = await _db.ScoreWeightConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId.Value);
            if (company != null) return ToDto(company, "company");
        }
        var def = await _db.ScoreWeightConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == null);
        return def != null ? ToDto(def, "platform") : new ScoreWeightConfigDto
        {
            Safety = DriverScorecardMath.DefaultSafetyWeight,
            Compliance = DriverScorecardMath.DefaultComplianceWeight,
            Punctuality = DriverScorecardMath.DefaultPunctualityWeight,
            Behavior = DriverScorecardMath.DefaultBehaviorWeight,
            Source = "platform"
        };
    }

    public async Task<ScoreWeightConfigDto> SetWeightsAsync(Guid? companyId,
        double safety, double compliance, double punctuality, double behavior, string? actor)
    {
        var row = await _db.ScoreWeightConfigs.FirstOrDefaultAsync(c => c.CompanyId == companyId);
        if (row == null)
        {
            row = new ScoreWeightConfig { Id = Guid.NewGuid(), CompanyId = companyId };
            _db.ScoreWeightConfigs.Add(row);
        }
        row.SafetyWeight = safety;
        row.ComplianceWeight = compliance;
        row.PunctualityWeight = punctuality;
        row.BehaviorWeight = behavior;
        row.UpdatedAt = _clock();
        row.UpdatedBy = actor;
        await _db.SaveChangesAsync();
        return ToDto(row, companyId == null ? "platform" : "company");
    }

    private static ScoreWeightConfigDto ToDto(ScoreWeightConfig c, string source) => new()
    {
        Safety = c.SafetyWeight,
        Compliance = c.ComplianceWeight,
        Punctuality = c.PunctualityWeight,
        Behavior = c.BehaviorWeight,
        Source = source
    };

    // ── Materialization ────────────────────────────────────────────────────
    /// <summary>
    /// Returns the current period for (driver, window), recomputing when the row
    /// is missing or stale. Materialized, not live: a page view reuses the cached
    /// row; recompute happens at most once per staleness window.
    /// </summary>
    public async Task<DriverScorePeriod> EnsureCurrentAsync(Guid driverId, string window)
    {
        var driver = await _db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driverId && !d.IsDeleted)
            ?? throw new KeyNotFoundException($"Driver {driverId} not found");
        var now = _clock();
        var today = now.Date;

        var row = await _db.DriverScorePeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DriverId == driverId && p.Window == window && p.AnchorDate == today);
        if (row != null && now - row.ComputedAt <= Staleness) return row;

        return await ComputeAndPersistAsync(driver, window, now);
    }

    /// <summary>Explicit recompute of all windows for a driver (after a weight-config change).</summary>
    public async Task<DriverScorePeriod> RecomputeAsync(Guid driverId)
    {
        var driver = await _db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driverId && !d.IsDeleted)
            ?? throw new KeyNotFoundException($"Driver {driverId} not found");
        var now = _clock();
        DriverScorePeriod? latest = null;
        foreach (var window in Windows)
            latest = await ComputeAndPersistAsync(driver, window, now);
        return latest!;
    }

    private async Task<DriverScorePeriod> ComputeAndPersistAsync(Driver driver, string window, DateTime now)
    {
        var (start, end) = WindowBounds(window, now);
        var today = now.Date;

        // Previous anchor (for the score-drop check) BEFORE we upsert today's row.
        var previous = window == "30d"
            ? await _db.DriverScorePeriods.AsNoTracking()
                .Where(p => p.DriverId == driver.Id && p.Window == "30d" && p.AnchorDate < today)
                .OrderByDescending(p => p.AnchorDate).ThenByDescending(p => p.ComputedAt)
                .FirstOrDefaultAsync()
            : null;

        var events = await _db.DriverBehaviorEvents.AsNoTracking()
            .Where(e => e.DriverId == driver.Id && e.EventTimeUtc >= start && e.EventTimeUtc <= end)
            .Select(e => new DriverScorecardMath.EventDeduction(e.EventType, e.Confidence))
            .ToListAsync();

        var trips = await _db.Trips.AsNoTracking()
            .Where(t => t.DriverId == driver.Id && !t.IsDeleted
                && (t.Status == TripStatus.Completed
                    ? t.ActualEndTime >= start && t.ActualEndTime <= end
                    : (t.Status == TripStatus.Cancelled || t.Status == TripStatus.Aborted) && t.UpdatedAt >= start && t.UpdatedAt <= end))
            .Select(t => new
            {
                t.Id, t.Status, t.IdleMinutes, t.ActualStartTime, t.ActualEndTime, t.ActualDuration,
                t.FuelUsedLiters, t.ActualDistance
            })
            .ToListAsync();
        var tripIds = trips.Select(t => t.Id).ToList();

        var waypoints = tripIds.Count == 0
            ? new List<DriverScorecardMath.WaypointTiming>()
            : await _db.TripWaypoints.AsNoTracking()
                .Where(w => tripIds.Contains(w.TripId))
                .Select(w => new DriverScorecardMath.WaypointTiming(w.ExpectedArrival, w.ActualArrival))
                .ToListAsync();

        var checkpointAgg = tripIds.Count == 0
            ? new List<(Guid TripId, int Visited, int Expected)>()
            : (await _db.TripGeofences.AsNoTracking()
                .Where(g => tripIds.Contains(g.TripId) && g.Role == TripGeofenceRole.Checkpoint)
                .Select(g => new { g.TripId, Visited = g.Visited == true ? 1 : 0, Expected = 1 })
                .ToListAsync())
                .Select(x => (TripId: x.TripId, Visited: x.Visited, Expected: x.Expected))
                .ToList();
        var checkpointByTrip = checkpointAgg
            .GroupBy(c => c.TripId)
            .Select(g => (TripId: g.Key, Visited: g.Sum(x => x.Visited), Expected: g.Sum(x => x.Expected)))
            .ToDictionary(x => x.TripId, x => x);

        var tripActivities = trips.Select(t => new DriverScorecardMath.TripActivity(
            t.Status, t.IdleMinutes,
            t.ActualStartTime.HasValue && t.ActualEndTime.HasValue
                ? t.ActualEndTime - t.ActualStartTime
                : t.ActualDuration,
            t.FuelUsedLiters, t.ActualDistance,
            checkpointByTrip.TryGetValue(t.Id, out var c) ? c.Visited : 0,
            checkpointByTrip.TryGetValue(t.Id, out var c2) ? c2.Expected : 0)).ToList();

        var weights = await GetWeightsAsync(driver.CompanyId);
        var result = DriverScorecardMath.Compute(events, tripActivities, waypoints,
            (await _db.Drivers.AsNoTracking().Where(d => d.Id == driver.Id).Select(d => d.LicenseExpiry).FirstOrDefaultAsync()),
            now, weights.Safety, weights.Compliance, weights.Punctuality, weights.Behavior);

        var distance = tripActivities.Sum(t => t.ActualDistance ?? 0);

        // Upsert TODAY's anchor: a recompute later the same day must refresh the
        // row, never duplicate it (unique index on Driver+Window+AnchorDate), and
        // must never touch earlier anchors — historical periods stay immutable
        // until an explicit recompute of that day.
        var period = await _db.DriverScorePeriods.FirstOrDefaultAsync(p =>
            p.DriverId == driver.Id && p.Window == window && p.AnchorDate == today)
            ?? new DriverScorePeriod { Id = Guid.NewGuid() };
        period.CompanyId = driver.CompanyId;
        period.DriverId = driver.Id;
        period.Window = window;
        period.AnchorDate = today;
        period.PeriodStart = start;
        period.PeriodEnd = end;
        period.SafetyScore = result.Safety;
        period.ComplianceScore = result.Compliance;
        period.PunctualityScore = result.Punctuality;
        period.BehaviorScore = result.Behavior;
        period.CompositeScore = result.Composite;
        period.TripCount = result.TripCount;
        period.EventCount = result.EventCount;
        period.DistanceKm = distance > 0 ? distance : null;
        period.ComputedAt = now;

        if (_db.Entry(period).State == EntityState.Detached)
            _db.DriverScorePeriods.Add(period);
        await _db.SaveChangesAsync();

        if (window == "30d" && result.Composite.HasValue)
            await RaiseScoreDropAlertIfNeededAsync(driver, previous, result.Composite.Value, now);

        return period;
    }

    private async Task RaiseScoreDropAlertIfNeededAsync(Driver driver, DriverScorePeriod? previous,
        decimal currentComposite, DateTime now)
    {
        if (!DriverScorecardMath.ShouldAlertScoreDrop(previous?.CompositeScore, currentComposite)) return;
        if (!await _alertEnforcement.IsEntitledAsync(driver.CompanyId, AlertTypeCode)) return;

        // Throttle: one score-drop alert per driver per 24h.
        var since = now.AddHours(-24);
        var recent = await _db.Alerts.AsNoTracking().AnyAsync(a => !a.IsDeleted
            && a.DriverId == driver.Id && a.AlertType == AlertRecordType && a.CreatedAt >= since);
        if (recent) return;

        var title = "Driver Score Dropped";
        var message = previous?.CompositeScore.HasValue == true
            ? $"Driver {driver.FirstName} {driver.LastName}'s composite score dropped from {previous.CompositeScore} to {currentComposite} in the 30-day window."
            : $"Driver {driver.FirstName} {driver.LastName}'s composite score is {currentComposite} — below the critical threshold.";
        _db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            AlertType = AlertRecordType,
            Severity = AlertSeverity.High,
            Title = title,
            Message = message,
            CompanyId = driver.CompanyId,
            TenantId = driver.CompanyId,
            DriverId = driver.Id
        });
        await _db.SaveChangesAsync();

        if (_notificationService != null)
        {
            await _notificationService.NotifyCompanyAdminsAsync(driver.CompanyId, "alert.fired",
                title, message, (int)AlertSeverity.High, "Driver", driver.Id, $"/drivers/{driver.Id}");
        }
    }

    // ── Ranking / leaderboard ──────────────────────────────────────────────
    /// <summary>Latest materialized period per driver for a window (null scores last).</summary>
    public async Task<List<DriverScoreRankingDto>> GetRankingAsync(string window, IReadOnlyCollection<Guid> companyIds,
        decimal? minScore = null, int limit = 100)
    {
        var periods = await _db.DriverScorePeriods.AsNoTracking()
            .Where(p => p.Window == window && companyIds.Contains(p.CompanyId))
            .GroupBy(p => p.DriverId)
            .Select(g => g.OrderByDescending(p => p.AnchorDate).ThenByDescending(p => p.ComputedAt).First())
            .ToListAsync();
        if (periods.Count == 0) return new List<DriverScoreRankingDto>();

        var driverIds = periods.Select(p => p.DriverId).ToList();
        var names = await _db.Drivers.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id) && !d.IsDeleted)
            .Select(d => new { d.Id, Name = d.FirstName + " " + d.LastName })
            .ToDictionaryAsync(n => n.Id, n => n.Name);

        var result = periods.Select(p => new DriverScoreRankingDto
        {
            DriverId = p.DriverId,
            Name = names.TryGetValue(p.DriverId, out var n) ? n : "Unknown driver",
            Composite = p.CompositeScore,
            Safety = p.SafetyScore,
            Compliance = p.ComplianceScore,
            Punctuality = p.PunctualityScore,
            Behavior = p.BehaviorScore,
            InsufficientData = !p.CompositeScore.HasValue,
            TripCount = p.TripCount
        }).ToList();

        if (minScore.HasValue)
            result = result.Where(r => r.Composite.HasValue && r.Composite.Value >= minScore.Value).ToList();

        return result
            .OrderByDescending(r => r.Composite.HasValue)
            .ThenByDescending(r => r.Composite)
            .Take(limit)
            .ToList();
    }

    /// <summary>Full scorecard DTO for the detail view (scores + trend + explainability feed).</summary>
    public async Task<DriverScorecardDto> GetScorecardAsync(Guid driverId, string window)
    {
        var driver = await _db.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driverId && !d.IsDeleted)
            ?? throw new KeyNotFoundException($"Driver {driverId} not found");
        var period = await EnsureCurrentAsync(driverId, window);
        var weights = await GetWeightsAsync(driver.CompanyId);

        var trend = await _db.DriverScorePeriods.AsNoTracking()
            .Where(p => p.DriverId == driverId && p.Window == window && p.CompositeScore.HasValue)
            .OrderByDescending(p => p.AnchorDate)
            .Take(10)
            .Select(p => new DriverScoreTrendPointDto { AnchorDate = p.AnchorDate, Composite = p.CompositeScore, Safety = p.SafetyScore })
            .ToListAsync();
        trend.Reverse();

        var (start, end) = WindowBounds(window, _clock());
        var topEvents = await _db.DriverBehaviorEvents.AsNoTracking()
            .Where(e => e.DriverId == driverId && e.EventTimeUtc >= start && e.EventTimeUtc <= end)
            .OrderByDescending(e => e.EventTimeUtc)
            .Take(50)
            .Select(e => new { e.Id, e.EventType, e.Confidence, e.EventTimeUtc, e.MediaUrl })
            .ToListAsync();
        var top = topEvents
            .Select(e => new DriverScoreEventDto
            {
                Id = e.Id,
                EventType = DriverBehaviorCatalog.Spec(e.EventType).CanonicalCode,
                EventTypeName = DriverBehaviorCatalog.Spec(e.EventType).AlertName,
                Confidence = e.Confidence,
                EventTimeUtc = e.EventTimeUtc,
                MediaUrl = e.MediaUrl,
                Deduction = DriverSafetyScoring.Deduction(e.EventType) * (0.5 + 0.5 * Math.Clamp(e.Confidence, 0, 1))
            })
            .OrderByDescending(e => e.Deduction)
            .Take(5)
            .ToList();

        return new DriverScorecardDto
        {
            DriverId = driver.Id,
            Name = driver.FirstName + " " + driver.LastName,
            Window = window,
            InsufficientData = !period.CompositeScore.HasValue,
            TripCount = period.TripCount,
            EventCount = period.EventCount,
            DistanceKm = period.DistanceKm,
            Weights = weights,
            Scores = new DriverScorecardScoresDto
            {
                Safety = period.SafetyScore,
                Compliance = period.ComplianceScore,
                Punctuality = period.PunctualityScore,
                Behavior = period.BehaviorScore,
                Composite = period.CompositeScore
            },
            Trend = trend,
            TopEvents = top
        };
    }
}