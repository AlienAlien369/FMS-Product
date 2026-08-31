using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Many-to-many: User ↔ Role (within a company)
/// </summary>
public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
