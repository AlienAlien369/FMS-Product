namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Periodic sensor-derived fuel level reading for a vehicle with a fuel sensor
/// attached via the Device Abstraction Layer (VehicleDevice role FuelSensor).
/// One row per accepted telemetry snapshot that carries fuel data — the raw
/// level stream from which consumption is derived.
///
/// DELIBERATELY lean (like TelemetryEvent): append-only high-volume stream, no
/// soft-delete/audit baggage. Tenant scoping is the explicit TenantId column.
///
/// The derived pair (LitersConsumed, DistanceKm) is computed at write time
/// between this reading and the vehicle's previous snapshot so consumption
/// queries (trends, efficiency, anomaly detection) never re-derive from raw
/// levels — and so the fuel.theft_suspected / fuel.efficiency_degraded alert
/// logic and the query surface share one source of truth.
/// </summary>
public class FuelConsumptionSnapshot
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid? DeviceId { get; set; }

    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;

    public double? FuelLevelPercent { get; set; }
    public double? FuelLevelLiters { get; set; }
    public double? OdometerKm { get; set; }
    public bool? Ignition { get; set; }
    public bool? EngineOn { get; set; }

    /// <summary>Liters consumed between this reading and the previous one (positive drop; 0 when the level rose/refueled).</summary>
    public double? LitersConsumed { get; set; }

    /// <summary>Distance traveled between this reading and the previous one.</summary>
    public double? DistanceKm { get; set; }

    /// <summary>Consumption = LitersConsumed / DistanceKm * 100 (L/100km). Null until both are positive.</summary>
    public double? ConsumptionLitersPer100km { get; set; }

    /// <summary>True when this drop was flagged as a suspected theft (anomaly detection).</summary>
    public bool IsAnomaly { get; set; }

    public string? AnomalyReason { get; set; }
}