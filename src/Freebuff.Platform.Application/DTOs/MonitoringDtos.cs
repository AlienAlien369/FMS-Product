using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Application.DTOs;

public class CreateAlertDto
{
    public string AlertType { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Medium;
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? AlertConfigurationId { get; set; }
}

public class CreateFuelRecordDto
{
    public Guid VehicleId { get; set; }
    public FuelType FuelType { get; set; } = FuelType.Diesel;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; } = "liters";
    public decimal? PricePerUnit { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal? OdometerReading { get; set; }
    public decimal? FuelLevel { get; set; }
    public bool IsRefueling { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime RecordDate { get; set; } = DateTime.UtcNow;
}
