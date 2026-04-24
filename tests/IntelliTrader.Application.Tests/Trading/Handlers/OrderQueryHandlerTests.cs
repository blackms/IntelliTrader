using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public class OrderQueryHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();

    [Fact]
    public async Task HandleAsync_WithExistingOrder_ReturnsMappedOrderView()
    {
        // Arrange
        var order = CreateFilledOrder("order-1");
        var handler = new GetOrderHandler(_orderRepositoryMock.Object);

        _orderRepositoryMock
            .Setup(x => x.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var query = new GetOrderQuery { OrderId = order.Id };

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(order.Id);
        result.Value.Pair.Should().Be(order.Pair);
        result.Value.Status.Should().Be(OrderLifecycleStatus.Filled);
        result.Value.CanAffectPosition.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithFilters_ReturnsRecentMatchingOrdersOnly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var matching = CreateSubmittedOrder("order-2", pair, DateTimeOffset.UtcNow.AddMinutes(-1));
        var nonMatchingStatus = CreateFilledOrder("order-3", pair, DateTimeOffset.UtcNow.AddMinutes(-2));
        var nonMatchingPair = CreateSubmittedOrder(
            "order-4",
            TradingPair.Create("ETHUSDT", "USDT"),
            DateTimeOffset.UtcNow);

        var handler = new GetRecentOrdersHandler(_orderRepositoryMock.Object);

        _orderRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { nonMatchingStatus, matching, nonMatchingPair });

        var query = new GetRecentOrdersQuery
        {
            Pair = pair,
            Status = OrderLifecycleStatus.Submitted,
            Side = DomainOrderSide.Buy,
            Limit = 10
        };

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().Be(matching.Id);
        result.Value[0].Status.Should().Be(OrderLifecycleStatus.Submitted);
    }

    [Fact]
    public async Task HandleAsync_WithActiveOrdersQuery_ReturnsOnlyNonTerminalOrders()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var newestPartial = CreatePartiallyFilledOrder("order-5", pair, DateTimeOffset.UtcNow);
        var olderSubmitted = CreateSubmittedOrder("order-6", pair, DateTimeOffset.UtcNow.AddMinutes(-1));
        var filled = CreateFilledOrder("order-7", pair, DateTimeOffset.UtcNow.AddMinutes(-2));
        var otherPair = CreateSubmittedOrder(
            "order-8",
            TradingPair.Create("ETHUSDT", "USDT"),
            DateTimeOffset.UtcNow.AddMinutes(-3));

        var handler = new GetActiveOrdersHandler(_orderRepositoryMock.Object);

        _orderRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderSubmitted, otherPair, filled, newestPartial });

        var query = new GetActiveOrdersQuery
        {
            Pair = pair,
            Side = DomainOrderSide.Buy,
            Limit = 10
        };

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Select(order => order.Id).Should().Equal(newestPartial.Id, olderSubmitted.Id);
        result.Value.Should().OnlyContain(order => !order.IsTerminal);
    }

    [Fact]
    public async Task HandleAsync_WithTradingHistoryQuery_ReturnsFilledOrdersMappedAsTrades()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var positionId = PositionId.Create();
        var initialBuy = CreateFilledOrder(
            "order-buy-1",
            pair,
            DateTimeOffset.UtcNow.AddMinutes(-30),
            OrderIntent.OpenPosition,
            positionId);
        var dcaBuy = CreateFilledOrder(
            "order-dca-1",
            pair,
            DateTimeOffset.UtcNow.AddMinutes(-20),
            OrderIntent.ExecuteDca,
            positionId);
        var sell = CreateFilledOrder(
            "order-sell-1",
            pair,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            OrderIntent.ClosePosition,
            positionId,
            DomainOrderSide.Sell);
        var pending = CreateSubmittedOrder(
            "order-pending-1",
            pair,
            DateTimeOffset.UtcNow.AddMinutes(-5));

        var handler = new GetTradingHistoryHandler(_orderRepositoryMock.Object);

        _orderRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pending, initialBuy, sell, dcaBuy });

        // Act
        var result = await handler.HandleAsync(new GetTradingHistoryQuery
        {
            Pair = pair,
            Limit = 10
        });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Select(entry => entry.OrderId).Should().Equal("order-sell-1", "order-dca-1", "order-buy-1");
        result.Value.Select(entry => entry.Type).Should().Equal(TradeType.Sell, TradeType.DCA, TradeType.Buy);
        result.Value.Should().OnlyContain(entry => entry.PositionId == positionId);
        result.Value.Should().OnlyContain(entry => entry.Pair == pair);
    }

    [Fact]
    public async Task HandleAsync_WithTradingHistoryQuery_SkipsFilledOrdersWithoutPositionLink()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var unlinkedFilledOrder = CreateFilledOrder(
            "order-unlinked-1",
            pair,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            OrderIntent.OpenPosition,
            relatedPositionId: null);

        var handler = new GetTradingHistoryHandler(_orderRepositoryMock.Object);

        _orderRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { unlinkedFilledOrder });

        // Act
        var result = await handler.HandleAsync(new GetTradingHistoryQuery { Pair = pair });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private static OrderLifecycle CreateSubmittedOrder(
        string orderId,
        TradingPair? pair = null,
        DateTimeOffset? submittedAt = null)
    {
        var order = OrderLifecycle.Submit(
            OrderId.From(orderId),
            pair ?? TradingPair.Create("BTCUSDT", "USDT"),
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(0.02m),
            Price.Create(50000m),
            "MomentumBreakout",
            submittedAt ?? DateTimeOffset.UtcNow);

        order.ClearDomainEvents();
        return order;
    }

    private static OrderLifecycle CreateFilledOrder(
        string orderId,
        TradingPair? pair = null,
        DateTimeOffset? submittedAt = null)
    {
        return CreateFilledOrder(
            orderId,
            pair,
            submittedAt,
            OrderIntent.OpenPosition,
            PositionId.Create());
    }

    private static OrderLifecycle CreateFilledOrder(
        string orderId,
        TradingPair? pair,
        DateTimeOffset? submittedAt,
        OrderIntent intent,
        PositionId? relatedPositionId,
        DomainOrderSide side = DomainOrderSide.Buy)
    {
        var tradingPair = pair ?? TradingPair.Create("BTCUSDT", "USDT");
        var submittedRelatedPositionId = intent == OrderIntent.OpenPosition ? null : relatedPositionId;
        var order = OrderLifecycle.Submit(
            OrderId.From(orderId),
            tradingPair,
            side,
            DomainOrderType.Market,
            Quantity.Create(0.02m),
            Price.Create(50000m),
            "MomentumBreakout",
            submittedAt ?? DateTimeOffset.UtcNow,
            intent,
            submittedRelatedPositionId);

        order.ClearDomainEvents();
        order.MarkFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        if (intent == OrderIntent.OpenPosition && relatedPositionId is not null)
        {
            order.LinkRelatedPosition(relatedPositionId);
        }

        order.ClearDomainEvents();
        return order;
    }

    private static OrderLifecycle CreatePartiallyFilledOrder(
        string orderId,
        TradingPair? pair = null,
        DateTimeOffset? submittedAt = null)
    {
        var order = CreateSubmittedOrder(orderId, pair, submittedAt);
        order.MarkPartiallyFilled(
            Quantity.Create(0.01m),
            Price.Create(50000m),
            Money.Create(500m, "USDT"),
            Money.Create(0.5m, "USDT"));
        order.ClearDomainEvents();
        return order;
    }
}
