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

    Task MarkProcessedAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}
