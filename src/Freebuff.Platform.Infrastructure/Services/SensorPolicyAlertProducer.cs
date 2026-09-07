using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Ingestion.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Threshold-based alerting on continuous sensor streams (distinct from the
/// Driver Safety cluster's discrete-event model). Two policies, both resolved
/// by FleetPolicyService (per-vehicle override ?? company default):
///
///   vehicle.speed_limit_exceeded — the vehicle's speed vs the POLICY limit.
///     The hardware governor limit is informational only and NEVER alerts;
///     a company can enforce 60 km/h policy on a vehicle whose governor
///     allows 80. Fires at High when policy is exceeded.
///
///   vehicle.tyre_pressure_anomaly — each tyre vs the policy min/max range,
///     with severity tiers: warning at ≥10% outside the range, critical at
///     ≥20% outside OR a rapid drop (≥25% within 5 min) that catches a
///     blowout-in-progress before a static check would.
///
/// Entitlement-gated via CompanyAlertSubscription, throttled per vehicle+type
/// (severity-aware: a warning is suppressed by an existing warning or critical;
/// a critical only by an existing critical, so escalation always fires).
/// All mutation is staged in the caller's context (DeviceIngestionService owns
/// SaveChanges, so the telemetry row and its alert effects commit atomically).
/// </summary>
public class SensorPolicyAlertProducer
{
    public const string SpeedAlertCode = "vehicle.speed_limit_exceeded";
    public const string TyreAlertCode = "vehicle.tyre_pressure_anomaly";

    // Tyre severity tiers: 10% outside range → warning, 20% → critical.
    public const double WarningDeviationPct = 0.10;
    public const double CriticalDeviationPct = 0.20;
    // Rapid loss: ≥25% drop vs the previous reading within 5 minutes → critical.
    public const double RapidLossDropPct = 0.25;
    public static readonly TimeSpan RapidLossWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _db;
    private readonly IAlertTypeEnforcement _alertEnforcement;
    private readonly INotificationService? _notificationService;
    private readonly FleetPolicyService _policies;
    private readonly Func<DateTime> _clock;

    public SensorPolicyAlertProducer(ApplicationDbContext db, IAlertTypeEnforcement alertEnforcement,
        FleetPolicyService policies, INotificationService? notificationService = null, Func<DateTime>? clock = null)
    {
        _db = db;
        _alertEnforcement = alertEnforcement;
        _policies = policies;
        _notificationService = notificationService;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Evaluates one accepted telemetry snapshot against the vehicle's policies
    /// and stages any alerts (speed-over-policy + per-tyre pressure anomalies).
    /// Readings are persisted by the caller; this class only raises alerts.
    /// </summary>
    public async Task ProcessSnapshotAsync(Guid companyId, Guid? vehicleId, double? speedKmh,
        IReadOnlyList<NormalizedTyrePressure> tyres, double? latitude, double? longitude, DateTime at)
    {
        if (!vehicleId.HasValue) return; // unassigned device — no policy context

        var vehicle = await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId.Value && !v.IsDeleted);
        if (vehicle == null) return;

        if (speedKmh.HasValue)
            await EvaluateSpeedAsync(companyId, vehicle, speedKmh.Value, latitude, longitude, at);

        if (tyres.Count > 0)
            await EvaluateTyresAsync(companyId, vehicle, tyres, latitude, longitude, at);
    }

    private async Task EvaluateSpeedAsync(Guid companyId, Vehicle vehicle, double speedKmh,
        double? latitude, double? longitude, DateTime at)
    {
        var policy = await _policies.GetSpeedPolicyAsync(companyId, vehicle);
        if (!policy.MaxKmh.HasValue || speedKmh <= policy.MaxKmh.Value) return;

        if (!await _alertEnforcement.IsEntitledAsync(companyId, SpeedAlertCode)) return;            if (await RecentAlertExistsAsync(companyId, vehicle.Id, SpeedAlertCode, AlertSeverity.High, at)) return;

            var title = $"Speed limit exceeded — vehicle {vehicle.RegistrationNumber}";
            var message = $"Vehicle {vehicle.RegistrationNumber} was clocked at {speedKmh:0} km/h — above the {policy.MaxKmh:0} km/h policy limit{FormatPosition(latitude, longitude)}.";
            await StageAlertAsync(companyId, vehicle.Id, SpeedAlertCode, title, message, AlertSeverity.High, latitude, longitude, at);
    }

    private async Task EvaluateTyresAsync(Guid companyId, Vehicle vehicle, IReadOnlyList<NormalizedTyrePressure> tyres,
        double? latitude, double? longitude, DateTime at)
    {
        var policy = await _policies.GetTyrePolicyAsync(companyId, vehicle);
        if (!policy.MinBar.HasValue || !policy.MaxBar.HasValue) return;
        if (!await _alertEnforcement.IsEntitledAsync(companyId, TyreAlertCode)) return;

        foreach (var tyre in tyres)
        {
            if (!TryParsePosition(tyre.Position, out var position)) continue;

            var (severity, reason) = await ClassifyAsync(companyId, vehicle.Id, position, tyre.PressureBar,
                policy.MinBar.Value, policy.MaxBar.Value, at);

            if (severity == null) continue;
            if (await RecentAlertExistsAsync(companyId, vehicle.Id, TyreAlertCode, severity.Value, at)) continue;

            var positionLabel = PositionLabel(position);
            var title = $"{severity.Value} tyre pressure — vehicle {vehicle.RegistrationNumber}";
            var message = $"{positionLabel} tyre of vehicle {vehicle.RegistrationNumber}: {tyre.PressureBar:0.00} bar ({reason})."
                + (tyre.TemperatureC.HasValue ? $" Temperature {tyre.TemperatureC:0}°C." : string.Empty)
                + FormatPosition(latitude, longitude);
            await StageAlertAsync(companyId, vehicle.Id, TyreAlertCode, title, message, severity.Value, latitude, longitude, at);
        }
    }

    /// <summary>
    /// Returns (null, _) when within range; (Warning|Critical, reason) otherwise.
    /// Critical wins when the deviation crosses the 20% tier AND a rapid loss is
    /// detected — the static check and the blowout check run together so a fast
    /// drop is flagged critical even while still nominally inside the range.
    /// </summary>
    internal async Task<(AlertSeverity? Severity, string Reason)> ClassifyAsync(Guid companyId, Guid vehicleId,
        TyrePosition position, double pressureBar, double minBar, double maxBar, DateTime at)
    {
        var previous = await _db.TyrePressureReadings.AsNoTracking()
            .Where(r => r.TenantId == companyId && r.VehicleId == vehicleId && r.Position == position
                && r.EventTimeUtc >= at - RapidLossWindow && r.EventTimeUtc < at)
            .OrderByDescending(r => r.EventTimeUtc)
            .Select(r => new { r.PressureBar, r.EventTimeUtc })
            .FirstOrDefaultAsync();
        return ClassifyShared(pressureBar, minBar, maxBar, previous?.PressureBar, previous?.EventTimeUtc, at);
    }

    /// <summary>
    /// Single classification rule shared by the alert pipeline AND the live
    /// sensors surface (so the UI color can never disagree with the alert
    /// severity): static deviation tiers (≥10% → warning, ≥20% → critical) plus
    /// the temporal rapid-loss check (≥25% drop vs the previous reading within
    /// the window → critical, regardless of the static tier).
    /// </summary>
    public static (AlertSeverity? Severity, string Reason) ClassifyShared(double pressureBar, double minBar, double maxBar,
        double? previousPressureBar, DateTime? previousAt, DateTime at)
    {
        var deviationReason = ClassifyStatic(pressureBar, minBar, maxBar, out var staticSeverity);

        if (previousPressureBar is > 0 && previousAt.HasValue
            && previousAt.Value >= at - RapidLossWindow && previousAt.Value < at)
        {
            var drop = (previousPressureBar.Value - pressureBar) / previousPressureBar.Value;
            if (drop >= RapidLossDropPct)
                return (AlertSeverity.High,
                    $"rapid pressure loss — dropped {drop * 100:0}% within {RapidLossWindow.TotalMinutes:0} min");
        }

        if (staticSeverity.HasValue) return (staticSeverity.Value, deviationReason!);
        return (null, string.Empty);
    }

    /// <summary>
    /// Static deviation tiers, shared with the vehicle sensors endpoint so the
    /// live UI colors and the alert severity can never disagree: warning at
    /// ≥10% outside the range, critical at ≥20%.
    /// </summary>
    public static string? ClassifyStatic(double pressureBar, double minBar, double maxBar, out AlertSeverity? severity)
    {
        severity = null;
        double deviation;
        string reason;
        if (pressureBar < minBar)
        {
            deviation = (minBar - pressureBar) / minBar;
            reason = $"{pressureBar:0.00} bar is {(deviation * 100):0}% below the {minBar:0.00} bar minimum";
        }
        else if (pressureBar > maxBar)
        {
            deviation = (pressureBar - maxBar) / maxBar;
            reason = $"{pressureBar:0.00} bar is {(deviation * 100):0}% above the {maxBar:0.00} bar maximum";
        }
        else
        {
            return null;
        }

        // Tiers map onto the alert severity scale: warning tier → Medium, critical tier → High.
        severity = deviation >= CriticalDeviationPct ? AlertSeverity.High
            : deviation >= WarningDeviationPct ? AlertSeverity.Medium
            : (AlertSeverity?)null;
        return reason;
    }



    private async Task StageAlertAsync(Guid companyId, Guid vehicleId, string code, string title, string message,
        AlertSeverity severity, double? latitude, double? longitude, DateTime at)
    {
        _db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            AlertType = code,
            Severity = severity,
            Title = title,
            Message = message,
            CompanyId = companyId,
            TenantId = companyId,
            VehicleId = vehicleId,
            Latitude = latitude,
            Longitude = longitude
        });
        if (_notificationService != null)
        {
            await _notificationService.NotifyCompanyAdminsAsync(companyId, "alert.fired", title, message,
                (int)severity, "Vehicle", vehicleId, $"/vehicles/{vehicleId}");
        }
    }

    /// <summary>Severity-aware throttle: a warning is suppressed by an existing warning OR critical; a critical only by an existing critical.</summary>
    private async Task<bool> RecentAlertExistsAsync(Guid companyId, Guid vehicleId, string code,
        AlertSeverity severity, DateTime at)
    {
        var since = _clock().Add(-ThrottleWindow);
        return await _db.Alerts.AsNoTracking().AnyAsync(a => !a.IsDeleted
            && a.CompanyId == companyId && a.VehicleId == vehicleId
            && a.AlertType == code && (int)a.Severity >= (int)severity
            && a.CreatedAt >= since);
    }

    private static string FormatPosition(double? latitude, double? longitude)
        => latitude.HasValue && longitude.HasValue ? $" at {latitude:0.00000},{longitude:0.00000}" : string.Empty;

    public static bool TryParsePosition(string label, out TyrePosition position)
    {
        switch (label.Trim().ToLowerInvariant().Replace("-", "_"))
        {
            case "front_left": position = TyrePosition.FrontLeft; return true;
            case "front_right": position = TyrePosition.FrontRight; return true;
            case "rear_left": position = TyrePosition.RearLeft; return true;
            case "rear_right": position = TyrePosition.RearRight; return true;
            case "spare": position = TyrePosition.Spare; return true;
            default: position = default; return false;
        }
    }

    public static string PositionLabel(TyrePosition position) => position switch
    {
        TyrePosition.FrontLeft => "Front-left",
        TyrePosition.FrontRight => "Front-right",
        TyrePosition.RearLeft => "Rear-left",
        TyrePosition.RearRight => "Rear-right",
        TyrePosition.Spare => "Spare",
        _ => position.ToString()
    };
}