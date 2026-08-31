using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Document entity for compliance and file management.
/// </summary>
public class Document : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? Category { get; set; } // insurance, license, permit, msds, etc.
    public DateTime? ExpiryDate { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    // Polymorphic association
    public EntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid CompanyId { get; set; }
}
