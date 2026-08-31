using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Many-to-many: Package ↔ Feature
/// </summary>
public class PackageFeature : BaseEntity
{
    public Guid PackageId { get; set; }
    public Package Package { get; set; } = null!;

    public Guid FeatureId { get; set; }
    public Feature Feature { get; set; } = null!;
}
