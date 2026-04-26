using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Handler for executing DCA (Dollar Cost Averaging) entries on existing positions.
/// </summary>
public sealed class ExecuteDCAHandler : ICommandHandler<ExecuteDCACommand, ExecuteDCAResult>
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IExchangePort _exchangePort;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly IDomainEventOutbox _eventOutbox;
    private readonly INotificationPort? _notificationPort;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ITransactionalUnitOfWork _unitOfWork;

    public ExecuteDCAHandler(
        IPortfolioRepository portfolioRepository,
        IPositionRepository positionRepository,
        IOrderRepository orderRepository,
        IExchangePort exchangePort,
        IDomainEventDispatcher eventDispatcher,
        IDomainEventOutbox eventOutbox,
        TradingConstraintValidator constraintValidator,
        ITransactionalUnitOfWork unitOfWork,
        INotificationPort? notificationPort = null)
    {
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        _eventOutbox = eventOutbox ?? throw new ArgumentNullException(nameof(eventOutbox));
        _constraintValidator = constraintValidator ?? throw new ArgumentNullException(nameof(constraintValidator));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _notificationPort = notificationPort;
    }

    public async Task<Result<ExecuteDCAResult>> HandleAsync(
        ExecuteDCACommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Get the position
        var position = await _positionRepository.GetByIdAsync(command.PositionId, cancellationToken);
        if (position is null)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.NotFound("Position", command.PositionId.Value.ToString()));
        }

        // 2. Check if position is already closed
        if (position.IsClosed)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.Conflict($"Position {command.PositionId} is already closed"));
        }

        // 3. Get the portfolio
        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.NotFound("Portfolio", "default"));
        }

        // 4. Get current price
        var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
        if (priceResult.IsFailure)
        {
            return Result<ExecuteDCAResult>.Failure(priceResult.Error);
        }

        var currentPrice = priceResult.Value;

        // 5. Validate DCA constraints
        var dcaConstraints = DCAConstraints.Default;
        var validation = _constraintValidator.ValidateDCA(
            portfolio,
            position,
            command.Cost,
            currentPrice,
            dcaConstraints);

        if (!validation.IsValid)
        {
            var errorMessage = string.Join("; ", validation.Errors.Select(e => e.Message));
            return Result<ExecuteDCAResult>.Failure(
                Error.Validation(errorMessage));
        }

        // 6. Get trading rules from exchange
        var rulesResult = await _exchangePort.GetTradingRulesAsync(position.Pair, cancellationToken);
        if (rulesResult.IsFailure)
        {
            return Result<ExecuteDCAResult>.Failure(rulesResult.Error);
        }

        var rules = rulesResult.Value;
        if (!rules.IsTradingEnabled)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.Validation($"Trading is not enabled for {position.Pair}"));
        }

        // 7. Validate order against exchange rules
        if (command.Cost.Amount < rules.MinOrderValue)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.Validation($"Order value {command.Cost} is below minimum {rules.MinOrderValue}"));
        }

        // 8. Place buy order on exchange
        ExchangeOrderResult orderResult;
        if (command.UseLimitOrder && command.MaxPrice != null)
        {
            var quantity = CalculateQuantity(command.Cost, command.MaxPrice, rules);
            var limitOrderResult = await _exchangePort.PlaceLimitBuyAsync(
                position.Pair,
                quantity,
                command.MaxPrice,
                cancellationToken);

            if (limitOrderResult.IsFailure)
            {
                return Result<ExecuteDCAResult>.Failure(limitOrderResult.Error);
            }

            orderResult = limitOrderResult.Value;
        }
        else
        {
            var marketOrderResult = await _exchangePort.PlaceMarketBuyAsync(
                position.Pair,
                command.Cost,
                cancellationToken);

            if (marketOrderResult.IsFailure)
            {
                return Result<ExecuteDCAResult>.Failure(marketOrderResult.Error);
            }

            orderResult = marketOrderResult.Value;
        }

        var orderLifecycle = ExchangeOrderLifecycleFactory.Create(
            orderResult,
            intent: OrderIntent.ExecuteDca,
            relatedPositionId: position.Id);

        // 9. Check if order was filled
        if (!orderLifecycle.CanAffectPosition)
        {
            return await PersistPendingOrderFailureAsync(orderLifecycle, orderResult.Status, cancellationToken);
        }

        // 10. Add DCA entry to position
        position.AddDCAEntry(
            orderLifecycle.Id,
            orderLifecycle.AveragePrice,
            orderLifecycle.FilledQuantity,
            orderLifecycle.Fees);

        // 11. Update portfolio - record the additional cost
        portfolio.RecordPositionCostIncreased(
            position.Id,
            position.Pair,
            orderLifecycle.Cost);
        orderLifecycle.MarkCurrentFillApplied();
        var domainEvents = CollectDomainEvents(orderLifecycle, position, portfolio);

        // 12. Save changes
        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(orderLifecycle, cancellationToken);
            await _positionRepository.SaveAsync(position, cancellationToken);
            await _portfolioRepository.SaveAsync(portfolio, cancellationToken);
            await EnqueueDomainEventsAsync(domainEvents, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<ExecuteDCAResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<ExecuteDCAResult>.Failure(
                Error.ExchangeError($"Failed to save DCA entry: {ex.Message}"));
        }

        // 13. Dispatch domain events
        ClearDomainEvents(orderLifecycle, position, portfolio);
        await DispatchCommittedDomainEventsAsync(domainEvents, cancellationToken);

        // 14. Send notification
        await SendNotificationAsync(position, orderResult, cancellationToken);

        // 15. Return result
        return Result<ExecuteDCAResult>.Success(new ExecuteDCAResult
        {
            PositionId = position.Id,
            Pair = position.Pair,
            OrderId = orderLifecycle.Id,
            EntryPrice = orderLifecycle.AveragePrice,
            Quantity = orderLifecycle.FilledQuantity,
            Cost = orderLifecycle.Cost,
            Fees = orderLifecycle.Fees,
            NewDCALevel = position.DCALevel,
            NewAveragePrice = position.AveragePrice,
            NewTotalQuantity = position.TotalQuantity,
            NewTotalCost = position.TotalCost,
            ExecutedAt = DateTimeOffset.UtcNow
        });
    }

    private static Quantity CalculateQuantity(Money cost, Price price, TradingPairRules rules)
    {
        var rawQuantity = cost.Amount / price.Value;

        // Apply step size rounding
        if (rules.QuantityStepSize > 0)
        {
            rawQuantity = Math.Floor(rawQuantity / rules.QuantityStepSize) * rules.QuantityStepSize;
        }

        // Round to precision
        rawQuantity = Math.Round(rawQuantity, rules.QuantityPrecision);

        // Ensure minimum quantity
        if (rawQuantity < rules.MinQuantity)
        {
            rawQuantity = rules.MinQuantity;
        }

        return Quantity.Create(rawQuantity);
    }

    private async Task<Result<ExecuteDCAResult>> PersistPendingOrderFailureAsync(
        OrderLifecycle orderLifecycle,
        OrderStatus exchangeStatus,
        CancellationToken cancellationToken)
    {
        var domainEvents = CollectDomainEvents(orderLifecycle);

        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(orderLifecycle, cancellationToken);
            await EnqueueDomainEventsAsync(domainEvents, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<ExecuteDCAResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<ExecuteDCAResult>.Failure(
                Error.ExchangeError($"Failed to save order lifecycle: {ex.Message}"));
        }

        ClearDomainEvents(orderLifecycle);
        await DispatchCommittedDomainEventsAsync(domainEvents, cancellationToken);

        return Result<ExecuteDCAResult>.Failure(
            Error.ExchangeError($"DCA order was not filled. Status: {exchangeStatus}"));
    }

    private async Task BeginTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
    }

    private async Task EnqueueDomainEventsAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await _eventOutbox.EnqueueAsync(events, cancellationToken);
    }

    private async Task DispatchCommittedDomainEventsAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await _eventDispatcher.DispatchManyAsync(events, cancellationToken);

        foreach (var domainEvent in events)
        {
            await _eventOutbox.MarkProcessedAsync(domainEvent.EventId, cancellationToken);
        }
    }

    private static List<IDomainEvent> CollectDomainEvents(OrderLifecycle orderLifecycle)
    {
        return orderLifecycle.DomainEvents.ToList();
    }

    private static List<IDomainEvent> CollectDomainEvents(
        OrderLifecycle orderLifecycle,
        Position position,
        Portfolio portfolio)
    {
        var events = new List<IDomainEvent>();
        events.AddRange(orderLifecycle.DomainEvents);
        events.AddRange(position.DomainEvents);
        events.AddRange(portfolio.DomainEvents);
        return events;
    }

    private static void ClearDomainEvents(OrderLifecycle orderLifecycle)
    {
        orderLifecycle.ClearDomainEvents();
    }

    private static void ClearDomainEvents(
        OrderLifecycle orderLifecycle,
        Position position,
        Portfolio portfolio)
    {
        orderLifecycle.ClearDomainEvents();
        position.ClearDomainEvents();
        portfolio.ClearDomainEvents();
    }

    private async Task SendNotificationAsync(
        Position position,
        ExchangeOrderResult orderResult,
        CancellationToken cancellationToken)
    {
        if (_notificationPort is null)
            return;

        try
        {
            var notification = new Notification
            {
                Title = $"DCA Executed (Level {position.DCALevel})",
                Message = $"DCA for {position.Pair} position\n" +
                          $"Price: {orderResult.AveragePrice}\n" +
                          $"Quantity: {orderResult.FilledQuantity}\n" +
                          $"Cost: {orderResult.Cost}\n" +
                          $"New Avg Price: {position.AveragePrice}\n" +
                          $"Total Qty: {position.TotalQuantity}",
                Type = NotificationType.Trade,
                Priority = NotificationPriority.Normal
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
/// Handler for executing DCA entries by trading pair.
/// </summary>
public sealed class ExecuteDCAByPairHandler : ICommandHandler<ExecuteDCAByPairCommand, ExecuteDCAResult>
{
    private readonly IPositionRepository _positionRepository;
    private readonly ExecuteDCAHandler _executeDCAHandler;

    public ExecuteDCAByPairHandler(
        IPositionRepository positionRepository,
        ExecuteDCAHandler executeDCAHandler)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _executeDCAHandler = executeDCAHandler ?? throw new ArgumentNullException(nameof(executeDCAHandler));
    }

    public async Task<Result<ExecuteDCAResult>> HandleAsync(
        ExecuteDCAByPairCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Find position by pair
        var position = await _positionRepository.GetByPairAsync(command.Pair, cancellationToken);
        if (position is null)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.NotFound("Position", $"pair {command.Pair}"));
        }

        // Delegate to the main handler
        var dcaCommand = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = command.Cost,
            MaxPrice = command.MaxPrice,
            UseLimitOrder = command.UseLimitOrder
        };

        return await _executeDCAHandler.HandleAsync(dcaCommand, cancellationToken);
    }
}
