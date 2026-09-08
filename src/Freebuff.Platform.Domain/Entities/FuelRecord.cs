using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Fuel record — one fill-up transaction for the Fuel Management module.
///
/// Two ingestion paths land here:
///   - manual_entry:   fleet staff log a fill-up (odometer + liters + cost).
///     Efficiency for that fill-up is computed against the vehicle's previous
///     transaction at write time (DistanceTraveledKm / Quantity).
///   - sensor_derived: a fuel-sensor telemetry snapshot recognized a refuel
///     (level rose). Consumption itself lives on FuelConsumptionSnapshot rows;
///     the refuel is materialized here as the transaction the reporting surface
///     consumes, so both paths share one transaction history.
///
/// Source is null for legacy rows (pre-module) — treat as manual.
/// </summary>
public class FuelRecord : BaseEntity
{
    public const string SourceManual = "manual_entry";
    public const string SourceSensor = "sensor_derived";

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

    /// <summary>manual_entry | sensor_derived (null = legacy manual row).</summary>
    public string? Source { get; set; }

    /// <summary>Fuel station / location captured at fill-up, when known.</summary>
    public string? Station { get; set; }

    /// <summary>Distance since the vehicle's previous transaction — the efficiency denominator (km).</summary>
    public decimal? DistanceTraveledKm { get; set; }

    public bool IsRefueling { get; set; } = true;
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
    public string? Notes { get; set; }
    public DateTime RecordDate { get; set; } = DateTime.UtcNow;
}
