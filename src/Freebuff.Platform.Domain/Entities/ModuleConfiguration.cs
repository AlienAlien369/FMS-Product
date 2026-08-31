using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Maps which modules are enabled for which company.
/// </summary>
public class ModuleConfiguration : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid ModuleId { get; set; }
    public Module Module { get; set; } = null!;

    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public string? CustomConfig { get; set; } // JSON for module-specific overrides
}
