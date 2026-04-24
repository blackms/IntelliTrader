using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Events;

namespace IntelliTrader.Infrastructure.Events;

/// <summary>
/// Emits audit entries for fully or partially filled exchange orders.
/// </summary>
public sealed class OrderFilledAuditHandler : IDomainEventHandler<OrderFilledEvent>
{
    private readonly IAuditService _auditService;

    public OrderFilledAuditHandler(IAuditService auditService)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    public Task HandleAsync(OrderFilledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var details =
            $"Order {domainEvent.OrderId} filled for {domainEvent.Pair}. " +
            $"Side={domainEvent.Side}, FilledAmount={domainEvent.FilledAmount}, AveragePrice={domainEvent.AveragePrice}, " +
            $"Cost={domainEvent.Cost}, Fees={domainEvent.Fees}, Partial={domainEvent.IsPartialFill}.";

        _auditService.LogAudit("OrderFilled", details);
        return Task.CompletedTask;
    }
}
