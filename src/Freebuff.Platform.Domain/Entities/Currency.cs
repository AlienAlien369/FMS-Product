using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Currency entity.
/// </summary>
public class Currency : BaseEntity
{
    public string Code { get; set; } = string.Empty; // e.g., "USD", "EUR", "INR"
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; } = 2;
    public bool IsDefault { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public int DisplayOrder { get; set; }
}
