using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.Trading.Aggregates;
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
    private readonly IExchangePort _exchangePort;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly INotificationPort? _notificationPort;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly IUnitOfWork _unitOfWork;

    public ExecuteDCAHandler(
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

        // 9. Check if order was filled
        if (!orderResult.IsFullyFilled && !orderResult.IsPartiallyFilled)
        {
            return Result<ExecuteDCAResult>.Failure(
                Error.ExchangeError($"DCA order was not filled. Status: {orderResult.Status}"));
        }

        // 10. Add DCA entry to position
        position.AddDCAEntry(
            OrderId.From(orderResult.OrderId),
            orderResult.AveragePrice,
            orderResult.FilledQuantity,
            orderResult.Fees);

        // 11. Update portfolio - record the additional cost
        portfolio.RecordPositionCostIncreased(
            position.Id,
            position.Pair,
            orderResult.Cost);

        // 12. Save changes
        try
        {
            await _positionRepository.SaveAsync(position, cancellationToken);
            await _portfolioRepository.SaveAsync(portfolio, cancellationToken);

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
        await DispatchDomainEventsAsync(position, portfolio, cancellationToken);

        // 14. Send notification
        await SendNotificationAsync(position, orderResult, cancellationToken);

        // 15. Return result
        return Result<ExecuteDCAResult>.Success(new ExecuteDCAResult
        {
            PositionId = position.Id,
            Pair = position.Pair,
            OrderId = OrderId.From(orderResult.OrderId),
            EntryPrice = orderResult.AveragePrice,
            Quantity = orderResult.FilledQuantity,
            Cost = orderResult.Cost,
            Fees = orderResult.Fees,
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
