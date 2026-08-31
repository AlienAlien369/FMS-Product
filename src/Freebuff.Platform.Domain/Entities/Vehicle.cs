using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Vehicle entity with full fleet management support.
/// </summary>
public class Vehicle : BaseEntity
{
    public string RegistrationNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? VehicleType { get; set; } // Configurable vehicle types
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public FuelType FuelType { get; set; } = FuelType.Diesel;
    public decimal? FuelTankCapacity { get; set; }
    public string? FuelCapacityUnit { get; set; } = "liters";
    public string? EngineNumber { get; set; }
    public string? ChassisNumber { get; set; }
    public string? VinNumber { get; set; }

    // Company
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // Assigned client
    public Guid? ClientId { get; set; }
    public Client? Client { get; set; }

    // Assigned driver
    public Guid? DriverId { get; set; }
    public Driver? Driver { get; set; }

    // Status
    public VehicleStatus Status { get; set; } = VehicleStatus.Active;

    // Device
    public string? DeviceImei { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceSerialNumber { get; set; }

    // Tracking
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public double? LastSpeed { get; set; }
    public double? LastHeading { get; set; }
    public DateTime? LastLocationUpdate { get; set; }
    public bool? IgnitionStatus { get; set; }

    // Metadata
    public long? OdometerReading { get; set; }
    public long? EngineHours { get; set; }
    public string? CustomAttributes { get; set; } // JSON

    // Navigation
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
    public ICollection<VehicleGeofence> VehicleGeofences { get; set; } = new List<VehicleGeofence>();
    public ICollection<FuelRecord> FuelRecords { get; set; } = new List<FuelRecord>();
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; } = new List<MaintenanceRecord>();
}
