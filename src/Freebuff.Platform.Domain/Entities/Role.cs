using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Dynamic Role entity. Companies create their own roles.
/// </summary>
public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CompanyId { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public bool IsSystemRole { get; set; } // System roles cannot be deleted
    public int DisplayOrder { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
