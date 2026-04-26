using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public class OrderQueryHandlerTests
{
    private readonly Mock<IOrderReadModel> _orderReadModelMock = new();

    [Fact]
    public async Task GetOrderHandler_WithMissingOrder_ReturnsNotFound()
    {
        var orderId = OrderId.From("missing-order");
        var handler = new GetOrderHandler(_orderReadModelMock.Object);

        _orderReadModelMock
            .Setup(x => x.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderView?)null);

        var result = await handler.HandleAsync(new GetOrderQuery { OrderId = orderId });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
        result.Error.Message.Should().Contain(orderId.Value);
    }

    [Fact]
    public async Task GetActiveOrdersHandler_DelegatesFiltersToReadModel()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var activeOrder = CreateOrderView("active-order-1", pair, OrderLifecycleStatus.PartiallyFilled);
        var handler = new GetActiveOrdersHandler(_orderReadModelMock.Object);

        _orderReadModelMock
            .Setup(x => x.GetActiveAsync(
                pair,
                DomainOrderSide.Buy,
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([activeOrder]);

        var result = await handler.HandleAsync(new GetActiveOrdersQuery
        {
            Pair = pair,
            Side = DomainOrderSide.Buy,
            Limit = 10
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(activeOrder);
    }

    [Fact]
    public async Task GetTradingHistoryHandler_DelegatesRangeAndPaginationToReadModel()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;
        var history = new TradeHistoryEntry
        {
            PositionId = PositionId.Create(),
            Pair = pair,
            Type = TradeType.Buy,
            Price = Price.Create(50000m),
            Quantity = Quantity.Create(0.02m),
            Cost = Money.Create(1000m, "USDT"),
            Fees = Money.Create(1m, "USDT"),
            Timestamp = DateTimeOffset.UtcNow,
            OrderId = "history-order-1",
            Note = "OpenPosition"
        };
        var handler = new GetTradingHistoryHandler(_orderReadModelMock.Object);

        _orderReadModelMock
            .Setup(x => x.GetTradingHistoryAsync(
                pair,
                from,
                to,
                5,
                20,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([history]);

        var result = await handler.HandleAsync(new GetTradingHistoryQuery
        {
            Pair = pair,
            From = from,
            To = to,
            Offset = 5,
            Limit = 20
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(history);
    }

    [Fact]
    public async Task GetTradingHistoryHandler_WhenRangeIsInvalid_ReturnsValidationWithoutReadingModel()
    {
        var handler = new GetTradingHistoryHandler(_orderReadModelMock.Object);

        var result = await handler.HandleAsync(new GetTradingHistoryQuery
        {
            From = DateTimeOffset.UtcNow,
            To = DateTimeOffset.UtcNow.AddDays(-1)
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        _orderReadModelMock.Verify(
            x => x.GetTradingHistoryAsync(
                It.IsAny<TradingPair?>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static OrderView CreateOrderView(
        string orderId,
        TradingPair pair,
        OrderLifecycleStatus status)
    {
        return new OrderView
        {
            Id = OrderId.From(orderId),
            Pair = pair,
            Side = DomainOrderSide.Buy,
            Type = DomainOrderType.Market,
            Status = status,
            RequestedQuantity = Quantity.Create(0.02m),
            FilledQuantity = Quantity.Create(0.01m),
            SubmittedPrice = Price.Create(50000m),
            AveragePrice = Price.Create(50000m),
            Cost = Money.Create(500m, "USDT"),
            Fees = Money.Create(0.5m, "USDT"),
            SignalRule = "MomentumBreakout",
            SubmittedAt = DateTimeOffset.UtcNow,
            CanAffectPosition = true,
            IsTerminal = false
        };
    }
}
