using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public sealed class PositionReadModelProjectionHandler :
    IDomainEventHandler<PositionOpened>,
    IDomainEventHandler<DCAExecuted>,
    IDomainEventHandler<PositionPartiallyClosed>,
    IDomainEventHandler<PositionClosed>
{
    private readonly IPositionReadModelProjectionWriter _projectionWriter;

    public PositionReadModelProjectionHandler(IPositionReadModelProjectionWriter projectionWriter)
    {
        _projectionWriter = projectionWriter ?? throw new ArgumentNullException(nameof(projectionWriter));
    }

    public Task HandleAsync(PositionOpened domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(DCAExecuted domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(PositionPartiallyClosed domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }

    public Task HandleAsync(PositionClosed domainEvent, CancellationToken cancellationToken = default)
    {
        return _projectionWriter.ProjectAsync(domainEvent, cancellationToken);
    }
}
