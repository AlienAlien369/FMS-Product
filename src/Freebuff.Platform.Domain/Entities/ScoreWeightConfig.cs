namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Category weights for the Driver Scorecard composite — Super Admin sets the
/// platform default (CompanyId = null); a Company Admin may override within
/// their own company (entitlement-then-override, same pattern as every other
/// company-preference in the product). Weights are non-negative; the composite
/// blend normalizes them to sum 1 at compute time, so a fleet that cares about
/// safety can set 0.7/0.1/0.1/0.1 without any arithmetic ceremony.
/// </summary>
public class ScoreWeightConfig
{
    public Guid Id { get; set; }

    /// <summary>Company this override applies to; NULL = the platform default.</summary>
    public Guid? CompanyId { get; set; }

    public double SafetyWeight { get; set; }
    public double ComplianceWeight { get; set; }
    public double PunctualityWeight { get; set; }
    public double BehaviorWeight { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }

    public Company? Company { get; set; }
}