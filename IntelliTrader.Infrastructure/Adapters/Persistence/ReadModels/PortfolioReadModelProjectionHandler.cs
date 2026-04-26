using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public sealed class PortfolioReadModelProjectionHandler :
    IDomainEventHandler<PositionAddedToPortfolio>,
    IDomainEventHandler<PositionRemovedFromPortfolio>,
    IDomainEventHandler<PortfolioBalanceChanged>
{
    private readonly IPortfolioReadModelProjectionWriter _projectionWriter;

    public PortfolioReadModelProjectionHandler(IPortfolioReadModelProjectionWriter projectionWriter)
    {
        _projectionWriter = projectionWriter ?? throw new ArgumentNullException(nameof(projectionWriter));
    }

    public Task HandleAsync(PositionAddedToPortfolio domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(PositionRemovedFromPortfolio domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(PortfolioBalanceChanged domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }
}
