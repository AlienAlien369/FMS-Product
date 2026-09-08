using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Application.DTOs;

public class FuelRecordDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public Guid CompanyId { get; set; }
    public FuelType FuelType { get; set; }
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? PricePerUnit { get; set; }
    public decimal? TotalCost { get; set; }
    public decimal? OdometerReading { get; set; }
    public decimal? FuelLevel { get; set; }
    public string? Source { get; set; }
    public string? Station { get; set; }
    public decimal? DistanceTraveledKm { get; set; }
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
    public string? Notes { get; set; }
    public DateTime RecordDate { get; set; }

    /// <summary>km per liter for this fill-up (null until the next transaction provides distance).</summary>
    public decimal? EfficiencyKmPerLiter { get; set; }

    /// <summary>Liters per 100 km for this fill-up.</summary>
    public decimal? ConsumptionLitersPer100km { get; set; }
}

public class FuelFleetStatsDto
{
    public int TransactionCount { get; set; }
    public decimal TotalLiters { get; set; }
    public decimal TotalSpend { get; set; }
    public decimal? AvgPricePerLiter { get; set; }
    /// <summary>Fleet-wide efficiency: total distance / total liters across complete pairs.</summary>
    public decimal? AvgEfficiencyKmPerLiter { get; set; }
    public decimal? CostPerKm { get; set; }
    public int AnomalyCount { get; set; }
    public int SensorCoveredVehicles { get; set; }
    public int ManualVehicles { get; set; }
}

public class FuelTrendPointDto
{
    public string Day { get; set; } = string.Empty;
    public decimal Liters { get; set; }
    public decimal Spend { get; set; }
    public decimal? EfficiencyKmPerLiter { get; set; }
}

public class FuelVehicleMetricsDto
{
    public Guid VehicleId { get; set; }
    public string? VehicleRegistration { get; set; }
    public int TransactionCount { get; set; }
    public decimal TotalLiters { get; set; }
    public decimal TotalSpend { get; set; }
    public decimal? AvgEfficiencyKmPerLiter { get; set; }
    public decimal? CostPerKm { get; set; }
    public List<FuelRecordDto> Transactions { get; set; } = new();
    /// <summary>Sensor-derived consumption trend (per-day aggregate), when a fuel sensor is attached.</summary>
    public List<FuelTrendPointDto> ConsumptionTrend { get; set; } = new();
    public List<FuelConsumptionSnapshotDto> Snapshots { get; set; } = new();
    public bool HasFuelSensor { get; set; }
}

public class FuelConsumptionSnapshotDto
{
    public Guid Id { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public double? FuelLevelPercent { get; set; }
    public double? FuelLevelLiters { get; set; }
    public double? OdometerKm { get; set; }
    public double? LitersConsumed { get; set; }
    public double? DistanceKm { get; set; }
    public double? ConsumptionLitersPer100km { get; set; }
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
}