using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Many-to-many: Package ↔ Module.
/// A package grants access to whole modules (top-level groups from the page
/// registry); page-level control inside an accessible module is the role's job.
/// This replaces the legacy Package↔Feature concept.
/// </summary>
public class PackageModule : BaseEntity
{
    public Guid PackageId { get; set; }
    public Package Package { get; set; } = null!;

    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;
}
