using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Alert entity. Created when an alert condition is triggered.
/// </summary>
public class Alert : BaseEntity
{
    public string AlertType { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    // Location
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }

    // Associations
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public Guid? DriverId { get; set; }
    public Driver? Driver { get; set; }
    public Guid? AlertConfigurationId { get; set; }
    public AlertConfiguration? AlertConfiguration { get; set; }

    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? Resolution { get; set; }
}
