using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Reconciles a persisted order lifecycle against the exchange and applies
/// the corresponding domain-side effects when a submitted order eventually fills.
/// </summary>
public sealed class RefreshOrderStatusHandler : ICommandHandler<RefreshOrderStatusCommand, RefreshOrderStatusResult>
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IExchangePort _exchangePort;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly IDomainEventOutbox _eventOutbox;
    private readonly ITransactionalUnitOfWork _unitOfWork;

    public RefreshOrderStatusHandler(
        IPortfolioRepository portfolioRepository,
        IPositionRepository positionRepository,
        IOrderRepository orderRepository,
        IExchangePort exchangePort,
        IDomainEventDispatcher eventDispatcher,
        IDomainEventOutbox eventOutbox,
        ITransactionalUnitOfWork unitOfWork)
    {
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        _eventOutbox = eventOutbox ?? throw new ArgumentNullException(nameof(eventOutbox));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Result<RefreshOrderStatusResult>> HandleAsync(
        RefreshOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(
                Error.NotFound("Order", command.OrderId.Value));
        }

        if (order.Intent == OrderIntent.Unknown)
        {
            return Result<RefreshOrderStatusResult>.Failure(
                Error.Validation($"Order {order.Id.Value} does not contain refreshable intent metadata."));
        }

        if (order.IsTerminal && !order.HasUnappliedFill)
        {
            return Result<RefreshOrderStatusResult>.Success(CreateResult(
                order,
                order.Status,
                appliedDomainEffects: false,
                positionId: order.RelatedPositionId));
        }

        var exchangeResult = await _exchangePort.GetOrderAsync(order.Pair, order.Id.Value, cancellationToken);
        if (exchangeResult.IsFailure)
        {
            return Result<RefreshOrderStatusResult>.Failure(exchangeResult.Error);
        }

        var previousStatus = order.Status;
        bool changed;
        try
        {
            changed = ExchangeOrderLifecycleFactory.Refresh(order, exchangeResult.Value);
        }
        catch (ArgumentException ex)
        {
            return Result<RefreshOrderStatusResult>.Failure(Error.Validation(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result<RefreshOrderStatusResult>.Failure(Error.Validation(ex.Message));
        }

        if (!changed && !order.HasUnappliedFill)
        {
            return Result<RefreshOrderStatusResult>.Success(CreateResult(
                order,
                previousStatus,
                appliedDomainEffects: false,
                positionId: order.RelatedPositionId));
        }

        if (!order.CanAffectPosition)
        {
            return await PersistOrderOnlyAsync(order, previousStatus, cancellationToken);
        }

        var occurredAt = exchangeResult.Value.UpdatedAt ?? exchangeResult.Value.CreatedAt;

        return order.Intent switch
        {
            OrderIntent.OpenPosition => await ApplyOpenPositionAsync(order, previousStatus, occurredAt, cancellationToken),
            OrderIntent.ClosePosition => await ApplyClosePositionAsync(order, previousStatus, occurredAt, cancellationToken),
            OrderIntent.ExecuteDca => await ApplyDcaAsync(order, previousStatus, occurredAt, cancellationToken),
            _ => Result<RefreshOrderStatusResult>.Failure(
                Error.Validation($"Unsupported order intent {order.Intent} for order {order.Id.Value}."))
        };
    }

    private async Task<Result<RefreshOrderStatusResult>> ApplyOpenPositionAsync(
        OrderLifecycle order,
        OrderLifecycleStatus previousStatus,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (!order.HasUnappliedFill)
        {
            return await PersistOrderOnlyAsync(order, previousStatus, cancellationToken);
        }

        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(Error.NotFound("Portfolio", "default"));
        }

        Position position;
        var relatedPositionId = order.RelatedPositionId ?? portfolio.GetPositionId(order.Pair);
        if (relatedPositionId is null)
        {
            position = Position.Open(
                order.Pair,
                order.Id,
                order.AveragePrice,
                order.FilledQuantity,
                order.Fees,
                order.SignalRule,
                occurredAt);

            portfolio.RecordPositionOpened(position.Id, order.Pair, order.UnappliedCost);
            order.LinkRelatedPosition(position.Id);
        }
        else
        {
            var existingPosition = await _positionRepository.GetByIdAsync(relatedPositionId, cancellationToken);
            if (existingPosition is null)
            {
                return Result<RefreshOrderStatusResult>.Failure(
                    Error.NotFound("Position", relatedPositionId.Value.ToString()));
            }

            position = existingPosition;
            if (!position.Entries.Any(entry => entry.OrderId == order.Id))
            {
                return Result<RefreshOrderStatusResult>.Failure(
                    Error.Validation($"Position {position.Id.Value} does not contain opening order {order.Id.Value}."));
            }

            if (order.RelatedPositionId is null)
            {
                order.LinkRelatedPosition(position.Id);
            }

            position.ApplyOpeningFillDelta(
                order.Id,
                order.AveragePrice,
                order.FilledQuantity,
                order.Fees,
                occurredAt);

            portfolio.RecordPositionCostIncreased(position.Id, position.Pair, order.UnappliedCost);
        }

        order.MarkCurrentFillApplied();

        return await PersistOrderPositionPortfolioAsync(
            order,
            previousStatus,
            position,
            portfolio,
            cancellationToken);
    }

    private async Task<Result<RefreshOrderStatusResult>> ApplyClosePositionAsync(
        OrderLifecycle order,
        OrderLifecycleStatus previousStatus,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (!order.HasUnappliedFill)
        {
            return await PersistOrderOnlyAsync(order, previousStatus, cancellationToken);
        }

        var positionId = order.RelatedPositionId;
        if (positionId is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(
                Error.Validation($"Order {order.Id.Value} is missing related position metadata."));
        }

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(
                Error.NotFound("Position", positionId.Value.ToString()));
        }

        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(Error.NotFound("Portfolio", "default"));
        }

        var closeDelta = position.ApplyCloseFillDelta(
            order.Id,
            CalculateUnappliedAveragePrice(order),
            order.UnappliedQuantity,
            order.UnappliedFees,
            occurredAt);

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

        order.MarkCurrentFillApplied();

        return await PersistOrderPositionPortfolioAsync(
            order,
            previousStatus,
            position,
            portfolio,
            cancellationToken);
    }

    private async Task<Result<RefreshOrderStatusResult>> ApplyDcaAsync(
        OrderLifecycle order,
        OrderLifecycleStatus previousStatus,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var positionId = order.RelatedPositionId;
        if (positionId is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(
                Error.Validation($"Order {order.Id.Value} is missing related position metadata."));
        }

        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(
                Error.NotFound("Position", positionId.Value.ToString()));
        }

        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<RefreshOrderStatusResult>.Failure(Error.NotFound("Portfolio", "default"));
        }

        if (!order.HasUnappliedFill)
        {
            return await PersistOrderOnlyAsync(order, previousStatus, cancellationToken);
        }

        if (position.Entries.Any(entry => entry.OrderId == order.Id))
        {
            position.ApplyDCAFillDelta(
                order.Id,
                order.AveragePrice,
                order.FilledQuantity,
                order.Fees,
                occurredAt);
        }
        else
        {
            position.AddDCAEntry(
                order.Id,
                order.AveragePrice,
                order.UnappliedQuantity,
                order.UnappliedFees,
                occurredAt);
        }

        portfolio.RecordPositionCostIncreased(position.Id, position.Pair, order.UnappliedCost);
        order.MarkCurrentFillApplied();

        return await PersistOrderPositionPortfolioAsync(
            order,
            previousStatus,
            position,
            portfolio,
            cancellationToken);
    }

    private async Task<Result<RefreshOrderStatusResult>> PersistOrderOnlyAsync(
        OrderLifecycle order,
        OrderLifecycleStatus previousStatus,
        CancellationToken cancellationToken)
    {
        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(order, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<RefreshOrderStatusResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<RefreshOrderStatusResult>.Failure(
                Error.ExchangeError($"Failed to persist refreshed order: {ex.Message}"));
        }

        await DispatchDomainEventsAsync(order, cancellationToken);

        return Result<RefreshOrderStatusResult>.Success(CreateResult(
            order,
            previousStatus,
            appliedDomainEffects: false,
            positionId: order.RelatedPositionId));
    }

    private async Task<Result<RefreshOrderStatusResult>> PersistOrderPositionPortfolioAsync(
        OrderLifecycle order,
        OrderLifecycleStatus previousStatus,
        Position position,
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        try
        {
            await BeginTransactionIfSupportedAsync(cancellationToken);
            await _orderRepository.SaveAsync(order, cancellationToken);
            await _positionRepository.SaveAsync(position, cancellationToken);
            await _portfolioRepository.SaveAsync(portfolio, cancellationToken);

            var commitResult = await _unitOfWork.CommitAsync(cancellationToken);
            if (commitResult.IsFailure)
            {
                return Result<RefreshOrderStatusResult>.Failure(commitResult.Error);
            }
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            return Result<RefreshOrderStatusResult>.Failure(
                Error.ExchangeError($"Failed to persist refreshed order effects: {ex.Message}"));
        }

        await DispatchDomainEventsAsync(order, position, portfolio, cancellationToken);

        return Result<RefreshOrderStatusResult>.Success(CreateResult(
            order,
            previousStatus,
            appliedDomainEffects: true,
            positionId: position.Id));
    }

    private async Task BeginTransactionIfSupportedAsync(CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
    }

    private async Task DispatchDomainEventsAsync(
        OrderLifecycle order,
        CancellationToken cancellationToken)
    {
        var events = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        if (events.Count == 0)
        {
            return;
        }

        await _eventDispatcher.DispatchManyAsync(events, cancellationToken);
    }

    private async Task DispatchDomainEventsAsync(
        OrderLifecycle order,
        Position position,
        Portfolio portfolio,
        CancellationToken cancellationToken)
    {
        var events = new List<IDomainEvent>();
        events.AddRange(order.DomainEvents);
        events.AddRange(position.DomainEvents);
        events.AddRange(portfolio.DomainEvents);

        order.ClearDomainEvents();
        position.ClearDomainEvents();
        portfolio.ClearDomainEvents();

        await _eventDispatcher.DispatchManyAsync(events, cancellationToken);
    }

    private static RefreshOrderStatusResult CreateResult(
        OrderLifecycle order,
        OrderLifecycleStatus previousStatus,
        bool appliedDomainEffects,
        PositionId? positionId)
    {
        return new RefreshOrderStatusResult
        {
            OrderId = order.Id,
            PreviousStatus = previousStatus,
            CurrentStatus = order.Status,
            AppliedDomainEffects = appliedDomainEffects,
            PositionId = positionId
        };
    }

    private static Price CalculateUnappliedAveragePrice(OrderLifecycle order)
    {
        return Price.Create(order.UnappliedCost.Amount / order.UnappliedQuantity.Value);
    }
}
