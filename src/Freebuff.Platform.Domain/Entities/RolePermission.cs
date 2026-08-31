using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Many-to-many: Role ↔ Permission
/// </summary>
public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
