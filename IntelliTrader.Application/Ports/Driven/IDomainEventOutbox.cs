using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Durable outbox for domain events that must be committed with aggregate state.
/// </summary>
public interface IDomainEventOutbox
{
    Task EnqueueAsync(
        IEnumerable<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DomainEventOutboxMessage>> GetUnprocessedAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record DomainEventOutboxMessage(
    Guid EventId,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? ProcessedAt)
{
    public bool IsProcessed => ProcessedAt.HasValue;
}
