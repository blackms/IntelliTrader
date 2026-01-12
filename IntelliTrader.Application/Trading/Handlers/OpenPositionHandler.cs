using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.Trading.Aggregates;
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
    private readonly IExchangePort _exchangePort;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly INotificationPort? _notificationPort;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly IUnitOfWork _unitOfWork;

    public OpenPositionHandler(
        IPortfolioRepository portfolioRepository,
        IPositionRepository positionRepository,
        IExchangePort exchangePort,
        IDomainEventDispatcher eventDispatcher,
        TradingConstraintValidator constraintValidator,
        IUnitOfWork unitOfWork,
        INotificationPort? notificationPort = null)
    {
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
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

        // 7. Check if order was filled
        if (!orderResult.IsFullyFilled && !orderResult.IsPartiallyFilled)
        {
            return Result<OpenPositionResult>.Failure(
                Error.ExchangeError($"Order was not filled. Status: {orderResult.Status}"));
        }

        // 8. Create the Position aggregate
        var position = Position.Open(
            command.Pair,
            OrderId.From(orderResult.OrderId),
            orderResult.AveragePrice,
            orderResult.FilledQuantity,
            orderResult.Fees,
            command.SignalRule);

        // 9. Update the Portfolio aggregate
        portfolio.RecordPositionOpened(
            position.Id,
            command.Pair,
            orderResult.Cost);

        // 10. Save changes
        try
        {
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
        await DispatchDomainEventsAsync(position, portfolio, cancellationToken);

        // 12. Send notification (non-blocking)
        await SendNotificationAsync(position, orderResult, cancellationToken);

        // 13. Return result
        return Result<OpenPositionResult>.Success(new OpenPositionResult
        {
            PositionId = position.Id,
            Pair = command.Pair,
            OrderId = OrderId.From(orderResult.OrderId),
            EntryPrice = orderResult.AveragePrice,
            Quantity = orderResult.FilledQuantity,
            Cost = orderResult.Cost,
            Fees = orderResult.Fees,
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

    private async Task DispatchDomainEventsAsync(
        Position position,
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        var positionEvents = position.DomainEvents.ToList();
        var portfolioEvents = portfolio.DomainEvents.ToList();

        position.ClearDomainEvents();
        portfolio.ClearDomainEvents();

        var allEvents = positionEvents.Concat(portfolioEvents);
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
