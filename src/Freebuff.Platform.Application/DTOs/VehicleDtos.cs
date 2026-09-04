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
}

public class CreateVehicleDto
{
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
