using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public sealed class TradingStateReadModelProjectionHandler :
    IDomainEventHandler<TradingSuspendedEvent>,
    IDomainEventHandler<TradingResumedEvent>
{
    private readonly ITradingStateReadModelProjectionWriter _projectionWriter;

    public TradingStateReadModelProjectionHandler(ITradingStateReadModelProjectionWriter projectionWriter)
    {
        _projectionWriter = projectionWriter ?? throw new ArgumentNullException(nameof(projectionWriter));
    }

    public Task HandleAsync(TradingSuspendedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(TradingResumedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }
}
