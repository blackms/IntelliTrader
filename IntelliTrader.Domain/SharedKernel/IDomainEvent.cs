namespace IntelliTrader.Domain.SharedKernel;

/// <summary>
/// Marker interface for domain events.
/// Domain events represent something that happened in the domain.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// When the event occurred.
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Unique identifier for event deduplication and tracking.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Optional correlation ID for tracking related events across a workflow.
    /// </summary>
    string? CorrelationId { get; }
}

/// <summary>
/// Base record for domain events providing common properties.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public string? CorrelationId { get; init; }
}
