using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Subscription entity. Maintains subscription history - never overwrite historical pricing.
/// </summary>
public class Subscription : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid PackageId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CanceledAt { get; set; }

    // Pricing
    public decimal CurrentPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public string BillingCycle { get; set; } = "monthly";
    public decimal? DiscountPercentage { get; set; }
    public decimal? TaxPercentage { get; set; }
    public decimal EffectivePrice => CurrentPrice * (1 - (DiscountPercentage ?? 0) / 100) * (1 + (TaxPercentage ?? 0) / 100);

    // Limits override (null means use package defaults)
    public int? MaxUsers { get; set; }
    public int? MaxVehicles { get; set; }
    public int? MaxDrivers { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public Package Package { get; set; } = null!;
}
