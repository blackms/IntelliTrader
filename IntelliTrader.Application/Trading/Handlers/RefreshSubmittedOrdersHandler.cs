using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.Trading.Orders;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Refreshes a batch of persisted non-terminal orders by delegating to the
/// single-order reconciliation use case.
/// </summary>
public sealed class RefreshSubmittedOrdersHandler
    : ICommandHandler<RefreshSubmittedOrdersCommand, RefreshSubmittedOrdersResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICommandDispatcher _commandDispatcher;

    public RefreshSubmittedOrdersHandler(
        IOrderRepository orderRepository,
        ICommandDispatcher commandDispatcher)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
    }

    public async Task<Result<RefreshSubmittedOrdersResult>> HandleAsync(
        RefreshSubmittedOrdersCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var allOrders = await _orderRepository.GetAllAsync(cancellationToken);
        var refreshableOrders = allOrders
            .Where(order => !order.IsTerminal)
            .OrderBy(order => order.SubmittedAt)
            .ToList();

        var limit = command.Limit <= 0 ? 50 : command.Limit;
        var batch = refreshableOrders
            .Take(limit)
            .ToList();

        var refreshedCount = 0;
        var appliedDomainEffectsCount = 0;
        var failedCount = 0;

        foreach (var order in batch)
        {
            var refreshResult = await _commandDispatcher.DispatchAsync<RefreshOrderStatusCommand, RefreshOrderStatusResult>(
                new RefreshOrderStatusCommand
                {
                    OrderId = order.Id
                },
                cancellationToken);

            if (refreshResult.IsFailure)
            {
                failedCount++;
                continue;
            }

            refreshedCount++;
            if (refreshResult.Value.AppliedDomainEffects)
            {
                appliedDomainEffectsCount++;
            }
        }

        return Result<RefreshSubmittedOrdersResult>.Success(new RefreshSubmittedOrdersResult
        {
            TotalSubmitted = refreshableOrders.Count,
            AttemptedCount = batch.Count,
            RefreshedCount = refreshedCount,
            AppliedDomainEffectsCount = appliedDomainEffectsCount,
            FailedCount = failedCount
        });
    }
}
