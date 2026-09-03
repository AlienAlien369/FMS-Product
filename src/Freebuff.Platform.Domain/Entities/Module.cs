using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Dynamic module entity. Modules are registered and configured at platform level.
/// </summary>
public class Module : BaseEntity
{
    public string Code { get; set; } = string.Empty; // e.g., "fleet", "vehicles"
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Route { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }
    public string ModuleVersion { get; set; } = "1.0.0"; // Renamed to avoid hiding BaseEntity.Version
    public bool IsCore { get; set; } // Core modules cannot be disabled
    public string? Dependencies { get; set; } // JSON array of dependent module codes

    // Navigation
    public ICollection<Feature> Features { get; set; } = new List<Feature>();
    public ICollection<ModuleConfiguration> ModuleConfigurations { get; set; } = new List<ModuleConfiguration>();
    public ICollection<Page> Pages { get; set; } = new List<Page>();
}
