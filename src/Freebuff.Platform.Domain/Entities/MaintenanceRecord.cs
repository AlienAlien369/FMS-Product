using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Maintenance record entity.
/// </summary>
public class MaintenanceRecord : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public Guid CompanyId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string MaintenanceType { get; set; } = "preventive"; // preventive, corrective
    public string? Workshop { get; set; }
    public decimal? Cost { get; set; }
    public string? Currency { get; set; } = "USD";
    public decimal? OdometerAtService { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsCompleted { get; set; }
    public string? PartsReplaced { get; set; } // JSON
    public string? Notes { get; set; }
}
