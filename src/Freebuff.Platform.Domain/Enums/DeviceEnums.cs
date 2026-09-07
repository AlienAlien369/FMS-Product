namespace Freebuff.Platform.Domain.Enums;

/// <summary>Transport a vendor uses to deliver payloads (protocol axis).</summary>
public enum DeviceProtocolType
{
    TcpRaw = 0,
    Udp = 1,
    HttpWebhook = 2,
    Mqtt = 3
}

/// <summary>Kind of tracking/telemetry device.</summary>
public enum DeviceType
{
    GpsTracker = 0,
    Dashcam = 1,
    Adas = 2,
    FuelSensor = 3,
    TemperatureSensor = 4,
    DualCamera = 5,
    /// <summary>AI driver-monitoring camera (drowsiness/distraction/phone detection + dashcam capture).</summary>
    DmsCamera = 6,
    Other = 99
}

/// <summary>What identifier the device transmits (IMEI for GSM, serial/MAC for others).</summary>
public enum DeviceIdentityType
{
    Imei = 0,
    Serial = 1,
    Mac = 2,
    PhoneNumber = 3
}

public enum DeviceStatus
{
    Active = 0,
    Inactive = 1,
    Retired = 2,
    Lost = 3,
    /// <summary>Registered from legacy data (or pre-provisioned) with no vendor yet — traffic is rejected until identified.</summary>
    AwaitingVendor = 4
}

public enum DeviceSimStatus
{
    Active = 0,
    Failover = 1,
    Blocked = 2,
    Retired = 3
}

/// <summary>Role a device plays on a particular vehicle assignment.</summary>
public enum VehicleDeviceRole
{
    PrimaryTracker = 0,
    SecondaryTracker = 1,
    Dashcam = 2,
    Adas = 3,
    FuelSensor = 4,
    TemperatureSensor = 5,
    Spare = 6,
    /// <summary>AI driver-monitoring camera (DMS) — one per vehicle alongside the primary tracker.</summary>
    DmsCamera = 7
}

/// <summary>
/// Canonical driver-behavior / DMS event type. The ADAPTER maps vendor-specific
/// codes onto these; nothing downstream ever branches on a vendor's vocabulary.
/// Stored as int on <c>DriverBehaviorEvent.EventType</c>.
/// </summary>
public enum DriverBehaviorEventType
{
    HarshBraking = 0,
    HarshAcceleration = 1,
    HarshCornering = 2,
    ExcessiveIdling = 3,
    Drowsiness = 4,
    Distraction = 5,
    PhoneUsage = 6,
    SosTriggered = 7
}

public enum TelemetryParseStatus
{
    Parsed = 0,
    Unparsed = 1,
    Failed = 2
}
