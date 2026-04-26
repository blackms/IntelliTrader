using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;

namespace IntelliTrader.Application.Trading.Handlers;

internal static class DomainEventOutboxWorkflow
{
    public static List<IDomainEvent> Collect(OrderLifecycle order)
    {
        return order.DomainEvents.ToList();
    }

    public static List<IDomainEvent> Collect(
        OrderLifecycle order,
        Position position,
        Portfolio portfolio)
    {
        var events = new List<IDomainEvent>();
        events.AddRange(order.DomainEvents);
        events.AddRange(position.DomainEvents);
        events.AddRange(portfolio.DomainEvents);
        return events;
    }

    public static void Clear(OrderLifecycle order)
    {
        order.ClearDomainEvents();
    }

    public static void Clear(
        OrderLifecycle order,
        Position position,
        Portfolio portfolio)
    {
        order.ClearDomainEvents();
        position.ClearDomainEvents();
        portfolio.ClearDomainEvents();
    }

    public static async Task EnqueueAsync(
        IDomainEventOutbox eventOutbox,
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await eventOutbox.EnqueueAsync(events, cancellationToken);
    }

    public static async Task DispatchCommittedAsync(
        IDomainEventDispatcher eventDispatcher,
        IDomainEventOutbox eventOutbox,
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await eventDispatcher.DispatchManyAsync(events, cancellationToken);

        foreach (var domainEvent in events)
        {
            await eventOutbox.MarkProcessedAsync(domainEvent.EventId, cancellationToken);
        }
    }
}
