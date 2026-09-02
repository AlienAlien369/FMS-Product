namespace Freebuff.Platform.Domain.Enums;

public enum EntityStatus
{
    Active = 0,
    Inactive = 1,
    Pending = 2,
    Suspended = 3,
    Archived = 4
}

public enum EntityType
{
    Platform = 0,
    Company = 1,
    Module = 2,
    Feature = 3,
    Vehicle = 4,
    Driver = 5,
    Trip = 6,
    Geofence = 7,
    Alert = 8,
    Document = 9,
    Client = 10,
    Fuel = 11,
    Maintenance = 12,
    User = 13,
    Role = 14,
    Configuration = 15,
    Subscription = 16,
    Package = 17,
    Notification = 18,
    Report = 19,
    Other = 99
}

public enum AuditAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Restore = 3,
    Login = 4,
    Logout = 5,
    PermissionChange = 6,
    RoleChange = 7,
    ConfigurationChange = 8,
    SubscriptionChange = 9,
    DataExport = 10,
    DataImport = 11,
    Other = 99
}

public enum ConfigurationValueType
{
    Boolean = 0,
    String = 1,
    Number = 2,
    Decimal = 3,
    Json = 4,
    Date = 5,
    DateTime = 6,
    Array = 7,
    Reference = 8
}

public enum ConfigurationScope
{
    System = 0,
    Module = 1,
    Package = 2,
    Company = 3,
    Role = 4,
    User = 5,
    Vehicle = 6,
    Driver = 7
}

public enum NotificationChannel
{
    InApp = 0,
    Email = 1,
    SMS = 2,
    Push = 3,
    Webhook = 4
}

public enum AlertSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum SubscriptionStatus
{
    Active = 0,
    Trialing = 1,
    PastDue = 2,
    Canceled = 3,
    Expired = 4,
    Suspended = 5
}

public enum VehicleStatus
{
    Active = 0,
    Inactive = 1,
    InMaintenance = 2,
    Retired = 3,
    Stolen = 4
}

public enum DriverStatus
{
    Active = 0,
    Inactive = 1,
    OnTrip = 2,
    OffDuty = 3,
    Suspended = 4
}

public enum TripStatus
{
    Planned = 0,
    Started = 1,
    InProgress = 2,
    Paused = 3,
    Completed = 4,
    Cancelled = 5
}

public enum GeofenceType
{
    Circle = 0,
    Rectangle = 1,
    Polygon = 2
}

public enum RouteStatus
{
    Draft = 0,
    Active = 1,
    InProgress = 2,
    Completed = 3,
    Archived = 4,
    Cancelled = 5
}

public enum RouteType
{
    Standard = 0,
    Optimized = 1,
    Express = 2,
    RoundTrip = 3,
    MultiStop = 4
}

public enum PermissionAction
{
    Create = 0,
    Read = 1,
    Update = 2,
    Delete = 3,
    Export = 4,
    Import = 5,
    Approve = 6,
    Assign = 7,
    Execute = 8,
    Configure = 9,
    Manage = 10
}

public enum FuelType
{
    Petrol = 0,
    Diesel = 1,
    CNG = 2,
    LNG = 3,
    Electric = 4,
    Hybrid = 5,
    Hydrogen = 6,
    Other = 7
}

public enum MapProvider
{
    OpenStreetMap = 0,
    GoogleMaps = 1,
    Mapbox = 2,
    Here = 3,
    Custom = 99
}
