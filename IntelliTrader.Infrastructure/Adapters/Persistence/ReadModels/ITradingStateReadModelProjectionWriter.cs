using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;

public interface ITradingStateReadModelProjectionWriter
{
    Task ProjectAsync(TradingSuspendedEvent domainEvent, CancellationToken cancellationToken = default);

    Task ProjectAsync(TradingResumedEvent domainEvent, CancellationToken cancellationToken = default);
}
