using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Enums;

namespace Freebuff.Platform.Domain.Entities;

/// <summary>
/// Audit log entry for tracking all important changes.
/// Does not inherit BaseEntity because audit logs are never deleted.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public AuditAction Action { get; set; }
    public EntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public string? Source { get; set; }
    public string? Reason { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
}
