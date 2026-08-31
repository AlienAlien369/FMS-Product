using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Feature entity. Features belong to modules and are controlled by feature flags.
/// </summary>
public class Feature : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid ModuleId { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }
    public bool IsEnabledByDefault { get; set; } = true;

    // Navigation
    public Module Module { get; set; } = null!;
    public ICollection<PackageFeature> PackageFeatures { get; set; } = new List<PackageFeature>();
}
