using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Ingestion.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.DriverSafety;

/// <summary>
/// Driver-behavior alert pipeline: DMS events normalized by an adapter are
/// persisted driver-resolved (scorecard groundwork) and alerted through the
/// registry — entitlement-gated for regular behavior alerts, with panic-button
/// events bypassing entitlement, role visibility and per-user preferences.
/// </summary>
public class DriverBehaviorAlertProducerTests
{
    private static ApplicationDbContext NewDb(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static NormalizedBehaviorEvent Event(string code, double confidence = 1.0, string? mediaUrl = null)
        => new() { EventType = code, Confidence = confidence, MediaUrl = mediaUrl };

    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Vehicle = Guid.NewGuid();
    private static readonly Guid Driver = Guid.NewGuid();
    private static readonly Guid Device = Guid.NewGuid();
    private static readonly DateTime At = new(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);

    // ── Panic button: emergency signal ─────────────────────────────────────

    [Fact]
    public async Task PanicButton_FiresCriticalAlert_EvenWhenCompanyNotEntitled()
    {
        using var db = NewDb("panic_entitlement_" + Guid.NewGuid());
        var notifications = new CapturingNotificationService();
        var producer = new DriverBehaviorAlertProducer(db, new DenyAllAlertEnforcement(), notifications);

        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("sos_triggered", 0.99) }, 23.15, 72.65, 12.5, Guid.NewGuid(), At);
        await db.SaveChangesAsync();

