using Freebuff.Platform.Domain.Common;

namespace Freebuff.Platform.Domain.Events;

public abstract class BaseDomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}
