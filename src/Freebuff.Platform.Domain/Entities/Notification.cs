using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// In-app notification addressed to a single user. EventType is the machine
/// code of a NotificationEventType registry row (same pattern as AlertType),
/// so new notification-worthy events are a registry entry, not bell-icon code.
///
/// DeliveryChannel is reserved for the future email/SMS/push phase — this pass
/// writes "in_app" only, but the column exists so multi-channel delivery later
/// is a data change, not a schema rewrite.
/// </summary>
public class Notification : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Legacy free-text type kept for back-compat; prefer <see cref="EventType"/>.</summary>
    public string? Type { get; set; }

    /// <summary>Registry event code, e.g. "company.package_changed" / "permission.role_updated".</summary>
    public string? EventType { get; set; }

    public string? ActionUrl { get; set; }

    /// <summary>AlertSeverity value (0=Info .. 4=Critical).</summary>
    public int Severity { get; set; } = (int)AlertSeverity.Medium;

    /// <summary>Entity the notification links back to (e.g. "Role", "Trip", "Company").</summary>
    public string? RelatedEntityType { get; set; }

    /// <summary>Primary key of the related entity, when applicable.</summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary>Delivery channel — "in_app" now; email/sms/push reserved for the follow-up phase.</summary>
    public string? DeliveryChannel { get; set; } = "in_app";

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

/// <summary>
/// Master registry of notification-worthy events (alert.fired, permission.role_updated,
/// company.package_changed, user.created, …). Managed by Super Admin — adding a new
/// notification source is a row here, not new code in the bell component.
/// </summary>
public class NotificationEventType : BaseEntity
{
    /// <summary>Unique machine-readable code, e.g. "company.package_changed".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Category grouping: Fleet, Permission, Company, User, Device, System.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Default severity when this event fires.</summary>
    public int DefaultSeverity { get; set; } = (int)AlertSeverity.Medium;

    public int DisplayOrder { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

/// <summary>
/// Per-user mute/unmute layer on top of what the user is entitled to see. This is
/// personal — unlike RoleAlertVisibility (admin-controlled entitlement), this only
/// narrows what a specific user wants surfaced. Absence of a row = enabled.
/// </summary>
public class NotificationPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid CompanyId { get; set; }

    /// <summary>Registry event code this preference applies to.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>false = muted (user does not want this event surfaced).</summary>
    public bool Enabled { get; set; } = true;
}