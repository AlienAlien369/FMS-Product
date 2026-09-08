using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// Sensor-derived fuel analytics — runs on every accepted telemetry snapshot
/// that carries fuel data for a vehicle with a fuel sensor attached (Device
/// Abstraction Layer, VehicleDevice role FuelSensor).
///
/// What this produces, per snapshot:
///   1. A FuelConsumptionSnapshot row (lean, like telemetry) with the derived
///      pair (LitersConsumed, DistanceKm, ConsumptionLitersPer100km) computed
///      against the vehicle's previous snapshot — the source of truth for
///      consumption trends and the vehicle's efficiency baseline.
///   2. A materialized FuelRecord (source=sensor_derived) when a refuel is
///      recognized (level rose ≥ threshold with ignition off — a station stop),
///      so sensor-equipped and manual-only vehicles share one transaction
///      history on the reporting surface.
///   3. Entitlement-gated, throttled alerts:
///        fuel.theft_suspected      — large level drop, ~no distance, engine
///                                    never off (no legitimate refuel event).
///        fuel.low_level            — level below company threshold
///                                    (fuel.low_level_threshold_percent, default 10).
///        fuel.efficiency_degraded  — consumption worse than the vehicle's own
///                                    historical baseline by ≥25% (catches
///                                    developing mechanical issues early).
///
/// All mutation is staged in the caller's context (DeviceIngestionService owns
/// SaveChanges, so the telemetry row + snapshots + alerts commit atomically).
/// </summary>
public class FuelConsumptionAnalyzer
{
    public const string TheftAlertCode = "fuel.theft_suspected";
    public const string LowLevelAlertCode = "fuel.low_level";
    public const string EfficiencyAlertCode = "fuel.efficiency_degraded";

    public const string LowLevelConfigKey = "fuel.low_level_threshold_percent";
    public const double DefaultLowLevelThresholdPercent = 10;

    // Theft heuristic: a drop large enough to matter, over (almost) no distance,
    // with the engine never reported off between readings. A parked/ignition-off
    // level change is treated as a legitimate refuel — not a theft signal.
    public const double MinTheftDropLiters = 8;
    public const double TheftDropFractionOfTank = 0.15;
    public const double TheftMaxDistanceKm = 2;

    // Efficiency baseline: consumption worse than the vehicle's own recent
    // average by ≥25% flags degradation. Minimum distance avoids noise on
    // sub-kilometer pairs.
    public const double EfficiencyDegradationFactor = 1.25;
    public const double MinEfficiencyDistanceKm = 5;
    public const int BaselineWindowCount = 20;

    // Refuel recognition: level rose ≥ 5% of tank capacity (or 5 L) → a fill-up.
    public const double MinRefuelLiters = 5;
    public const double RefuelFractionOfTank = 0.05;

    // Per-code throttle windows (a theft worth repeating if it keeps dropping is
    // not — 6h; low level + efficiency are daily-grade signals).
    public static readonly TimeSpan TheftThrottle = TimeSpan.FromHours(6);
    public static readonly TimeSpan DailyThrottle = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext _db;
    private readonly IAlertTypeEnforcement _alertEnforcement;
    private readonly INotificationService? _notificationService;
    private readonly Func<DateTime> _clock;

    public FuelConsumptionAnalyzer(ApplicationDbContext db, IAlertTypeEnforcement alertEnforcement,
        INotificationService? notificationService = null, Func<DateTime>? clock = null)
    {
        _db = db;
        _alertEnforcement = alertEnforcement;
        _notificationService = notificationService;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Evaluates one accepted telemetry snapshot against the vehicle's previous
    /// fuel reading. Returns immediately when there is no fuel data or no
    /// assigned vehicle. Snapshots + alerts are staged into _db (the caller
    /// commits).
    /// </summary>
    public async Task ProcessSnapshotAsync(Guid companyId, Guid? vehicleId, Guid? deviceId,
        double? fuelLevelPercent, double? fuelLevelLiters, double? odometerKm,
        bool? ignition, bool? engineOn, double? latitude, double? longitude, DateTime at)
    {
        if (!vehicleId.HasValue) return; // unassigned device — no vehicle context
        if (!fuelLevelLiters.HasValue && !fuelLevelPercent.HasValue) return; // no fuel data

        var vehicle = await _db.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId.Value && !v.IsDeleted);
        if (vehicle == null) return;

        var previous = await _db.FuelConsumptionSnapshots.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId.Value && s.EventTimeUtc < at)
            .OrderByDescending(s => s.EventTimeUtc)
            .FirstOrDefaultAsync();

        var currentLiters = fuelLevelLiters ?? (fuelLevelPercent.HasValue && vehicle.FuelTankCapacity is > 0
            ? fuelLevelPercent.Value / 100 * (double)vehicle.FuelTankCapacity.Value
            : (double?)null);
        var previousLiters = previous != null
            ? previous.FuelLevelLiters ?? (previous.FuelLevelPercent.HasValue && vehicle.FuelTankCapacity is > 0
                ? previous.FuelLevelPercent.Value / 100 * (double)vehicle.FuelTankCapacity.Value
                : (double?)null)
            : null;

