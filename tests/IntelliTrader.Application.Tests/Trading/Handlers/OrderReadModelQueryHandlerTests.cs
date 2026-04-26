using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class OrderReadModelQueryHandlerTests
{
    private readonly Mock<IOrderReadModel> _orderReadModelMock = new();

    [Fact]
    public async Task GetOrderHandler_WithExistingReadModelOrder_ReturnsOrderView()
    {
        var orderView = CreateOrderView("order-read-1");
        var handler = new GetOrderHandler(_orderReadModelMock.Object);

        _orderReadModelMock
            .Setup(x => x.GetByIdAsync(orderView.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderView);

        var result = await handler.HandleAsync(new GetOrderQuery { OrderId = orderView.Id });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(orderView);
        _orderReadModelMock.Verify(
            x => x.GetByIdAsync(orderView.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRecentOrdersHandler_DelegatesFiltersToReadModel()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderView = CreateOrderView("order-read-2", pair, OrderLifecycleStatus.Submitted);
        var handler = new GetRecentOrdersHandler(_orderReadModelMock.Object);

        _orderReadModelMock
            .Setup(x => x.GetRecentAsync(
                pair,
                OrderLifecycleStatus.Submitted,
                DomainOrderSide.Buy,
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([orderView]);

        var result = await handler.HandleAsync(new GetRecentOrdersQuery
        {
            Pair = pair,
            Status = OrderLifecycleStatus.Submitted,
            Side = DomainOrderSide.Buy,
            Limit = 25
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(orderView);
        _orderReadModelMock.Verify(
            x => x.GetRecentAsync(
                pair,
                OrderLifecycleStatus.Submitted,
                DomainOrderSide.Buy,
                25,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static OrderView CreateOrderView(
        string orderId,
        TradingPair? pair = null,
        OrderLifecycleStatus status = OrderLifecycleStatus.Filled)
    {
        var tradingPair = pair ?? TradingPair.Create("BTCUSDT", "USDT");
        return new OrderView
        {
            Id = OrderId.From(orderId),
            Pair = tradingPair,
            Side = DomainOrderSide.Buy,
            Type = DomainOrderType.Market,
            Status = status,
            RequestedQuantity = Quantity.Create(0.02m),
            FilledQuantity = status == OrderLifecycleStatus.Submitted ? Quantity.Zero : Quantity.Create(0.02m),
            SubmittedPrice = Price.Create(50000m),
            AveragePrice = status == OrderLifecycleStatus.Submitted ? Price.Zero : Price.Create(50000m),
            Cost = status == OrderLifecycleStatus.Submitted ? Money.Zero("USDT") : Money.Create(1000m, "USDT"),
            Fees = status == OrderLifecycleStatus.Submitted ? Money.Zero("USDT") : Money.Create(1m, "USDT"),
            SignalRule = "MomentumBreakout",
            SubmittedAt = DateTimeOffset.UtcNow,
            CanAffectPosition = status is OrderLifecycleStatus.PartiallyFilled or OrderLifecycleStatus.Filled,
            IsTerminal = status is OrderLifecycleStatus.Filled or OrderLifecycleStatus.Canceled or OrderLifecycleStatus.Rejected
        };
    }
}
