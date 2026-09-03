using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Package entity. Packages define what modules/features/limits are included in a subscription.
/// </summary>
public class Package : BaseEntity
{
    // ── Basic Info ─────────────────────────────────────
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? Highlights { get; set; } // JSON array of highlight strings
    public string? TermsOfServiceUrl { get; set; }
    public string? WelcomeMessage { get; set; } // Shown to company after subscription

    // ── Pricing ────────────────────────────────────────
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly"; // monthly, yearly, custom
    public decimal? YearlyPrice { get; set; } // Discounted yearly price
    public decimal? SetupFee { get; set; } // One-time setup fee
    public decimal? MinCommitment { get; set; } // Minimum monthly commitment
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsCustom { get; set; } // Custom packages for specific companies

    // ── Trial ──────────────────────────────────────────
    public int TrialDays { get; set; } = 0; // 0 = no trial
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
    public decimal OveragePricePerUser { get; set; } = 0;
    public decimal OveragePricePerVehicle { get; set; } = 0;
    public decimal OveragePricePerDriver { get; set; } = 0;
    public decimal OveragePricePerTrip { get; set; } = 0;
    public decimal OveragePricePerApiCall { get; set; } = 0;
    public decimal OveragePricePerGbStorage { get; set; } = 0;

    // ── Support & SLA ──────────────────────────────────
    public string SupportLevel { get; set; } = "basic"; // basic, standard, premium, enterprise
    public int SlaUptimePercent { get; set; } = 99; // 99, 99.5, 99.9, 99.99
    public string? SupportHours { get; set; } // "24/7", "Business hours", etc.
    public string? SupportContactEmail { get; set; }
    public string? SupportContactPhone { get; set; }
    public int ResponseTimeHours { get; set; } = 48; // Max response time in hours
    public int ResolutionTimeHours { get; set; } = 72; // Max resolution time in hours

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

    // ── Navigation ─────────────────────────────────────
    public ICollection<PackageFeature> PackageFeatures { get; set; } = new List<PackageFeature>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
