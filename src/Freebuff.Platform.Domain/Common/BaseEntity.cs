namespace Freebuff.Platform.Domain.Common;

/// <summary>
/// Base entity for all tenant-owned business entities.
/// Provides multi-tenancy, audit tracking, soft delete, and optimistic concurrency.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    /// <summary>Tenant/Company that owns this record</summary>
    public Guid? TenantId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public string? DeletionReason { get; set; }

    // Optimistic concurrency
    public int Version { get; set; }

    // Domain events
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
