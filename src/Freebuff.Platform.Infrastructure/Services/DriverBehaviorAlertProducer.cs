using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Ingestion.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Freebuff.Platform.Infrastructure.Services;

/// <summary>
/// The driver-behavior alert producer: after an accepted telemetry payload that
/// carries normalized behavior events (from a DMS/dashcam adapter), it
///  1. persists each event as a DriverBehaviorEvent row (scorecard groundwork —
///     driver-resolved + indexed by driver/time at write time), and
///  2. raises a registry alert per event, gated by the company's
///     CompanyAlertSubscription entitlement — EXCEPT panic-button events, which
///     bypass entitlement, role visibility and per-user notification
///     preferences entirely (NonMutablePriority in the registry), always firing
///     at Critical severity through the emergency notification path.
/// Vendor knowledge never reaches this class: the adapter has already
/// normalized event codes, and the DriverBehaviorCatalog is the only place
/// canonical codes meet alert specs. All mutation is staged in the caller's
/// context — the caller owns SaveChanges so the telemetry row and its alert
/// effects commit atomically.
/// </summary>
public class DriverBehaviorAlertProducer
{
    private readonly ApplicationDbContext _db;
    private readonly IAlertTypeEnforcement _alertEnforcement;
    private readonly INotificationService? _notificationService;
    private readonly Func<DateTime> _clock;

    public DriverBehaviorAlertProducer(ApplicationDbContext db, IAlertTypeEnforcement alertEnforcement,
        INotificationService? notificationService = null, Func<DateTime>? clock = null)
    {
        _db = db;
        _alertEnforcement = alertEnforcement;
        _notificationService = notificationService;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Persist normalized behavior events + raise their alerts. Unknown canonical
    /// codes are dropped (adapter misconfiguration must not fail the payload).
    /// </summary>
    public async Task ProcessBehaviorEventsAsync(Guid companyId, Guid deviceId, Guid? vehicleId, Guid? driverId,
        IReadOnlyList<NormalizedBehaviorEvent> events, double? latitude, double? longitude, double? speedKmh,
        Guid? telemetryEventId, DateTime at)
    {
        foreach (var ev in events)
        {
            if (!DriverBehaviorCatalog.TryGetEventType(ev.EventType, out var eventType)) continue;

            _db.DriverBehaviorEvents.Add(new DriverBehaviorEvent
            {
                Id = Guid.NewGuid(),
                TenantId = companyId,
                DeviceId = deviceId,
                VehicleId = vehicleId,
                DriverId = driverId,
                EventType = eventType,
                Confidence = Math.Clamp(ev.Confidence, 0, 1),
                EventTimeUtc = at,
                Latitude = latitude,
                Longitude = longitude,
                SpeedKmh = speedKmh,
                MediaUrl = ev.MediaUrl,
                TelemetryEventId = telemetryEventId
            });

            await RaiseAlertAsync(companyId, vehicleId, driverId, eventType, ev, at, latitude, longitude);
        }
    }

    private async Task RaiseAlertAsync(Guid companyId, Guid? vehicleId, Guid? driverId,
        DriverBehaviorEventType eventType, NormalizedBehaviorEvent ev, DateTime at,
        double? latitude, double? longitude)
    {
        var spec = DriverBehaviorCatalog.Spec(eventType);

        // ── Panic button: emergency signal, bypasses the narrowing entirely. ──
        // No entitlement gate, no role-visibility narrowing, no per-user
        // preference mute — the registry marks it NonMutablePriority. The only
        // guard is a short throttle window so a held/stuck SOS button cannot
        // spam the company.
        if (spec.NonMutablePriority)
        {
            if (vehicleId.HasValue && await RecentAlertExistsAsync(companyId, vehicleId.Value, spec, at)) return;

            var title = $"{spec.AlertTitle} — vehicle {vehicleId}";
            var message = BuildMessage(spec, vehicleId, driverId, ev, at, latitude, longitude);
            _db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                AlertType = spec.AlertRecordType,
                Severity = AlertSeverity.Critical,
                Title = title,
                Message = message,
                CompanyId = companyId,
                TenantId = companyId,
                VehicleId = vehicleId,
                DriverId = driverId,
                Latitude = latitude,
                Longitude = longitude
            });
            if (_notificationService != null)
            {
                var (entityType, entityId, url) = ResolveLink(driverId, vehicleId);
                await _notificationService.NotifyPanicAsync(companyId, vehicleId ?? Guid.Empty,
                    spec.AlertTypeCode, "alert.fired", title, message, (int)AlertSeverity.Critical,
                    entityType, entityId, url);
            }
            return;
        }

