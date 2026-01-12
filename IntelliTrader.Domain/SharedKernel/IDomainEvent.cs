namespace IntelliTrader.Domain.SharedKernel;

/// <summary>
/// Marker interface for domain events.
/// Domain events represent something that happened in the domain.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Base record for domain events providing common properties.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
