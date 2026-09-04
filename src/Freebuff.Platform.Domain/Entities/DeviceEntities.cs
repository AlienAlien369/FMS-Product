using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Vendor catalog entry + runtime adapter registry metadata. Platform-level
/// (TenantId null, like Language/Module) — every company shares the vendor list.
/// A row's <see cref="Code"/> must match the <c>VendorCode</c> of a registered
/// adapter in Freebuff.Platform.Ingestion.
/// </summary>
public class DeviceVendor : BaseEntity
{
    public string Code { get; set; } = string.Empty;          // e.g. "sample-json", "pictor"
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AdapterVersion { get; set; }               // contract version this row expects
    public DeviceProtocolType ProtocolType { get; set; } = DeviceProtocolType.HttpWebhook;
    public string? PayloadFormat { get; set; }                // e.g. "json-webhook", "pictor-binary-v2"
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;

    /// <summary>JSON — how this vendor is reached (webhook path suffix, TCP port, MQTT topic prefix).</summary>
    public string? ListenerConfig { get; set; }

    /// <summary>JSON — canonical fields this vendor can produce (gps, ignition, fuel, temperature, driverId, …).</summary>
    public string? Capabilities { get; set; }

    /// <summary>JSON — optional ingest secret / raw-archive toggle and other catalog attributes.</summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// A physical tracking device, tenant-scoped and independent of any vehicle
/// (devices may be stock before install and can move between vehicles over time).
/// Vehicle links live on <see cref="VehicleDevice"/> assignments.
/// </summary>
public class Device : BaseEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Nullable = legacy/unidentified device; must be set before traffic is accepted (see Status AwaitingVendor).</summary>
    public Guid? VendorId { get; set; }
    public DeviceType DeviceType { get; set; } = DeviceType.GpsTracker;

    /// <summary>Original free-text device type captured during migration from the legacy Vehicle fields.</summary>
    public string? DeviceTypeOverride { get; set; }

    public DeviceIdentityType IdentityType { get; set; } = DeviceIdentityType.Imei;
    public string IdentityValue { get; set; } = string.Empty; // IMEI/serial/MAC the device transmits
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;
    public DateTime? InstallDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    /// <summary>JSON — vendor-specific extra attributes that don't fit the common schema.</summary>
    public string? RawMetadata { get; set; }
}

/// <summary>
/// A SIM installed in a device (a device can carry primary + failover SIMs).
/// SIM lifecycle is tracked independently of the device identity.
/// </summary>
public class DeviceSim : BaseEntity
{
    public Guid DeviceId { get; set; }
    public string? Iccid { get; set; }          // SIM card number
    public string? PhoneNumber { get; set; }    // MSISDN
    public string? Carrier { get; set; }
    public DeviceSimStatus Status { get; set; } = DeviceSimStatus.Active;
    public bool IsPrimary { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>JSON — APN, IP, data plan, roaming flags, other carrier metadata.</summary>
    public string? RawMetadata { get; set; }
}

/// <summary>
/// Many-to-many Vehicle ↔ Device assignment with history. Replaces the embedded
/// single-device fields on Vehicle. An active assignment is IsDeleted=false AND
/// AssignedTo IS NULL; one active assignment per (Vehicle, Role) is enforced by a
/// filtered unique index, and one active PrimaryTracker per vehicle.
/// </summary>
public class VehicleDevice : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Guid DeviceId { get; set; }
    public VehicleDeviceRole Role { get; set; } = VehicleDeviceRole.PrimaryTracker;
    public DateTime AssignedFrom { get; set; } = DateTime.UtcNow;
    public DateTime? AssignedTo { get; set; }
    public string? UnassignReason { get; set; }

    /// <summary>JSON — mounting position, sensor channel config for that vehicle, other per-assignment data.</summary>
    public string? RawMetadata { get; set; }
}
