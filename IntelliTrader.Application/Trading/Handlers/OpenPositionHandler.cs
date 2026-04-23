using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Handler for opening new trading positions.
/// </summary>
public sealed class OpenPositionHandler : ICommandHandler<OpenPositionCommand, OpenPositionResult>
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IExchangePort _exchangePort;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly INotificationPort? _notificationPort;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ITransactionalUnitOfWork _unitOfWork;

    public OpenPositionHandler(
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

    public async Task<Result<OpenPositionResult>> HandleAsync(
        OpenPositionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Get the portfolio
        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<OpenPositionResult>.Failure(
                Error.NotFound("Portfolio", "default"));
        }

        // 2. Validate constraints
        var validation = _constraintValidator.ValidateOpenPosition(
            portfolio,
            command.Pair,
            command.Cost);

        if (!validation.IsValid)
        {
            var errorMessage = string.Join("; ", validation.Errors.Select(e => e.Message));
            return Result<OpenPositionResult>.Failure(
                Error.Validation(errorMessage));
        }

        // 3. Get trading rules from exchange
        var rulesResult = await _exchangePort.GetTradingRulesAsync(command.Pair, cancellationToken);
        if (rulesResult.IsFailure)
        {
            return Result<OpenPositionResult>.Failure(rulesResult.Error);
        }

        var rules = rulesResult.Value;
        if (!rules.IsTradingEnabled)
        {
            return Result<OpenPositionResult>.Failure(
                Error.Validation($"Trading is not enabled for {command.Pair}"));
        }

        // 4. Validate order against exchange rules
        var orderValidation = ValidateOrderAgainstRules(command.Cost, rules);
        if (orderValidation.IsFailure)
        {
            return Result<OpenPositionResult>.Failure(orderValidation.Error);
        }

        // 5. Get current price (for limit order validation or reference)
        var priceResult = await _exchangePort.GetCurrentPriceAsync(command.Pair, cancellationToken);
        if (priceResult.IsFailure)
        {
            return Result<OpenPositionResult>.Failure(priceResult.Error);
        }

        var currentPrice = priceResult.Value;

        // 6. Place the order on the exchange
        ExchangeOrderResult orderResult;
        if (command.UseLimitOrder && command.MaxPrice != null)
        {
            // Calculate quantity for limit order
            var quantity = CalculateQuantity(command.Cost, command.MaxPrice, rules);
            var limitOrderResult = await _exchangePort.PlaceLimitBuyAsync(
                command.Pair,
                quantity,
                command.MaxPrice,
                cancellationToken);

            if (limitOrderResult.IsFailure)
            {
                return Result<OpenPositionResult>.Failure(limitOrderResult.Error);
            }

            orderResult = limitOrderResult.Value;
        }
        else
        {
            // Place market order
            var marketOrderResult = await _exchangePort.PlaceMarketBuyAsync(
                command.Pair,
                command.Cost,
                cancellationToken);

            if (marketOrderResult.IsFailure)
            {
                return Result<OpenPositionResult>.Failure(marketOrderResult.Error);
            }

            orderResult = marketOrderResult.Value;
        }

        var orderLifecycle = ExchangeOrderLifecycleFactory.Create(
            orderResult,
            command.SignalRule,
            OrderIntent.OpenPosition);

        // 7. Check if order was filled enough to open a position
        if (!orderLifecycle.CanAffectPosition)
        {
            return await PersistPendingOrderFailureAsync(orderLifecycle, orderResult.Status, cancellationToken);
        }

        // 8. Create the Position aggregate
        var position = Position.Open(
            command.Pair,
            orderLifecycle.Id,
            orderLifecycle.AveragePrice,
            orderLifecycle.FilledQuantity,
            orderLifecycle.Fees,
            command.SignalRule);

        // 9. Update the Portfolio aggregate
        portfolio.RecordPositionOpened(
            position.Id,
            command.Pair,
            orderLifecycle.Cost);
        orderLifecycle.LinkRelatedPosition(position.Id);
        orderLifecycle.MarkCurrentFillApplied();

        // 10. Save changes
        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(orderLifecycle, cancellationToken);
            await _positionRepository.SaveAsync(position, cancellationToken);
            await _portfolioRepository.SaveAsync(portfolio, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<OpenPositionResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<OpenPositionResult>.Failure(
                Error.ExchangeError($"Failed to save position: {ex.Message}"));
        }

        // 11. Dispatch domain events
        await DispatchDomainEventsAsync(orderLifecycle, position, portfolio, cancellationToken);

        // 12. Send notification (non-blocking)
        await SendNotificationAsync(position, orderResult, cancellationToken);

        // 13. Return result
        return Result<OpenPositionResult>.Success(new OpenPositionResult
        {
            PositionId = position.Id,
            Pair = command.Pair,
            OrderId = orderLifecycle.Id,
            EntryPrice = orderLifecycle.AveragePrice,
            Quantity = orderLifecycle.FilledQuantity,
            Cost = orderLifecycle.Cost,
            Fees = orderLifecycle.Fees,
            OpenedAt = position.OpenedAt
        });
    }

    private static Result ValidateOrderAgainstRules(Money cost, TradingPairRules rules)
    {
        if (cost.Amount < rules.MinOrderValue)
        {
            return Result.Failure(
                Error.Validation($"Order value {cost} is below minimum {rules.MinOrderValue}"));
        }

        return Result.Success();
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

    private async Task<Result<OpenPositionResult>> PersistPendingOrderFailureAsync(
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
                return Result<OpenPositionResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<OpenPositionResult>.Failure(
                Error.ExchangeError($"Failed to save order lifecycle: {ex.Message}"));
        }

        await DispatchOrderEventsAsync(orderLifecycle, cancellationToken);

        return Result<OpenPositionResult>.Failure(
            Error.ExchangeError($"Order was not filled. Status: {exchangeStatus}"));
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
        CancellationToken cancellationToken)
    {
        if (_notificationPort is null)
            return;

        try
        {
            var notification = new Notification
            {
                Title = "Position Opened",
                Message = $"Opened {position.Pair} position\n" +
                          $"Price: {orderResult.AveragePrice}\n" +
                          $"Quantity: {orderResult.FilledQuantity}\n" +
                          $"Cost: {orderResult.Cost}\n" +
                          $"Fees: {orderResult.Fees}",
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