        // Entitlement denied everywhere, yet the panic alert still fires — the
        // NonMutablePriority registry contract overrides the company gate.
        var alert = Assert.Single(db.Alerts);
        Assert.Equal("DriverPanicButton", alert.AlertType);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(Vehicle, alert.VehicleId);
        Assert.Equal(Driver, alert.DriverId);
        // Emergency path used (NotNotifyCompanyAdminsAsync).
        var push = Assert.Single(notifications.Dispatched);
        Assert.Equal("alert.fired", push.EventType);
        Assert.Equal((int)AlertSeverity.Critical, push.Severity);
    }

    [Fact]
    public async Task PanicButton_Notification_IgnoresPerUserPreferenceMute()
    {
        using var db = NewDb("panic_pref_" + Guid.NewGuid());
        var company = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Companies.Add(new Company { Id = company, Name = "C", Slug = "c", Status = EntityStatus.Active });
        db.AlertTypes.Add(new AlertType
        {
            Id = Guid.NewGuid(), Code = "driver.panic_button", Name = "Panic Button (SOS)",
            Category = "Driver", DefaultSeverity = 4, NonMutablePriority = true,
            Status = EntityStatus.Active
        });
        var adminRole = new Role
        {
            Id = Guid.NewGuid(), Name = "Company Admin", CompanyId = company,
            IsSystemRole = true, Status = EntityStatus.Active
        };
        db.Roles.Add(adminRole);
        db.Users.Add(new User
        {
            Id = userId, CompanyId = company, Email = "admin@c.test",
            FirstName = "A", LastName = "B", Status = EntityStatus.Active
        });
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), RoleId = adminRole.Id, UserId = userId });
        // The user MUTED alert.fired in their personal preferences.
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(), UserId = userId, CompanyId = company,
            EventType = "alert.fired", Enabled = false
        });
        await db.SaveChangesAsync();

        var service = new NotificationService(db, new StubPermissionService());
        var vehicleId = Guid.NewGuid();
        var delivered = await service.NotifyPanicAsync(company, vehicleId, "driver.panic_button",
            "alert.fired", "Panic button pressed", "SOS!", (int)AlertSeverity.Critical,
            "Vehicle", vehicleId, $"/vehicles/{vehicleId}");

        Assert.Equal(1, delivered);
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal((int)AlertSeverity.Critical, notification.Severity);
        Assert.Equal("alert.fired", notification.EventType);
    }

    // ── Regular behavior alerts: entitlement gate ──────────────────────────

    [Fact]
    public async Task Drowsiness_CompanyNotEntitled_NeverGeneratesAlert()
    {
        using var db = NewDb("drowsy_denied_" + Guid.NewGuid());
        var notifications = new CapturingNotificationService();
        var producer = new DriverBehaviorAlertProducer(db, new DenyAllAlertEnforcement(), notifications);

        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("drowsiness", 0.9) }, 23.1, 72.6, 40, Guid.NewGuid(), At);
        await db.SaveChangesAsync();

        // A company not subscribed to driver.drowsiness gets NO alert and NO
        // notification even though the device reported it — test-locked per spec.
        Assert.Empty(db.Alerts);
        Assert.Empty(notifications.Dispatched);
        // …but the raw event is still persisted (scorecard groundwork — raw
        // data collection must never depend on alert entitlement).
        var row = Assert.Single(db.DriverBehaviorEvents);
        Assert.Equal(DriverBehaviorEventType.Drowsiness, row.EventType);
        Assert.Equal(Driver, row.DriverId);
    }

    [Fact]
    public async Task Drowsiness_Entitled_RaisesHighAlert_AndNotification()
    {
        using var db = NewDb("drowsy_entitled_" + Guid.NewGuid());
        var notifications = new CapturingNotificationService();
        var producer = new DriverBehaviorAlertProducer(db, new AlwaysEntitledAlertEnforcement(), notifications);
        var telemetryEventId = Guid.NewGuid();

        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("drowsiness", 0.9, "https://cdn.example.com/snap/1.jpg") }, 23.1, 72.6, 40, telemetryEventId, At);
        await db.SaveChangesAsync();

        var alert = Assert.Single(db.Alerts);
        Assert.Equal("DriverDrowsiness", alert.AlertType);
        Assert.Equal(AlertSeverity.High, alert.Severity);
        Assert.Equal(23.1, alert.Latitude!.Value, 5);
        Assert.Equal(72.6, alert.Longitude!.Value, 5);
        var push = Assert.Single(notifications.Dispatched);
        Assert.Equal("alert.fired", push.EventType);
        Assert.Equal((int)AlertSeverity.High, push.Severity);

        var row = Assert.Single(db.DriverBehaviorEvents);
        Assert.Equal(DriverBehaviorEventType.Drowsiness, row.EventType);
        Assert.Equal(0.9, row.Confidence);
        Assert.Equal("https://cdn.example.com/snap/1.jpg", row.MediaUrl);
        Assert.Equal(telemetryEventId, row.TelemetryEventId);
        Assert.Equal(Driver, row.DriverId);
        Assert.Equal(Vehicle, row.VehicleId);
    }

    // ── Throttle: distinct alert per vehicle+type per window ───────────────

    [Fact]
    public async Task SameEventWithinThrottleWindow_NoDuplicateAlert()
    {
        using var db = NewDb("throttle_" + Guid.NewGuid());
        var notifications = new CapturingNotificationService();
        var clock = DateTime.UtcNow;
        var producer = new DriverBehaviorAlertProducer(db, new AlwaysEntitledAlertEnforcement(), notifications, () => clock);

        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("harsh_braking", 0.9) }, 23.1, 72.6, 50, Guid.NewGuid(), At);
        await db.SaveChangesAsync();
        // 30 s later (wall clock), same vehicle + same event type — inside the
        // 2-min window → the distinct alert is throttled.
        clock = clock.AddSeconds(30);
        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("harsh_braking", 0.95) }, 23.1, 72.6, 55, Guid.NewGuid(), At.AddSeconds(30));
        await db.SaveChangesAsync();

        Assert.Single(db.Alerts); // throttled — one distinct alert per episode
        Assert.Equal(2, db.DriverBehaviorEvents.Count()); // raw events are NOT throttled
    }

    [Fact]
    public async Task SameEventAfterThrottleWindow_SecondAlertFires()
    {
        using var db = NewDb("throttle_2_" + Guid.NewGuid());
        var notifications = new CapturingNotificationService();
        var clock = DateTime.UtcNow;
        var producer = new DriverBehaviorAlertProducer(db, new AlwaysEntitledAlertEnforcement(), notifications, () => clock);

        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("harsh_braking") }, 23.1, 72.6, 50, Guid.NewGuid(), At);
        await db.SaveChangesAsync();
        // 3 min later (wall clock) — past the 2-min window → a fresh alert fires.
        clock = clock.AddMinutes(3);
        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("harsh_braking") }, 23.1, 72.6, 50, Guid.NewGuid(), At.AddMinutes(3));
        await db.SaveChangesAsync();

        Assert.Equal(2, db.Alerts.Count());
        Assert.Equal(2, notifications.Dispatched.Count);
    }

    // ── Unknown canonical codes are dropped, never crash ───────────────────

    [Fact]
    public async Task UnknownCanonicalCode_IsDropped_Silently()
    {
        using var db = NewDb("unknown_" + Guid.NewGuid());
        var producer = new DriverBehaviorAlertProducer(db, new AlwaysEntitledAlertEnforcement(), null);

        await producer.ProcessBehaviorEventsAsync(Company, Device, Vehicle, Driver,
            new[] { Event("vendor_voodoo", 0.5) }, 23.1, 72.6, 50, Guid.NewGuid(), At);
        await db.SaveChangesAsync();

        Assert.Empty(db.Alerts);
        Assert.Empty(db.DriverBehaviorEvents);
    }
}