using IntelliTrader.Domain.Trading.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public interface IPortfolioReadModelProjectionWriter
{
    Task ProjectAsync(PositionAddedToPortfolio domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(PositionRemovedFromPortfolio domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(PortfolioBalanceChanged domainEvent, CancellationToken cancellationToken = default);
}
