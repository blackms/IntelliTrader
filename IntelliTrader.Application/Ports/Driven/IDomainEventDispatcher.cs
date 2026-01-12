using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Port for dispatching domain events to handlers.
/// This is a driven (secondary) port - the application defines it, infrastructure implements it.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a single domain event to all registered handlers.
    /// </summary>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches multiple domain events to all registered handlers.
    /// </summary>
    Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler for a specific domain event type.
/// </summary>
/// <typeparam name="TEvent">The type of domain event to handle</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the domain event.
    /// </summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
