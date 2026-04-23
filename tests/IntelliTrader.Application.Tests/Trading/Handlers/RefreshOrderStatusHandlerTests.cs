using FluentAssertions;
using Moq;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;
using ExchangeOrderSide = IntelliTrader.Application.Ports.Driven.OrderSide;
using ExchangeOrderStatus = IntelliTrader.Application.Ports.Driven.OrderStatus;
using ExchangeOrderType = IntelliTrader.Application.Ports.Driven.OrderType;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class RefreshOrderStatusHandlerTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock = new();
    private readonly Mock<IPositionRepository> _positionRepositoryMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly Mock<ITransactionalUnitOfWork> _unitOfWorkMock = new();
    private readonly RefreshOrderStatusHandler _handler;

    public RefreshOrderStatusHandlerTests()
    {
        _handler = new RefreshOrderStatusHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _orderRepositoryMock.Object,
            _exchangePortMock.Object,
            _eventDispatcherMock.Object,
            _unitOfWorkMock.Object);

        _orderRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<OrderLifecycle>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _positionRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _portfolioRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<Portfolio>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventDispatcherMock
            .Setup(x => x.DispatchManyAsync(It.IsAny<IEnumerable<IDomainEvent>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .SetupGet(x => x.HasActiveTransaction)
            .Returns(false);

        _unitOfWorkMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _unitOfWorkMock
            .Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_WhenSubmittedOpenOrderBecomesFilled_OpensPositionAndUpdatesPortfolio()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var submittedOrder = CreateSubmittedOrder(
            orderId: "refresh-open-1",
            pair: pair,
            side: DomainOrderSide.Buy,
            intent: OrderIntent.OpenPosition,
            signalRule: "MomentumBreakout");

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(submittedOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submittedOrder);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, submittedOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: submittedOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Buy,
                status: ExchangeOrderStatus.Filled,
                price: 50000m,
                quantity: 0.02m,
                fees: 1m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = submittedOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(submittedOrder.Id);
        result.Value.PreviousStatus.Should().Be(OrderLifecycleStatus.Submitted);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.AppliedDomainEffects.Should().BeTrue();
        result.Value.PositionId.Should().NotBeNull();

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == submittedOrder.Id &&
                    order.Status == OrderLifecycleStatus.Filled &&
                    order.Intent == OrderIntent.OpenPosition),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(position =>
                    position.Pair == pair &&
                    position.SignalRule == "MomentumBreakout" &&
                    !position.IsClosed),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio => savedPortfolio.HasPositionFor(pair)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _eventDispatcherMock.Verify(
            x => x.DispatchManyAsync(
                It.Is<IEnumerable<IDomainEvent>>(events =>
                    events.OfType<OrderFilledEvent>().Any() &&
                    events.OfType<PositionOpened>().Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSubmittedCloseOrderBecomesFilled_ClosesPositionAndReleasesPortfolio()
    {
        // Arrange
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("seed-buy-close-1"),
            Price.Create(2500m),
            Quantity.Create(0.4m),
            Money.Create(1m, "USDT"),
            "ExitRule");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);
        var submittedOrder = CreateSubmittedOrder(
            orderId: "refresh-close-1",
            pair: pair,
            side: DomainOrderSide.Sell,
            intent: OrderIntent.ClosePosition,
            relatedPositionId: position.Id,
            requestedQuantity: position.TotalQuantity.Value);

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(submittedOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submittedOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, submittedOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: submittedOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Sell,
                status: ExchangeOrderStatus.Filled,
                price: 2600m,
                quantity: position.TotalQuantity.Value,
                fees: 1m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = submittedOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.AppliedDomainEffects.Should().BeTrue();

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition => savedPosition.Id == position.Id && savedPosition.IsClosed),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio => !savedPortfolio.HasPositionFor(pair)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _eventDispatcherMock.Verify(
            x => x.DispatchManyAsync(
                It.Is<IEnumerable<IDomainEvent>>(events =>
                    events.OfType<OrderFilledEvent>().Any() &&
                    events.OfType<PositionClosed>().Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSubmittedCloseOrderBecomesPartiallyFilled_PersistsLifecycleWithoutClosingPosition()
    {
        // Arrange
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("seed-buy-close-partial-1"),
            Price.Create(2500m),
            Quantity.Create(0.4m),
            Money.Create(1m, "USDT"),
            "ExitRule");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);
        var submittedOrder = CreateSubmittedOrder(
            orderId: "refresh-close-partial-1",
            pair: pair,
            side: DomainOrderSide.Sell,
            intent: OrderIntent.ClosePosition,
            relatedPositionId: position.Id,
            requestedQuantity: position.TotalQuantity.Value);

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(submittedOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submittedOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, submittedOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: submittedOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Sell,
                status: ExchangeOrderStatus.PartiallyFilled,
                price: 2600m,
                quantity: 0.2m,
                fees: 0.5m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = submittedOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        result.Value.AppliedDomainEffects.Should().BeFalse();
        position.IsClosed.Should().BeFalse();
        portfolio.HasPositionFor(pair).Should().BeTrue();

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == submittedOrder.Id &&
                    order.Status == OrderLifecycleStatus.PartiallyFilled &&
                    order.AppliedQuantity.IsZero),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(It.IsAny<Portfolio>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenPartialCloseOrderBecomesFilled_ClosesPositionAndMarksFillApplied()
    {
        // Arrange
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("seed-buy-close-partial-2"),
            Price.Create(2500m),
            Quantity.Create(0.4m),
            Money.Create(1m, "USDT"),
            "ExitRule");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);
        var partialOrder = OrderLifecycle.Submit(
            OrderId.From("refresh-close-partial-2"),
            pair,
            DomainOrderSide.Sell,
            DomainOrderType.Market,
            Quantity.Create(0.4m),
            Price.Create(2600m),
            intent: OrderIntent.ClosePosition,
            relatedPositionId: position.Id);
        partialOrder.MarkPartiallyFilled(
            Quantity.Create(0.2m),
            Price.Create(2600m),
            Money.Create(520m, "USDT"),
            Money.Create(0.5m, "USDT"));
        partialOrder.ClearDomainEvents();

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(partialOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, partialOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: partialOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Sell,
                status: ExchangeOrderStatus.Filled,
                price: 2600m,
                quantity: 0.4m,
                fees: 1m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = partialOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.AppliedDomainEffects.Should().BeTrue();

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition => savedPosition.Id == position.Id && savedPosition.IsClosed),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio => !savedPortfolio.HasPositionFor(pair)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == partialOrder.Id &&
                    order.AppliedQuantity.Value == 0.4m &&
                    order.AppliedCost.Amount == 1040m &&
                    order.AppliedFees.Amount == 1m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenSubmittedDcaOrderBecomesFilled_AddsDcaEntryAndIncreasesPortfolioCost()
    {
        // Arrange
        var pair = TradingPair.Create("SOLUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("seed-buy-dca-1"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(1m, "USDT"),
            "DipBuy");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);
        var submittedOrder = CreateSubmittedOrder(
            orderId: "refresh-dca-1",
            pair: pair,
            side: DomainOrderSide.Buy,
            intent: OrderIntent.ExecuteDca,
            relatedPositionId: position.Id,
            requestedQuantity: 5m);

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(submittedOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submittedOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, submittedOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: submittedOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Buy,
                status: ExchangeOrderStatus.Filled,
                price: 90m,
                quantity: 5m,
                fees: 0.5m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = submittedOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.AppliedDomainEffects.Should().BeTrue();

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition =>
                    savedPosition.Id == position.Id &&
                    savedPosition.DCALevel == 1 &&
                    savedPosition.TotalQuantity.Value == 15m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio => savedPortfolio.GetTotalInvestedCost().Amount == 1450m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _eventDispatcherMock.Verify(
            x => x.DispatchManyAsync(
                It.Is<IEnumerable<IDomainEvent>>(events =>
                    events.OfType<OrderFilledEvent>().Any() &&
                    events.OfType<DCAExecuted>().Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAppliedPartialDcaOrderBecomesFilled_AppliesOnlyFillDelta()
    {
        // Arrange
        var pair = TradingPair.Create("SOLUSDT", "USDT");
        var orderId = OrderId.From("refresh-dca-partial-1");
        var position = Position.Open(
            pair,
            OrderId.From("seed-buy-dca-partial-1"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(1m, "USDT"),
            "DipBuy");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);

        position.AddDCAEntry(
            orderId,
            Price.Create(90m),
            Quantity.Create(5m),
            Money.Create(0.5m, "USDT"));

        portfolio.RecordPositionCostIncreased(position.Id, pair, Money.Create(450m, "USDT"));
        portfolio.ClearDomainEvents();

        var partialOrder = OrderLifecycle.Submit(
            orderId,
            pair,
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(10m),
            Price.Create(90m),
            intent: OrderIntent.ExecuteDca,
            relatedPositionId: position.Id);
        partialOrder.MarkPartiallyFilled(
            Quantity.Create(5m),
            Price.Create(90m),
            Money.Create(450m, "USDT"),
            Money.Create(0.5m, "USDT"));
        partialOrder.MarkCurrentFillApplied();
        partialOrder.ClearDomainEvents();

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(partialOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, partialOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: partialOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Buy,
                status: ExchangeOrderStatus.Filled,
                price: 90m,
                quantity: 10m,
                fees: 1m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = partialOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.AppliedDomainEffects.Should().BeTrue();

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition =>
                    savedPosition.Id == position.Id &&
                    savedPosition.DCALevel == 1 &&
                    savedPosition.TotalQuantity.Value == 20m &&
                    savedPosition.GetEntryAtLevel(1)!.Quantity.Value == 10m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio =>
                    savedPortfolio.GetTotalInvestedCost().Amount == 1900m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == partialOrder.Id &&
                    order.Status == OrderLifecycleStatus.Filled &&
                    order.AppliedQuantity.Value == 10m &&
                    order.AppliedCost.Amount == 900m &&
                    order.AppliedFees.Amount == 1m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAppliedPartialOpenOrderBecomesFilled_AppliesOnlyFillDeltaToExistingPosition()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderId = OrderId.From("refresh-open-partial-1");
        var position = Position.Open(
            pair,
            orderId,
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, "USDT"),
            "MomentumBreakout");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);

        var partialOrder = OrderLifecycle.Submit(
            orderId,
            pair,
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(0.05m),
            Price.Create(50000m),
            signalRule: "MomentumBreakout",
            intent: OrderIntent.OpenPosition);
        partialOrder.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        partialOrder.LinkRelatedPosition(position.Id);
        partialOrder.MarkCurrentFillApplied();
        partialOrder.ClearDomainEvents();

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(partialOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, partialOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: partialOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Buy,
                status: ExchangeOrderStatus.Filled,
                price: 50000m,
                quantity: 0.05m,
                fees: 2.5m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = partialOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.AppliedDomainEffects.Should().BeTrue();

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition =>
                    savedPosition.Id == position.Id &&
                    savedPosition.DCALevel == 0 &&
                    savedPosition.Entries.Count == 1 &&
                    savedPosition.TotalQuantity.Value == 0.05m &&
                    savedPosition.TotalCost.Amount == 2500m &&
                    savedPosition.TotalFees.Amount == 2.5m &&
                    savedPosition.GetEntryAtLevel(0)!.OrderId == partialOrder.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio =>
                    savedPortfolio.GetPositionId(pair) == position.Id &&
                    savedPortfolio.GetTotalInvestedCost().Amount == 2500m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == partialOrder.Id &&
                    order.Status == OrderLifecycleStatus.Filled &&
                    order.RelatedPositionId == position.Id &&
                    order.AppliedQuantity.Value == 0.05m &&
                    order.AppliedCost.Amount == 2500m &&
                    order.AppliedFees.Amount == 2.5m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenAppliedPartialOpenOrderReceivesLargerPartialFill_AppliesOnlyFillDelta()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderId = OrderId.From("refresh-open-partial-growth-1");
        var position = Position.Open(
            pair,
            orderId,
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, "USDT"),
            "MomentumBreakout");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);

        var partialOrder = OrderLifecycle.Submit(
            orderId,
            pair,
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(0.05m),
            Price.Create(50000m),
            signalRule: "MomentumBreakout",
            intent: OrderIntent.OpenPosition);
        partialOrder.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        partialOrder.LinkRelatedPosition(position.Id);
        partialOrder.MarkCurrentFillApplied();
        partialOrder.ClearDomainEvents();

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(partialOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, partialOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: partialOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Buy,
                status: ExchangeOrderStatus.PartiallyFilled,
                price: 50000m,
                quantity: 0.03m,
                fees: 1.5m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = partialOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        result.Value.AppliedDomainEffects.Should().BeTrue();

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition =>
                    savedPosition.Id == position.Id &&
                    savedPosition.DCALevel == 0 &&
                    savedPosition.TotalQuantity.Value == 0.03m &&
                    savedPosition.TotalCost.Amount == 1500m &&
                    savedPosition.TotalFees.Amount == 1.5m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Portfolio>(savedPortfolio =>
                    savedPortfolio.GetPositionId(pair) == position.Id &&
                    savedPortfolio.GetTotalInvestedCost().Amount == 1500m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == partialOrder.Id &&
                    order.Status == OrderLifecycleStatus.PartiallyFilled &&
                    order.AppliedQuantity.Value == 0.03m &&
                    order.AppliedCost.Amount == 1500m &&
                    order.AppliedFees.Amount == 1.5m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WhenLegacyAppliedPartialOpenOrderHasNoRelatedPosition_UsesActivePortfolioPosition()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderId = OrderId.From("refresh-open-legacy-partial-1");
        var position = Position.Open(
            pair,
            orderId,
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, "USDT"),
            "MomentumBreakout");
        var portfolio = CreatePortfolioWithOpenPosition(position, 10000m);

        var partialOrder = OrderLifecycle.Submit(
            orderId,
            pair,
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(0.05m),
            Price.Create(50000m),
            signalRule: "MomentumBreakout",
            intent: OrderIntent.OpenPosition);
        partialOrder.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        partialOrder.MarkCurrentFillApplied();
        partialOrder.ClearDomainEvents();

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(partialOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialOrder);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, partialOrder.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateExchangeOrderInfo(
                orderId: partialOrder.Id.Value,
                pair: pair,
                side: ExchangeOrderSide.Buy,
                status: ExchangeOrderStatus.Filled,
                price: 50000m,
                quantity: 0.05m,
                fees: 2.5m)));

        // Act
        var result = await _handler.HandleAsync(new RefreshOrderStatusCommand
        {
            OrderId = partialOrder.Id
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);

        _orderRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<OrderLifecycle>(order =>
                    order.Id == partialOrder.Id &&
                    order.RelatedPositionId == position.Id &&
                    order.AppliedQuantity.Value == 0.05m),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _positionRepositoryMock.Verify(
            x => x.SaveAsync(
                It.Is<Position>(savedPosition =>
                    savedPosition.Id == position.Id &&
                    savedPosition.TotalQuantity.Value == 0.05m &&
                    savedPosition.GetEntryAtLevel(0)!.OrderId == partialOrder.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Portfolio CreateTestPortfolio(
        decimal balance = 10000m,
        int maxPositions = 5,
        decimal minPositionCost = 10m)
    {
        return Portfolio.Create("Default", "USDT", balance, maxPositions, minPositionCost);
    }

    private static Portfolio CreatePortfolioWithOpenPosition(Position position, decimal balance)
    {
        var portfolio = CreateTestPortfolio(balance);
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);
        portfolio.ClearDomainEvents();
        return portfolio;
    }

    private static OrderLifecycle CreateSubmittedOrder(
        string orderId,
        TradingPair pair,
        DomainOrderSide side,
        OrderIntent intent,
        string? signalRule = null,
        PositionId? relatedPositionId = null,
        decimal requestedQuantity = 0.02m)
    {
        var order = OrderLifecycle.Submit(
            OrderId.From(orderId),
            pair,
            side,
            DomainOrderType.Market,
            Quantity.Create(requestedQuantity),
            Price.Create(50000m),
            signalRule: signalRule,
            timestamp: DateTimeOffset.UtcNow,
            intent: intent,
            relatedPositionId: relatedPositionId);

        order.ClearDomainEvents();
        return order;
    }

    private static ExchangeOrderInfo CreateExchangeOrderInfo(
        string orderId,
        TradingPair pair,
        ExchangeOrderSide side,
        ExchangeOrderStatus status,
        decimal price,
        decimal quantity,
        decimal fees)
    {
        return new ExchangeOrderInfo
        {
            OrderId = orderId,
            Pair = pair,
            Side = side,
            Type = ExchangeOrderType.Market,
            Status = status,
            OriginalQuantity = Quantity.Create(quantity),
            FilledQuantity = Quantity.Create(quantity),
            Price = Price.Create(price),
            AveragePrice = Price.Create(price),
            Fees = Money.Create(fees, "USDT"),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
