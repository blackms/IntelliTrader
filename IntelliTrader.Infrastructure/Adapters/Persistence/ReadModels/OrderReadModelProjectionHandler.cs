using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public sealed class OrderReadModelProjectionHandler :
    IDomainEventHandler<OrderPlacedEvent>,
    IDomainEventHandler<OrderFilledEvent>,
    IDomainEventHandler<OrderCanceledEvent>,
    IDomainEventHandler<OrderRejectedEvent>,
    IDomainEventHandler<OrderLinkedToPositionEvent>
{
    private readonly IOrderReadModelProjectionWriter _projectionWriter;

    public OrderReadModelProjectionHandler(IOrderReadModelProjectionWriter projectionWriter)
    {
        _projectionWriter = projectionWriter ?? throw new ArgumentNullException(nameof(projectionWriter));
    }

    public Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(OrderFilledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(OrderCanceledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(OrderRejectedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(OrderLinkedToPositionEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }
}
