using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Fuel record entity for fuel monitoring.
/// </summary>
public class FuelRecord : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public Guid CompanyId { get; set; }

    public FuelType FuelType { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; } = "liters";
    public decimal? PricePerUnit { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal? OdometerReading { get; set; }
    public decimal? FuelLevel { get; set; } // Percentage
    public bool IsRefueling { get; set; } = true;
    public bool IsAnomaly { get; set; }
    public string? Notes { get; set; }
    public DateTime RecordDate { get; set; } = DateTime.UtcNow;
}