        // ── Derived pair vs the previous reading ────────────────────────────
        double? litersConsumed = null, distanceKm = null, consumption = null;
        if (currentLiters.HasValue && previousLiters.HasValue)
        {
            var drop = previousLiters.Value - currentLiters.Value;
            if (drop > 0) litersConsumed = Math.Round(drop, 3);

            if (odometerKm.HasValue && previous.OdometerKm.HasValue && odometerKm.Value >= previous.OdometerKm.Value)
            {
                distanceKm = Math.Round(odometerKm.Value - previous.OdometerKm.Value, 3);
            }
            if (litersConsumed is > 0 && distanceKm is > 0)
                consumption = Math.Round(litersConsumed.Value / distanceKm.Value * 100, 2); // L/100km
        }

        var snapshot = new FuelConsumptionSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = companyId,
            VehicleId = vehicleId.Value,
            DeviceId = deviceId,
            EventTimeUtc = at,
            FuelLevelPercent = fuelLevelPercent,
            FuelLevelLiters = fuelLevelLiters,
            OdometerKm = odometerKm,
            Ignition = ignition,
            EngineOn = engineOn,
            LitersConsumed = litersConsumed,
            DistanceKm = distanceKm,
            ConsumptionLitersPer100km = consumption
        };

        // ── Refuel recognition (level rose while parked) → sensor transaction ─
        if (currentLiters.HasValue && previousLiters.HasValue
            && currentLiters.Value - previousLiters.Value >= RefuelThresholdLiters(vehicle)
            && (ignition == false || engineOn == false))
        {
            var refuel = new FuelRecord
            {
                Id = Guid.NewGuid(),
                VehicleId = vehicleId.Value,
                CompanyId = companyId,
                TenantId = companyId,
                FuelType = vehicle.FuelType,
                Quantity = Math.Round((decimal)(currentLiters.Value - previousLiters.Value), 3),
                Unit = "liters",
                OdometerReading = odometerKm.HasValue ? (decimal)odometerKm.Value : null,
                Source = FuelRecord.SourceSensor,
                RecordDate = at,
                CreatedAt = _clock()
            };
            // Distance since the previous transaction (any source) for consistency
            // with the manual path's efficiency computation.
            var lastTransaction = await _db.FuelRecords.AsNoTracking()
                .Where(f => !f.IsDeleted && f.VehicleId == vehicleId.Value && f.RecordDate <= at)
                .OrderByDescending(f => f.RecordDate)
                .Select(f => (decimal?)f.OdometerReading)
                .FirstOrDefaultAsync();
            if (refuel.OdometerReading.HasValue && lastTransaction.HasValue
                && refuel.OdometerReading.Value >= lastTransaction.Value)
            {
                refuel.DistanceTraveledKm = refuel.OdometerReading.Value - lastTransaction.Value;
            }
            _db.FuelRecords.Add(refuel);
        }

        // ── Anomaly detection: sudden drop, no distance, engine never off ───
        var anomaly = await EvaluateTheftAsync(companyId, vehicle, previous, currentLiters, previousLiters,
            litersConsumed, distanceKm, ignition, latitude, longitude, at);
        if (anomaly)
        {
            snapshot.IsAnomaly = true;
            snapshot.AnomalyReason = "Suspected fuel theft — sudden level drop with no matching distance and no engine-off refueling event";
        }

        _db.FuelConsumptionSnapshots.Add(snapshot);

        // ── Low level (below company threshold) ─────────────────────────────
        var levelPercent = fuelLevelPercent
            ?? (currentLiters.HasValue && vehicle.FuelTankCapacity is > 0
                ? currentLiters.Value / (double)vehicle.FuelTankCapacity.Value * 100
                : (double?)null);
        if (levelPercent.HasValue)
        {
            var threshold = await LowLevelThresholdAsync(companyId);
            if (levelPercent.Value <= threshold)
            {
                var title = $"Low fuel level — vehicle {vehicle.RegistrationNumber}";
                var message = $"Vehicle {vehicle.RegistrationNumber} is at {levelPercent.Value:0}% fuel{FormatPosition(latitude, longitude)}.";
                await StageAlertAsync(companyId, vehicle.Id, LowLevelAlertCode, title, message,
                    AlertSeverity.Low, latitude, longitude, at, DailyThrottle);
            }
        }

        // ── Efficiency degradation vs the vehicle's own baseline ────────────
        if (consumption is > 0 && distanceKm is >= MinEfficiencyDistanceKm)
        {
            var baseline = await EfficiencyBaselineAsync(companyId, vehicle.Id, at);
            if (baseline is > 0 && consumption.Value > baseline.Value * EfficiencyDegradationFactor)
            {
                var title = $"Fuel efficiency degraded — vehicle {vehicle.RegistrationNumber}";
                var message = $"Vehicle {vehicle.RegistrationNumber} is consuming {consumption.Value:0.0} L/100km — {((consumption.Value / baseline.Value - 1) * 100):0}% worse than its {baseline.Value:0.0} L/100km baseline{FormatPosition(latitude, longitude)}.";
                await StageAlertAsync(companyId, vehicle.Id, EfficiencyAlertCode, title, message,
                    AlertSeverity.Medium, latitude, longitude, at, DailyThrottle);
            }
        }
    }

    private double RefuelThresholdLiters(Vehicle vehicle)
        => vehicle.FuelTankCapacity is > 0
            ? Math.Max(MinRefuelLiters, (double)vehicle.FuelTankCapacity.Value * RefuelFractionOfTank)
            : MinRefuelLiters;

    private async Task<bool> EvaluateTheftAsync(Guid companyId, Vehicle vehicle,
        FuelConsumptionSnapshot? previous, double? currentLiters, double? previousLiters,
        double? litersConsumed, double? distanceKm, bool? ignition,
        double? latitude, double? longitude, DateTime at)
    {
        if (!currentLiters.HasValue || !previousLiters.HasValue || litersConsumed is not > 0) return false;

        var threshold = Math.Max(MinTheftDropLiters, (double)vehicle.FuelTankCapacity.GetValueOrDefault(0) * TheftDropFractionOfTank);
        if (litersConsumed.Value < threshold) return false;
        if (distanceKm.HasValue && distanceKm.Value >= TheftMaxDistanceKm) return false;

        // An engine-off change is a legitimate refuel event — never theft.
        // (Ignition unknown on either side → still suspicious: a drop this big
        // with no distance deserves a look regardless of the ignition flag.)
        if (previous?.Ignition == false || ignition == false) return false;

        if (!await _alertEnforcement.IsEntitledAsync(companyId, TheftAlertCode)) return false;
        if (await RecentAlertExistsAsync(companyId, vehicle.Id, TheftAlertCode, at, TheftThrottle)) return false;

        var title = $"Fuel theft suspected — vehicle {vehicle.RegistrationNumber}";
        var message = $"Vehicle {vehicle.RegistrationNumber} lost {litersConsumed.Value:0} L of fuel with {(distanceKm.HasValue ? distanceKm.Value: 0):0.0} km traveled and no engine-off refueling event{FormatPosition(latitude, longitude)}.";
        await StageAlertAsync(companyId, vehicle.Id, TheftAlertCode, title, message,
            AlertSeverity.High, latitude, longitude, at, TimeSpan.Zero); // throttle already checked above
        return true;
    }

    /// <summary>
    /// Vehicle's own recent average consumption (L/100km) across its last
    /// non-anomaly snapshot pairs — the baseline "normal" this vehicle exhibits.
    /// </summary>
    private async Task<double?> EfficiencyBaselineAsync(Guid companyId, Guid vehicleId, DateTime at)
    {
        var recent = await _db.FuelConsumptionSnapshots.AsNoTracking()
            .Where(s => s.VehicleId == vehicleId && s.EventTimeUtc < at && !s.IsAnomaly
                && s.ConsumptionLitersPer100km.HasValue && s.ConsumptionLitersPer100km > 0)
            .OrderByDescending(s => s.EventTimeUtc)
            .Take(BaselineWindowCount)
            .Select(s => s.ConsumptionLitersPer100km!.Value)
            .ToListAsync();
        return recent.Count == 0 ? null : recent.Average();
    }

    private async Task<double> LowLevelThresholdAsync(Guid companyId)
    {
        var raw = await _db.Configurations.AsNoTracking()
            .Where(c => c.Key == LowLevelConfigKey && c.Scope == ConfigurationScope.Company && !c.IsDeleted
                && (c.ScopeEntityId == companyId || c.CompanyId == companyId))
            .Select(c => c.Value)
            .FirstOrDefaultAsync();
        return double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : DefaultLowLevelThresholdPercent;
    }

    private async Task StageAlertAsync(Guid companyId, Guid vehicleId, string code, string title, string message,
        AlertSeverity severity, double? latitude, double? longitude, DateTime at, TimeSpan throttleWindow)
    {
        if (throttleWindow > TimeSpan.Zero && await RecentAlertExistsAsync(companyId, vehicleId, code, at, throttleWindow))
            return;

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
            Longitude = longitude,
            CreatedAt = _clock()
        });
        if (_notificationService != null)
        {
            await _notificationService.NotifyCompanyAdminsAsync(companyId, "alert.fired", title, message,
                (int)severity, "Vehicle", vehicleId, $"/vehicles/{vehicleId}");
        }
    }

    private async Task<bool> RecentAlertExistsAsync(Guid companyId, Guid vehicleId, string code, DateTime at, TimeSpan window)
    {
        var since = _clock().Add(-window);
        return await _db.Alerts.AsNoTracking().AnyAsync(a => !a.IsDeleted
            && a.CompanyId == companyId && a.VehicleId == vehicleId
            && a.AlertType == code && a.CreatedAt >= since);
    }

    private static string FormatPosition(double? latitude, double? longitude)
        => latitude.HasValue && longitude.HasValue ? $" at {latitude:0.00000},{longitude:0.00000}" : string.Empty;
}