using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Package entity. Packages define what modules/features/limits are included in a subscription.
/// </summary>
public class Package : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly"; // monthly, yearly, custom
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsCustom { get; set; }

    // Limits
    public int MaxUsers { get; set; } = 5;
    public int MaxVehicles { get; set; } = 10;
    public int MaxDrivers { get; set; } = 10;
    public long StorageLimitMb { get; set; } = 1024;
    public int MaxApiCallsPerDay { get; set; } = 1000;
    public int MaxTrackingDevices { get; set; } = 10;
    public int MaxAlertRules { get; set; } = 20;
    public int MaxGeofences { get; set; } = 10;

    // Navigation
    public ICollection<PackageFeature> PackageFeatures { get; set; } = new List<PackageFeature>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
