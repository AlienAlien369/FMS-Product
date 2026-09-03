namespace Freebuff.Platform.Application.DTOs;

public class PackageDto
{
    // ── Basic Info ─────────────────────────────────────
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Highlights { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? WelcomeMessage { get; set; }

    // ── Pricing ────────────────────────────────────────
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public decimal? YearlyPrice { get; set; }
    public decimal? SetupFee { get; set; }
    public decimal? MinCommitment { get; set; }
    public int Status { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsCustom { get; set; }

    // ── Trial ──────────────────────────────────────────
    public int TrialDays { get; set; }
    public bool AllowTrialExtension { get; set; }
    public int MaxTrialExtensions { get; set; }
    public int TrialExtensionDays { get; set; }

    // ── Resource Limits ────────────────────────────────
    public int MaxUsers { get; set; }
    public int MaxVehicles { get; set; }
    public int MaxDrivers { get; set; }
    public int MaxTripsPerDay { get; set; }
    public int MaxRoutes { get; set; }
    public int MaxReportsPerDay { get; set; }
    public long StorageLimitMb { get; set; }
    public int MaxApiCallsPerDay { get; set; }
    public int MaxTrackingDevices { get; set; }
    public int MaxAlertRules { get; set; }
    public int MaxGeofences { get; set; }
    public int MaxDocuments { get; set; }
    public int MaxNotificationsPerDay { get; set; }

    // ── Overage Pricing ────────────────────────────────
    public decimal OveragePricePerUser { get; set; }
    public decimal OveragePricePerVehicle { get; set; }
    public decimal OveragePricePerDriver { get; set; }
    public decimal OveragePricePerTrip { get; set; }
    public decimal OveragePricePerApiCall { get; set; }
    public decimal OveragePricePerGbStorage { get; set; }

    // ── Support & SLA ──────────────────────────────────
    public string SupportLevel { get; set; } = "basic";
    public int SlaUptimePercent { get; set; }
    public string? SupportHours { get; set; }
    public string? SupportContactEmail { get; set; }
    public string? SupportContactPhone { get; set; }
    public int ResponseTimeHours { get; set; }
    public int ResolutionTimeHours { get; set; }

    // ── Feature Flags ──────────────────────────────────
    public bool EnableLiveTracking { get; set; }
    public bool EnableGeofencing { get; set; }
    public bool EnableAlerts { get; set; }
    public bool EnableReports { get; set; }
    public bool EnableFuelMonitoring { get; set; }
    public bool EnableMaintenance { get; set; }
    public bool EnableRouteOptimization { get; set; }
    public bool EnableProofOfDelivery { get; set; }
    public bool EnableCctv { get; set; }
    public bool EnableSmsNotifications { get; set; }
    public bool EnableEmailNotifications { get; set; }
    public bool EnableWebhookIntegrations { get; set; }
    public bool EnableApiAccess { get; set; }
    public bool EnableBulkImport { get; set; }
    public bool EnableExport { get; set; }
    public bool EnableCustomFields { get; set; }
    public bool EnableMultiCompany { get; set; }
    public bool EnableAuditLog { get; set; }

    // ── Stats ──────────────────────────────────────────
    public int ActiveSubscriptions { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePackageDto
{
    // ── Basic Info ─────────────────────────────────────
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Highlights { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? WelcomeMessage { get; set; }

    // ── Pricing ────────────────────────────────────────
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public decimal? YearlyPrice { get; set; }
    public decimal? SetupFee { get; set; }
    public decimal? MinCommitment { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }

    // ── Trial ──────────────────────────────────────────
    public int TrialDays { get; set; } = 0;
    public bool AllowTrialExtension { get; set; }
    public int MaxTrialExtensions { get; set; } = 1;
    public int TrialExtensionDays { get; set; } = 7;

    // ── Resource Limits ────────────────────────────────
    public int MaxUsers { get; set; } = 5;
    public int MaxVehicles { get; set; } = 10;
    public int MaxDrivers { get; set; } = 10;
    public int MaxTripsPerDay { get; set; } = 50;
    public int MaxRoutes { get; set; } = 10;
    public int MaxReportsPerDay { get; set; } = 20;
    public long StorageLimitMb { get; set; } = 1024;
    public int MaxApiCallsPerDay { get; set; } = 1000;
    public int MaxTrackingDevices { get; set; } = 10;
    public int MaxAlertRules { get; set; } = 20;
    public int MaxGeofences { get; set; } = 10;
    public int MaxDocuments { get; set; } = 100;
    public int MaxNotificationsPerDay { get; set; } = 500;

    // ── Overage Pricing ────────────────────────────────
    public decimal OveragePricePerUser { get; set; }
    public decimal OveragePricePerVehicle { get; set; }
    public decimal OveragePricePerDriver { get; set; }
    public decimal OveragePricePerTrip { get; set; }
    public decimal OveragePricePerApiCall { get; set; }
    public decimal OveragePricePerGbStorage { get; set; }

    // ── Support & SLA ──────────────────────────────────
    public string SupportLevel { get; set; } = "basic";
    public int SlaUptimePercent { get; set; } = 99;
    public string? SupportHours { get; set; }
    public string? SupportContactEmail { get; set; }
    public string? SupportContactPhone { get; set; }
    public int ResponseTimeHours { get; set; } = 48;
    public int ResolutionTimeHours { get; set; } = 72;

    // ── Feature Flags ──────────────────────────────────
    public bool EnableLiveTracking { get; set; } = true;
    public bool EnableGeofencing { get; set; } = true;
    public bool EnableAlerts { get; set; } = true;
    public bool EnableReports { get; set; } = true;
    public bool EnableFuelMonitoring { get; set; }
    public bool EnableMaintenance { get; set; }
    public bool EnableRouteOptimization { get; set; }
    public bool EnableProofOfDelivery { get; set; }
    public bool EnableCctv { get; set; }
    public bool EnableSmsNotifications { get; set; }
    public bool EnableEmailNotifications { get; set; } = true;
    public bool EnableWebhookIntegrations { get; set; }
    public bool EnableApiAccess { get; set; } = true;
    public bool EnableBulkImport { get; set; }
    public bool EnableExport { get; set; } = true;
    public bool EnableCustomFields { get; set; }
    public bool EnableMultiCompany { get; set; }
    public bool EnableAuditLog { get; set; } = true;
}

public class UpdatePackageDto
{
    // ── Basic Info ─────────────────────────────────────
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Highlights { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? WelcomeMessage { get; set; }

    // ── Pricing ────────────────────────────────────────
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? BillingCycle { get; set; }
    public decimal? YearlyPrice { get; set; }
    public decimal? SetupFee { get; set; }
    public decimal? MinCommitment { get; set; }
    public int? DisplayOrder { get; set; }
    public bool? IsDefault { get; set; }
    public int? Status { get; set; }

    // ── Trial ──────────────────────────────────────────
    public int? TrialDays { get; set; }
    public bool? AllowTrialExtension { get; set; }
    public int? MaxTrialExtensions { get; set; }
    public int? TrialExtensionDays { get; set; }

    // ── Resource Limits ────────────────────────────────
    public int? MaxUsers { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxDrivers { get; set; }
    public int? MaxTripsPerDay { get; set; }
    public int? MaxRoutes { get; set; }
    public int? MaxReportsPerDay { get; set; }
    public long? StorageLimitMb { get; set; }
    public int? MaxApiCallsPerDay { get; set; }
    public int? MaxTrackingDevices { get; set; }
    public int? MaxAlertRules { get; set; }
    public int? MaxGeofences { get; set; }
    public int? MaxDocuments { get; set; }
    public int? MaxNotificationsPerDay { get; set; }

    // ── Overage Pricing ────────────────────────────────
    public decimal? OveragePricePerUser { get; set; }
    public decimal? OveragePricePerVehicle { get; set; }
    public decimal? OveragePricePerDriver { get; set; }
    public decimal? OveragePricePerTrip { get; set; }
    public decimal? OveragePricePerApiCall { get; set; }
    public decimal? OveragePricePerGbStorage { get; set; }

    // ── Support & SLA ──────────────────────────────────
    public string? SupportLevel { get; set; }
    public int? SlaUptimePercent { get; set; }
    public string? SupportHours { get; set; }
    public string? SupportContactEmail { get; set; }
    public string? SupportContactPhone { get; set; }
    public int? ResponseTimeHours { get; set; }
    public int? ResolutionTimeHours { get; set; }

    // ── Feature Flags ──────────────────────────────────
    public bool? EnableLiveTracking { get; set; }
    public bool? EnableGeofencing { get; set; }
    public bool? EnableAlerts { get; set; }
    public bool? EnableReports { get; set; }
    public bool? EnableFuelMonitoring { get; set; }
    public bool? EnableMaintenance { get; set; }
    public bool? EnableRouteOptimization { get; set; }
    public bool? EnableProofOfDelivery { get; set; }
    public bool? EnableCctv { get; set; }
    public bool? EnableSmsNotifications { get; set; }
    public bool? EnableEmailNotifications { get; set; }
    public bool? EnableWebhookIntegrations { get; set; }
    public bool? EnableApiAccess { get; set; }
    public bool? EnableBulkImport { get; set; }
    public bool? EnableExport { get; set; }
    public bool? EnableCustomFields { get; set; }
    public bool? EnableMultiCompany { get; set; }
    public bool? EnableAuditLog { get; set; }
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
    public int TrialExtensionsUsed { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
}

public class CreateSubscriptionDto
{
    public Guid CompanyId { get; set; }
    public Guid PackageId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? TaxPercentage { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxDrivers { get; set; }
    public bool StartTrial { get; set; }
}

public class RenewSubscriptionDto
{
    public DateTime? NewEndDate { get; set; }
    public decimal? NewPrice { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? TaxPercentage { get; set; }
}

public class AssignSubscriptionDto : CreateSubscriptionDto
{
    public decimal? CurrentPrice { get; set; }
    public string? Currency { get; set; }
    public string? BillingCycle { get; set; }
}