        // ── Regular behavior alerts: company entitlement gates generation. ──
        // A company not subscribed to e.g. driver.drowsiness never gets that
        // alert even when the device reports it (test-locked).
        if (!await _alertEnforcement.IsEntitledAsync(companyId, spec.AlertTypeCode)) return;
        if (vehicleId.HasValue && await RecentAlertExistsAsync(companyId, vehicleId.Value, spec, at)) return;

        var regTitle = $"{spec.AlertTitle} — vehicle {vehicleId}";
        var regMessage = BuildMessage(spec, vehicleId, driverId, ev, at, latitude, longitude);
        _db.Alerts.Add(new Alert
        {
            Id = Guid.NewGuid(),
            AlertType = spec.AlertRecordType,
            Severity = spec.Severity,
            Title = regTitle,
            Message = regMessage,
            CompanyId = companyId,
            TenantId = companyId,
            VehicleId = vehicleId,
            DriverId = driverId,
            Latitude = latitude,
            Longitude = longitude
        });
        if (_notificationService != null)
        {
            var (entityType, entityId, url) = ResolveLink(driverId, vehicleId);
            await _notificationService.NotifyCompanyAdminsAsync(companyId, "alert.fired",
                regTitle, regMessage, (int)spec.Severity, entityType, entityId, url);
        }
    }

    private static string BuildMessage(DriverBehaviorSpec spec, Guid? vehicleId, Guid? driverId,
        NormalizedBehaviorEvent ev, DateTime at, double? latitude, double? longitude)
    {
        var parts = new List<string>
        {
            $"Event at {at:u}",
            $"confidence {ev.Confidence:0.00}"
        };
        if (latitude.HasValue && longitude.HasValue) parts.Add($"position {latitude:0.00000},{longitude:0.00000}");
        if (ev.MediaUrl != null) parts.Add($"media {ev.MediaUrl}");
        return $"{spec.AlertName} detected ({string.Join(" · ", parts)}).";
    }

    /// <summary>Notifications deep-link to the driver page when known, else the vehicle page.</summary>
    private static (string? EntityType, Guid? EntityId, string? Url) ResolveLink(Guid? driverId, Guid? vehicleId)
    {
        if (driverId.HasValue) return ("Driver", driverId, $"/drivers/{driverId}");
        if (vehicleId.HasValue) return ("Vehicle", vehicleId, $"/vehicles/{vehicleId}");
        return (null, null, null);
    }

    /// <summary>
    /// Throttle: a distinct alert for the same vehicle+type fires at most once
    /// per spec window. Wall-clock based (CreatedAt is the audit insert time) —
    /// semantically "don't spam now", regardless of how stale a device's
    /// reported event time is.
    /// </summary>
    private async Task<bool> RecentAlertExistsAsync(Guid companyId, Guid vehicleId, DriverBehaviorSpec spec, DateTime at)
    {
        var since = _clock().AddMinutes(-spec.ThrottleMinutes);
        return await _db.Alerts.AsNoTracking().AnyAsync(a => !a.IsDeleted
            && a.CompanyId == companyId && a.VehicleId == vehicleId
            && a.AlertType == spec.AlertRecordType
            && a.CreatedAt >= since);
    }
}