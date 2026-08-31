using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Permission entity. Defines what actions can be performed on what resources.
/// </summary>
public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty; // e.g., "vehicle.create"
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Module { get; set; } = string.Empty; // e.g., "vehicle"
    public PermissionAction Action { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }

    // Navigation
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
