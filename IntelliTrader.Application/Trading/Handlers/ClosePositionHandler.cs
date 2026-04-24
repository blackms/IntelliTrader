using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Handler for closing trading positions by position ID.
/// </summary>
public sealed class ClosePositionHandler : ICommandHandler<ClosePositionCommand, ClosePositionResult>
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IExchangePort _exchangePort;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly INotificationPort? _notificationPort;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ITransactionalUnitOfWork _unitOfWork;

    public ClosePositionHandler(
        IPortfolioRepository portfolioRepository,
        IPositionRepository positionRepository,
        IOrderRepository orderRepository,
        IExchangePort exchangePort,
        IDomainEventDispatcher eventDispatcher,
        TradingConstraintValidator constraintValidator,
        ITransactionalUnitOfWork unitOfWork,
        INotificationPort? notificationPort = null)
    {
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        _constraintValidator = constraintValidator ?? throw new ArgumentNullException(nameof(constraintValidator));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _notificationPort = notificationPort;
    }

    public async Task<Result<ClosePositionResult>> HandleAsync(
        ClosePositionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Get the position
        var position = await _positionRepository.GetByIdAsync(command.PositionId, cancellationToken);
        if (position is null)
        {
            return Result<ClosePositionResult>.Failure(
                Error.NotFound("Position", command.PositionId.Value.ToString()));
        }

        // 2. Check if position is already closed
        if (position.IsClosed)
        {
            return Result<ClosePositionResult>.Failure(
                Error.Conflict($"Position {command.PositionId} is already closed"));
        }

        // 3. Get the portfolio
        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<ClosePositionResult>.Failure(
                Error.NotFound("Portfolio", "default"));
        }

        // 4. Get current price
        var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
        if (priceResult.IsFailure)
        {
            return Result<ClosePositionResult>.Failure(priceResult.Error);
        }

        var currentPrice = priceResult.Value;

        // 5. Validate close position constraints
        var closeConstraints = CreateCloseConstraints(command);
        var validation = _constraintValidator.ValidateClosePosition(
            portfolio,
            position,
            currentPrice,
            closeConstraints);

        if (!validation.IsValid)
        {
            var errorMessage = string.Join("; ", validation.Errors.Select(e => e.Message));
            return Result<ClosePositionResult>.Failure(
                Error.Validation(errorMessage));
        }

        // 6. Place sell order on exchange
        ExchangeOrderResult orderResult;
        if (command.UseLimitOrder && command.MinPrice != null)
        {
            var limitOrderResult = await _exchangePort.PlaceLimitSellAsync(
                position.Pair,
                position.TotalQuantity,
                command.MinPrice,
                cancellationToken);

            if (limitOrderResult.IsFailure)
            {
                return Result<ClosePositionResult>.Failure(limitOrderResult.Error);
            }

            orderResult = limitOrderResult.Value;
        }
        else
        {
            var marketOrderResult = await _exchangePort.PlaceMarketSellAsync(
                position.Pair,
                position.TotalQuantity,
                cancellationToken);

            if (marketOrderResult.IsFailure)
            {
                return Result<ClosePositionResult>.Failure(marketOrderResult.Error);
            }

            orderResult = marketOrderResult.Value;
        }

        var orderLifecycle = ExchangeOrderLifecycleFactory.Create(
            orderResult,
            intent: OrderIntent.ClosePosition,
            relatedPositionId: position.Id);

        // 7. Check if order was fully filled
        if (orderLifecycle.Status != OrderLifecycleStatus.Filled)
        {
            if (orderLifecycle.HasUnappliedFill)
            {
                return await PersistPartialCloseOrderFailureAsync(
                    position,
                    portfolio,
                    orderLifecycle,
                    orderResult.Status,
                    cancellationToken);
            }

            return await PersistPendingOrderFailureAsync(orderLifecycle, orderResult.Status, cancellationToken);
        }

        // 8. Calculate proceeds and fees
        var proceeds = orderLifecycle.Cost; // For sell orders, cost represents the proceeds
        var sellFees = orderLifecycle.Fees;

        // 9. Close the position
        position.Close(
            orderLifecycle.Id,
            orderLifecycle.AveragePrice,
            sellFees);

        // 10. Update portfolio - record position closed with proceeds
        portfolio.RecordPositionClosed(
            position.Id,
            position.Pair,
            proceeds);
        orderLifecycle.MarkCurrentFillApplied();

        // 11. Save changes
        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(orderLifecycle, cancellationToken);
            await _positionRepository.SaveAsync(position, cancellationToken);
            await _portfolioRepository.SaveAsync(portfolio, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<ClosePositionResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<ClosePositionResult>.Failure(
                Error.ExchangeError($"Failed to save closed position: {ex.Message}"));
        }

        // 12. Dispatch domain events
        await DispatchDomainEventsAsync(orderLifecycle, position, portfolio, cancellationToken);

        // 13. Calculate realized PnL
        var totalCost = position.TotalCost + position.TotalFees + sellFees;
        var realizedPnL = proceeds - totalCost;
        var realizedMargin = totalCost.Amount > 0
            ? Margin.Calculate(totalCost.Amount, proceeds.Amount)
            : Margin.Zero;

        // 14. Send notification
        await SendNotificationAsync(position, orderResult, realizedPnL, realizedMargin, command.Reason, cancellationToken);

        // 15. Return result
        return Result<ClosePositionResult>.Success(new ClosePositionResult
        {
            PositionId = position.Id,
            Pair = position.Pair,
            SellOrderId = orderLifecycle.Id,
            SellPrice = orderLifecycle.AveragePrice,
            Quantity = orderLifecycle.FilledQuantity,
            Proceeds = proceeds,
            Fees = sellFees,
            TotalCost = totalCost,
            RealizedPnL = realizedPnL,
            RealizedMargin = realizedMargin,
            HoldingPeriod = position.ClosedAt!.Value - position.OpenedAt,
            Reason = command.Reason,
            ClosedAt = position.ClosedAt!.Value
        });
    }

    private static ClosePositionConstraints CreateCloseConstraints(ClosePositionCommand command)
    {
        // For manual closes and stop-losses, we don't enforce constraints
        return command.Reason switch
        {
            CloseReason.StopLoss => ClosePositionConstraints.Default,
            CloseReason.Manual => ClosePositionConstraints.Default,
            CloseReason.SystemShutdown => ClosePositionConstraints.Default,
            _ => ClosePositionConstraints.Default
        };
    }

    private async Task<Result<ClosePositionResult>> PersistPendingOrderFailureAsync(
        OrderLifecycle orderLifecycle,
        OrderStatus exchangeStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(orderLifecycle, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<ClosePositionResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<ClosePositionResult>.Failure(
                Error.ExchangeError($"Failed to save order lifecycle: {ex.Message}"));
        }

        await DispatchOrderEventsAsync(orderLifecycle, cancellationToken);

        return Result<ClosePositionResult>.Failure(
            Error.ExchangeError($"Sell order was not filled. Status: {exchangeStatus}"));
    }

    private async Task<Result<ClosePositionResult>> PersistPartialCloseOrderFailureAsync(
        Position position,
        Portfolio portfolio,
        OrderLifecycle orderLifecycle,
        OrderStatus exchangeStatus,
        CancellationToken cancellationToken)
    {
        var closeDelta = position.ApplyCloseFillDelta(
            orderLifecycle.Id,
            orderLifecycle.AveragePrice,
            orderLifecycle.UnappliedQuantity,
            orderLifecycle.UnappliedFees);

        if (closeDelta.PositionClosed)
        {
            portfolio.RecordPositionClosed(position.Id, position.Pair, closeDelta.Proceeds);
        }
        else
        {
            portfolio.RecordPositionCostReduced(
                position.Id,
                position.Pair,
                closeDelta.ReleasedCost,
                closeDelta.Proceeds);
        }

        orderLifecycle.MarkCurrentFillApplied();

        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(orderLifecycle, cancellationToken);
            await _positionRepository.SaveAsync(position, cancellationToken);
            await _portfolioRepository.SaveAsync(portfolio, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<ClosePositionResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<ClosePositionResult>.Failure(
                Error.ExchangeError($"Failed to save partial close effects: {ex.Message}"));
        }

        await DispatchDomainEventsAsync(orderLifecycle, position, portfolio, cancellationToken);

        return Result<ClosePositionResult>.Failure(
            Error.ExchangeError($"Sell order was not filled. Status: {exchangeStatus}"));
    }

    private async Task BeginTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
    }

    private async Task DispatchOrderEventsAsync(
        OrderLifecycle orderLifecycle,
        CancellationToken cancellationToken)
    {
        var orderEvents = orderLifecycle.DomainEvents.ToList();
        orderLifecycle.ClearDomainEvents();

        await _eventDispatcher.DispatchManyAsync(orderEvents, cancellationToken);
    }

    private async Task DispatchDomainEventsAsync(
        OrderLifecycle orderLifecycle,
        Position position,
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        var orderEvents = orderLifecycle.DomainEvents.ToList();
        var positionEvents = position.DomainEvents.ToList();
        var portfolioEvents = portfolio.DomainEvents.ToList();

        orderLifecycle.ClearDomainEvents();
        position.ClearDomainEvents();
        portfolio.ClearDomainEvents();

        var allEvents = orderEvents.Concat(positionEvents).Concat(portfolioEvents);
        await _eventDispatcher.DispatchManyAsync(allEvents, cancellationToken);
    }

    private async Task SendNotificationAsync(
        Position position,
        ExchangeOrderResult orderResult,
        Money realizedPnL,
        Margin realizedMargin,
        CloseReason reason,
        CancellationToken cancellationToken)
    {
        if (_notificationPort is null)
            return;

        try
        {
            var pnlEmoji = realizedPnL.Amount >= 0 ? "+" : "";
            var notification = new Notification
            {
                Title = $"Position Closed ({reason})",
                Message = $"Closed {position.Pair} position\n" +
                          $"Price: {orderResult.AveragePrice}\n" +
                          $"Quantity: {orderResult.FilledQuantity}\n" +
                          $"Proceeds: {orderResult.Cost}\n" +
                          $"PnL: {pnlEmoji}{realizedPnL} ({realizedMargin})\n" +
                          $"Reason: {reason}",
                Type = NotificationType.Trade,
                Priority = reason == CloseReason.StopLoss ? NotificationPriority.High : NotificationPriority.Normal
            };

            await _notificationPort.SendAsync(notification, cancellationToken);
        }
        catch
        {
            // Notification failure should not fail the operation
        }
    }
}

/// <summary>
/// Handler for closing trading positions by trading pair.
/// </summary>
public sealed class ClosePositionByPairHandler : ICommandHandler<ClosePositionByPairCommand, ClosePositionResult>
{
    private readonly IPositionRepository _positionRepository;
    private readonly ClosePositionHandler _closePositionHandler;

    public ClosePositionByPairHandler(
        IPositionRepository positionRepository,
        ClosePositionHandler closePositionHandler)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _closePositionHandler = closePositionHandler ?? throw new ArgumentNullException(nameof(closePositionHandler));
    }

    public async Task<Result<ClosePositionResult>> HandleAsync(
        ClosePositionByPairCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Find position by pair
        var position = await _positionRepository.GetByPairAsync(command.Pair, cancellationToken);
        if (position is null)
        {
            return Result<ClosePositionResult>.Failure(
                Error.NotFound("Position", $"pair {command.Pair}"));
        }

        // Delegate to the main handler
        var closeCommand = new ClosePositionCommand
        {
            PositionId = position.Id,
            MinPrice = command.MinPrice,
            UseLimitOrder = command.UseLimitOrder,
            Reason = command.Reason
        };

        return await _closePositionHandler.HandleAsync(closeCommand, cancellationToken);
    }
}
