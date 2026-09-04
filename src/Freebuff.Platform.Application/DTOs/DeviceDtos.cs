namespace Freebuff.Platform.Application.DTOs;

public class DeviceSimDto
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string? Iccid { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Carrier { get; set; }
    public int Status { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime? ActivatedAt { get; set; }
}

public class CreateDeviceSimDto
{
    public string? Iccid { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Carrier { get; set; }
    public int Status { get; set; } = 0; // DeviceSimStatus.Active
    public bool IsPrimary { get; set; }
}

public class DeviceDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? VendorId { get; set; }
    public string? VendorCode { get; set; }
    public string? VendorName { get; set; }
    public int DeviceType { get; set; }
    public string? DeviceTypeOverride { get; set; }
    public int IdentityType { get; set; }
    public string IdentityValue { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public int Status { get; set; }
    public DateTime? InstallDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DeviceSimDto> Sims { get; set; } = new();
    public Guid? CurrentVehicleId { get; set; }
    public string? CurrentVehicleRegistration { get; set; }
}

public class DeviceUpdateDto
{
    public int? DeviceType { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public int? Status { get; set; }
    public string? RawMetadata { get; set; }
}

public class DeviceVendorDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AdapterVersion { get; set; }
    public int ProtocolType { get; set; }
    public string? PayloadFormat { get; set; }
    public string? ListenerConfig { get; set; }
    public string? Capabilities { get; set; }
    public int Status { get; set; }
    /// <summary>Number of non-deleted devices registered under this vendor (admin view).</summary>
    public int DeviceCount { get; set; }
}

public class CreateDeviceVendorDto
{
    /// <summary>Lowercase kebab-case unique identifier — the adapter registry key.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AdapterVersion { get; set; }
    public int ProtocolType { get; set; } = 2; // DeviceProtocolType.HttpWebhook
    public string? PayloadFormat { get; set; }
    public int Status { get; set; } = 0; // DeviceStatus.Active
    public string? ListenerConfig { get; set; }
    public string? Capabilities { get; set; }
}

public class UpdateDeviceVendorDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AdapterVersion { get; set; }
    public int? ProtocolType { get; set; }
    public string? PayloadFormat { get; set; }
    public int? Status { get; set; }
    public string? ListenerConfig { get; set; }
    public string? Capabilities { get; set; }
}

public class CreateDeviceDto
{
    /// <summary>Only honored for SuperAdmin; company users are scoped to their own company.</summary>
    public Guid? CompanyId { get; set; }
    public string VendorCode { get; set; } = string.Empty;
    public int DeviceType { get; set; } = 0;
    public int IdentityType { get; set; } = 0; // DeviceIdentityType.Imei
    public string IdentityValue { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public List<CreateDeviceSimDto> Sims { get; set; } = new();
}

public class AssignDeviceDto
{
    public Guid DeviceId { get; set; }
    public int Role { get; set; } = 0; // VehicleDeviceRole.PrimaryTracker
}

/// <summary>A vehicle's device assignment, with the device + vendor + SIM detail.</summary>
public class VehicleDeviceDto
{
    public Guid Id { get; set; }              // VehicleDevice assignment id
    public Guid VehicleId { get; set; }
    public Guid DeviceId { get; set; }
    public int Role { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public DateTime AssignedFrom { get; set; }
    public DateTime? AssignedTo { get; set; }
    public string? UnassignReason { get; set; }

    public string? VendorCode { get; set; }
    public string? VendorName { get; set; }
    public int DeviceType { get; set; }
    public string? DeviceTypeOverride { get; set; }
    public int IdentityType { get; set; }
    public string IdentityValue { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int DeviceStatus { get; set; }
    public List<DeviceSimDto> Sims { get; set; } = new();
}
