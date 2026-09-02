namespace Freebuff.Platform.Application.DTOs;

public class PackageDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public int Status { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsCustom { get; set; }
    public int MaxUsers { get; set; }
    public int MaxVehicles { get; set; }
    public int MaxDrivers { get; set; }
    public long StorageLimitMb { get; set; }
    public int MaxApiCallsPerDay { get; set; }
    public int MaxTrackingDevices { get; set; }
    public int MaxAlertRules { get; set; }
    public int MaxGeofences { get; set; }
    public int ActiveSubscriptions { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePackageDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public int MaxUsers { get; set; } = 5;
    public int MaxVehicles { get; set; } = 10;
    public int MaxDrivers { get; set; } = 10;
    public long StorageLimitMb { get; set; } = 1024;
    public int MaxApiCallsPerDay { get; set; } = 1000;
    public int MaxTrackingDevices { get; set; } = 10;
    public int MaxAlertRules { get; set; } = 20;
    public int MaxGeofences { get; set; } = 10;
}

public class UpdatePackageDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? BillingCycle { get; set; }
    public int? DisplayOrder { get; set; }
    public bool? IsDefault { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxDrivers { get; set; }
    public long? StorageLimitMb { get; set; }
    public int? MaxApiCallsPerDay { get; set; }
    public int? MaxTrackingDevices { get; set; }
    public int? MaxAlertRules { get; set; }
    public int? MaxGeofences { get; set; }
    public int? Status { get; set; }
}

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public Guid PackageId { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CanceledAt { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public decimal? DiscountPercentage { get; set; }
    public decimal? TaxPercentage { get; set; }
    public decimal EffectivePrice { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxDrivers { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AssignSubscriptionDto
{
    public Guid CompanyId { get; set; }
    public Guid PackageId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal CurrentPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public decimal? DiscountPercentage { get; set; }
    public decimal? TaxPercentage { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxDrivers { get; set; }
}

public class RenewSubscriptionDto
{
    public DateTime NewEndDate { get; set; }
    public decimal? NewPrice { get; set; }
}
