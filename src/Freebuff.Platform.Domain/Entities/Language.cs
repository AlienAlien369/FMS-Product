using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Localization language entity.
/// </summary>
public class Language : BaseEntity
{
    public string Code { get; set; } = string.Empty; // e.g., "en", "ar", "hi"
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public bool IsRightToLeft { get; set; }
    public bool IsDefault { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }
}
