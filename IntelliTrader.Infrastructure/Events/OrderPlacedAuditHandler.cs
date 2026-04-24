using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Events;

/// <summary>
/// Emits audit entries for submitted exchange orders.
/// </summary>
public sealed class OrderPlacedAuditHandler : IDomainEventHandler<OrderPlacedEvent>
{
    private readonly IAuditService _auditService;

    public OrderPlacedAuditHandler(IAuditService auditService)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    public Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var details =
            $"Order {domainEvent.OrderId} placed for {domainEvent.Pair}. " +
            $"Side={domainEvent.Side}, Type={domainEvent.OrderType}, Amount={domainEvent.Amount}, Price={domainEvent.Price}.";

        _auditService.LogAudit("OrderPlaced", details);
        return Task.CompletedTask;
    }
}
