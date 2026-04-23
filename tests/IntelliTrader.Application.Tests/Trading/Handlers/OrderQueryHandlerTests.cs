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
        var order = CreateSubmittedOrder(orderId, pair, submittedAt);
        order.MarkFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        order.ClearDomainEvents();
        return order;
    }
}
