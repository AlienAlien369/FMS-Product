namespace Freebuff.Platform.Application.DTOs;

public class VehicleDto
{
    public Guid Id { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? VehicleType { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public int FuelType { get; set; }
    public decimal? FuelTankCapacity { get; set; }
    public string? FuelCapacityUnit { get; set; }
    public string? EngineNumber { get; set; }
    public string? ChassisNumber { get; set; }
    public string? VinNumber { get; set; }
    public Guid CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public Guid? DriverId { get; set; }
    public string? DriverName { get; set; }
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
    public int Status { get; set; }
    public string? DeviceImei { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceSerialNumber { get; set; }
    /// <summary>Number of ACTIVE device assignments (new multi-device model).</summary>
    public int DeviceCount { get; set; }
    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public double? LastSpeed { get; set; }
    public double? LastHeading { get; set; }
    public DateTime? LastLocationUpdate { get; set; }
    public bool? IgnitionStatus { get; set; }
    public long? OdometerReading { get; set; }
    public long? EngineHours { get; set; }
    public DateTime CreatedAt { get; set; }

    // Sensor policies — per-vehicle overrides of the company fleet defaults (null = follow default)
    public double? SpeedPolicyMaxKmh { get; set; }
    public double? TyrePressureMinBar { get; set; }
    public double? TyrePressureMaxBar { get; set; }
}

public class CreateVehicleDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? VehicleType { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public int FuelType { get; set; } = 1;
    public decimal? FuelTankCapacity { get; set; }
    public string? FuelCapacityUnit { get; set; } = "liters";
    public string? EngineNumber { get; set; }
    public string? ChassisNumber { get; set; }
    public string? VinNumber { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? ClientId { get; set; }
    public string? DeviceImei { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceSerialNumber { get; set; }
    public double? SpeedPolicyMaxKmh { get; set; }
    public double? TyrePressureMinBar { get; set; }
    public double? TyrePressureMaxBar { get; set; }
}

public class UpdateVehicleDto
{
    public string? Name { get; set; }
    public string? VehicleType { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public int? FuelType { get; set; }
    public decimal? FuelTankCapacity { get; set; }
    public string? FuelCapacityUnit { get; set; }
    public string? EngineNumber { get; set; }
    public string? ChassisNumber { get; set; }
    public string? VinNumber { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? ClientId { get; set; }
    public string? DeviceImei { get; set; }
    public string? DeviceType { get; set; }
    public string? DeviceSerialNumber { get; set; }
    public int? Status { get; set; }
    public long? OdometerReading { get; set; }
    public long? EngineHours { get; set; }

    /// <summary>Per-vehicle policy overrides. Null = don't touch; 0 = clear the
    /// override and revert to the fleet default (0 is not a valid policy value).</summary>
    public double? SpeedPolicyMaxKmh { get; set; }
    public double? TyrePressureMinBar { get; set; }
    public double? TyrePressureMaxBar { get; set; }
}

/// <summary>Live sensor snapshot for one vehicle (speed vs policy + per-tyre pressure).</summary>
public class VehicleSensorsDto
{
    public double? SpeedKmh { get; set; }

    /// <summary>Hardware governor limit the device reports (null when the device/vendor doesn't expose it).</summary>
    public double? SpeedGovernorLimitKmh { get; set; }

    /// <summary>Resolved speed policy for this vehicle (override ?? fleet default); null = no policy configured.</summary>
    public double? PolicySpeedMaxKmh { get; set; }

    /// <summary>ok | over | noPolicy | noData</summary>
    public string SpeedStatus { get; set; } = "noData";

    /// <summary>True when the device reported a governor limit; false → "not supported by this device".</summary>
    public bool GovernorSupported { get; set; }

    /// <summary>True when any tyre reading exists; false → TPMS not supported by this device.</summary>
    public bool TyresSupported { get; set; }

    /// <summary>Resolved tyre policy range (override ?? fleet default); null = no policy configured.</summary>
    public double? TyrePolicyMinBar { get; set; }
    public double? TyrePolicyMaxBar { get; set; }

    public List<TyreSensorDto> Tyres { get; set; } = new();
    public DateTime? LastUpdate { get; set; }
}

/// <summary>Sensor history response: hourly rollups for one vehicle.</summary>
public class VehicleSensorHistoryDto
{
    public Guid VehicleId { get; set; }
    public List<SensorRollupDto> Items { get; set; } = new();
}

/// <summary>One hourly min/max/avg bucket of a sensor stream (speed or a single tyre).</summary>
public class SensorRollupDto
{
    /// <summary>"speed" | "tyre"</summary>
    public string SensorType { get; set; } = "speed";

    /// <summary>Set when SensorType = "tyre" (canonical TyrePosition int); null for speed.</summary>
    public int? TyrePosition { get; set; }

    public DateTime HourBucketUtc { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public double AvgValue { get; set; }
    public int ReadingCount { get; set; }
}

public class TyreSensorDto
{
    public int Position { get; set; }
    public string PositionName { get; set; } = string.Empty;
    public double? PressureBar { get; set; }
    public double? TemperatureC { get; set; }

    /// <summary>ok | warning | critical | notSupported</summary>
    public string Status { get; set; } = "notSupported";
}

public class DriverDto
{
    public Guid Id { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public string? LicenseCategory { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? ProfileImageUrl { get; set; }
    public Guid CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int Status { get; set; }
    public decimal? SafetyScore { get; set; }
    public decimal? BehaviourScore { get; set; }
    public Guid? AssignedVehicleId { get; set; }
    public string? AssignedVehicleReg { get; set; }
    public int TripCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateDriverDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public string? LicenseCategory { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? ProfileImageUrl { get; set; }
}

public class UpdateDriverDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public string? LicenseCategory { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? ProfileImageUrl { get; set; }
    public int? Status { get; set; }
}

public class ClientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public Guid CompanyId { get; set; }
    public int Status { get; set; }
}

public class CreateClientDto
{
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}
