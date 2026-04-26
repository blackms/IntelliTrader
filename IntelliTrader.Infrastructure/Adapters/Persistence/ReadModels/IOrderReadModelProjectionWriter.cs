using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public interface IOrderReadModelProjectionWriter
{
    Task ProjectAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(OrderFilledEvent domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(OrderCanceledEvent domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(OrderRejectedEvent domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(OrderLinkedToPositionEvent domainEvent, CancellationToken cancellationToken = default);
}
