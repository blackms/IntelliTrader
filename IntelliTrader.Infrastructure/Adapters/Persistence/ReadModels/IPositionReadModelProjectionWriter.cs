using IntelliTrader.Domain.Trading.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public interface IPositionReadModelProjectionWriter
{
    Task ProjectAsync(PositionOpened domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(DCAExecuted domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(PositionPartiallyClosed domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(PositionClosed domainEvent, CancellationToken cancellationToken = default);
}
