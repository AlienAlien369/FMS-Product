using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Alert configuration entity. Defines alert rules with configurable triggers.
/// </summary>
public class AlertConfiguration : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AlertType { get; set; } = string.Empty; // Configurable alert types
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
    public bool IsActive { get; set; } = true;

    // Trigger
    public string TriggerCondition { get; set; } = string.Empty; // JSON condition
    public decimal? Threshold { get; set; }
    public int? DurationSeconds { get; set; }

    // Notification
    public string? NotificationChannels { get; set; } // JSON array of channels
    public string? Recipients { get; set; } // JSON - user/role/email targets
    public int? CooldownMinutes { get; set; }
    public int? EscalationMinutes { get; set; }

    // Scope
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}
