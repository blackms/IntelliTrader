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
using StatusOrderSide = IntelliTrader.Application.Trading.Queries.OrderSide;
using StatusOrderStatus = IntelliTrader.Application.Trading.Queries.OrderStatus;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class PortfolioStatusQueryHandlerTests
{
    private readonly Mock<IPortfolioReadModel> _portfolioReadModelMock = new();
    private readonly Mock<IPositionReadModel> _positionReadModelMock = new();
    private readonly Mock<IOrderReadModel> _orderReadModelMock = new();
    private readonly Mock<ITradingStateReadModel> _tradingStateReadModelMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();

    [Fact]
    public async Task HandleAsync_WhenReadModelsHaveState_ReturnsAggregatedPortfolioStatus()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = CreatePortfolioEntry(
            total: 10000m,
            available: 8000m,
            reserved: 2000m,
            activePositions: 1);
        var position = CreatePositionEntry(pair, price: 50000m, quantity: 0.04m, fees: 2m);
        var order = CreateOrderView(pair);
        var activeBuy = CreateOrderView(
            TradingPair.Create("ETHUSDT", "USDT"),
            DomainOrderSide.Buy,
            OrderLifecycleStatus.Submitted,
            "active-buy-1");
        var activeSell = CreateOrderView(
            pair,
            DomainOrderSide.Sell,
            OrderLifecycleStatus.Submitted,
            "active-sell-1");

        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionReadModelMock
            .Setup(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });
        _orderReadModelMock
            .Setup(x => x.GetRecentAsync(
                null,
                null,
                null,
                25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { order });
        _orderReadModelMock
            .Setup(x => x.GetActiveAsync(
                null,
                null,
                int.MaxValue,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { activeBuy, activeSell });
        _tradingStateReadModelMock
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradingStateReadModelEntry
            {
                IsTradingSuspended = true,
                SuspensionReason = SuspensionReason.Manual,
                IsForcedSuspension = true,
                LastChangedAt = DateTimeOffset.Parse("2026-04-26T10:30:00Z")
            });
        _exchangePortMock
            .Setup(x => x.GetCurrentPricesAsync(
                It.Is<IEnumerable<TradingPair>>(pairs => pairs.Single() == pair),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<TradingPair, Price>>.Success(
                new Dictionary<TradingPair, Price>
                {
                    [pair] = Price.Create(55000m)
                }));

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetPortfolioStatusQuery
        {
            IncludeOrderHistory = true,
            OrderHistoryLimit = 25
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Summary.TotalBalance.Should().Be(10000m);
        result.Value.Summary.AvailableBalance.Should().Be(8000m);
        result.Value.Summary.ReservedBalance.Should().Be(2000m);
        result.Value.Summary.CurrentValue.Should().Be(2200m);
        result.Value.Summary.UnrealizedPnL.Should().Be(198m);
        result.Value.Summary.UnrealizedPnLPercent.Should().BeApproximately(9.8901098901m, 0.0000000001m);
        result.Value.Summary.ActivePositionCount.Should().Be(1);
        result.Value.Summary.TrailingBuyCount.Should().Be(1);
        result.Value.Summary.TrailingSellCount.Should().Be(1);
        result.Value.Summary.IsTradingSuspended.Should().BeTrue();
        result.Value.Summary.Market.Should().Be("USDT");
        result.Value.Positions.Should().ContainSingle()
            .Which.Should().Match<PositionInfo>(info =>
                info.Pair == "BTCUSDT" &&
                info.Amount == 0.04m &&
                info.TotalCost == 2000m &&
                info.CurrentValue == 2200m &&
                info.UnrealizedPnL == 198m &&
                info.DCALevel == 1 &&
                info.HasTrailingSell);
        result.Value.TrailingBuys.Should().BeEquivalentTo("ETHUSDT");
        result.Value.TrailingSells.Should().BeEquivalentTo("BTCUSDT");
        result.Value.OrderHistory.Should().ContainSingle()
            .Which.Should().Match<OrderHistoryInfo>(info =>
                info.OrderId == order.Id.Value &&
                info.Pair == "BTCUSDT" &&
                info.Side == StatusOrderSide.Buy &&
                info.Status == StatusOrderStatus.Filled &&
                info.AmountFilled == 0.04m);
    }

    [Fact]
    public async Task HandleAsync_WhenOptionalSectionsAreDisabled_DoesNotLoadThem()
    {
        var portfolio = CreatePortfolioEntry(
            total: 10000m,
            available: 10000m,
            reserved: 0m,
            activePositions: 0);

        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionReadModelMock
            .Setup(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PositionReadModelEntry>());
        _tradingStateReadModelMock
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TradingStateReadModelEntry.Running);

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetPortfolioStatusQuery
        {
            IncludePositions = false,
            IncludeTrailingOrders = false,
            IncludeOrderHistory = false
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Positions.Should().BeNull();
        result.Value.TrailingBuys.Should().BeNull();
        result.Value.TrailingSells.Should().BeNull();
        result.Value.OrderHistory.Should().BeNull();
        _orderReadModelMock.Verify(
            x => x.GetRecentAsync(
                It.IsAny<TradingPair?>(),
                It.IsAny<OrderLifecycleStatus?>(),
                It.IsAny<DomainOrderSide?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenPortfolioIsMissing_ReturnsNotFound()
    {
        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortfolioReadModelEntry?)null);

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetPortfolioStatusQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task HandleAsync_WhenPriceLookupFails_ReturnsFailure()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = CreatePortfolioEntry(
            total: 10000m,
            available: 8000m,
            reserved: 2000m,
            activePositions: 1);

        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionReadModelMock
            .Setup(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreatePositionEntry(pair, price: 50000m, quantity: 0.04m, fees: 2m) });
        _tradingStateReadModelMock
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(TradingStateReadModelEntry.Running);
        _exchangePortMock
            .Setup(x => x.GetCurrentPricesAsync(It.IsAny<IEnumerable<TradingPair>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<TradingPair, Price>>.Failure(
                Error.ExchangeError("price unavailable")));

        var handler = CreateHandler();

        var result = await handler.HandleAsync(new GetPortfolioStatusQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("price unavailable");
    }

    private GetPortfolioStatusHandler CreateHandler()
    {
        return new GetPortfolioStatusHandler(
            _portfolioReadModelMock.Object,
            _positionReadModelMock.Object,
            _orderReadModelMock.Object,
            _tradingStateReadModelMock.Object,
            _exchangePortMock.Object);
    }

    private static PortfolioReadModelEntry CreatePortfolioEntry(
        decimal total,
        decimal available,
        decimal reserved,
        int activePositions)
    {
        return new PortfolioReadModelEntry
        {
            Id = PortfolioId.Create(),
            Name = "Default",
            Market = "USDT",
            TotalBalance = Money.Create(total, "USDT"),
            AvailableBalance = Money.Create(available, "USDT"),
            ReservedBalance = Money.Create(reserved, "USDT"),
            ActivePositionCount = activePositions,
            MaxPositions = 5,
            MinPositionCost = Money.Create(100m, "USDT"),
            InvestedBalance = Money.Create(reserved, "USDT"),
            CreatedAt = DateTimeOffset.Parse("2026-04-26T10:00:00Z"),
            IsDefault = true
        };
    }

    private static PositionReadModelEntry CreatePositionEntry(
        TradingPair pair,
        decimal price,
        decimal quantity,
        decimal fees)
    {
        return new PositionReadModelEntry
        {
            Id = PositionId.Create(),
            Pair = pair,
            AveragePrice = Price.Create(price),
            TotalQuantity = Quantity.Create(quantity),
            TotalCost = Money.Create(price * quantity, pair.QuoteCurrency),
            TotalFees = Money.Create(fees, pair.QuoteCurrency),
            DCALevel = 1,
            EntryCount = 2,
            OpenedAt = DateTimeOffset.Parse("2026-04-26T09:00:00Z"),
            SignalRule = "RSI"
        };
    }

    private static OrderView CreateOrderView(
        TradingPair pair,
        DomainOrderSide side = DomainOrderSide.Buy,
        OrderLifecycleStatus status = OrderLifecycleStatus.Filled,
        string orderId = "order-1")
    {
        return new OrderView
        {
            Id = OrderId.From(orderId),
            Pair = pair,
            Side = side,
            Type = DomainOrderType.Market,
            Status = status,
            RequestedQuantity = Quantity.Create(0.04m),
            FilledQuantity = Quantity.Create(0.04m),
            SubmittedPrice = Price.Create(50000m),
            AveragePrice = Price.Create(50000m),
            Cost = Money.Create(2000m, pair.QuoteCurrency),
            Fees = Money.Create(2m, pair.QuoteCurrency),
            SubmittedAt = DateTimeOffset.Parse("2026-04-26T09:01:00Z"),
            CanAffectPosition = true,
            IsTerminal = true
        };
    }
}
