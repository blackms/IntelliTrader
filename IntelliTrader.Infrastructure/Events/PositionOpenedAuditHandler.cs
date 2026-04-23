using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.Events;

namespace IntelliTrader.Infrastructure.Events;

/// <summary>
/// Emits audit entries for newly opened positions.
/// This is the first concrete consumer of domain events in the new pipeline.
/// </summary>
public sealed class PositionOpenedAuditHandler : IDomainEventHandler<PositionOpened>
{
    private readonly IAuditService _auditService;

    public PositionOpenedAuditHandler(IAuditService auditService)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    public Task HandleAsync(PositionOpened domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var details =
            $"Position {domainEvent.PositionId.Value} opened for {domainEvent.Pair.Symbol}. " +
            $"OrderId={domainEvent.OrderId.Value}, Price={domainEvent.Price.Value}, " +
            $"Quantity={domainEvent.Quantity.Value}, Cost={domainEvent.Cost.Amount} {domainEvent.Cost.Currency}.";

        _auditService.LogAudit("PositionOpened", details);
        return Task.CompletedTask;
    }
}
