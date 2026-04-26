using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class PortfolioQueryHandlerTests
{
    private readonly Mock<IPortfolioReadModel> _portfolioReadModelMock = new();
    private readonly Mock<IPositionReadModel> _positionReadModelMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();

    [Fact]
    public async Task GetPortfolio_WithDefaultPortfolio_ReturnsMappedReadModelBalances()
    {
        var portfolio = CreatePortfolioEntry(
            total: 10000m,
            available: 9000m,
            reserved: 1000m,
            activePositions: 1,
            invested: 1000m);

        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var handler = new GetPortfolioHandler(_portfolioReadModelMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(portfolio.Id);
        result.Value.Name.Should().Be("Default");
        result.Value.TotalBalance.Amount.Should().Be(10000m);
        result.Value.AvailableBalance.Amount.Should().Be(9000m);
        result.Value.ReservedBalance.Amount.Should().Be(1000m);
        result.Value.ActivePositionCount.Should().Be(1);
        result.Value.CanOpenNewPosition.Should().BeTrue();
        result.Value.AvailablePercentage.Should().Be(90m);
        result.Value.ReservedPercentage.Should().Be(10m);
        result.Value.LastUpdatedAt.Should().Be(portfolio.LastUpdatedAt);
    }

    [Fact]
    public async Task GetPortfolio_WithMissingNamedPortfolio_ReturnsNotFound()
    {
        _portfolioReadModelMock
            .Setup(x => x.GetByNameAsync("Missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortfolioReadModelEntry?)null);

        var handler = new GetPortfolioHandler(_portfolioReadModelMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioQuery { Name = "Missing" });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
        result.Error.Message.Should().Contain("Missing");
    }

    [Fact]
    public async Task GetPortfolioStatistics_WithActivePositions_ReturnsCurrentPortfolioMetricsFromReadModels()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = CreatePortfolioEntry(
            total: 5000m,
            available: 4000m,
            reserved: 1000m,
            activePositions: 1,
            invested: 1000m);
        var position = CreatePositionEntry(pair, price: 50000m, quantity: 0.02m, fees: 1m);

        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionReadModelMock
            .Setup(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });
        _positionReadModelMock
            .Setup(x => x.GetClosedAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                null,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PositionReadModelEntry>());
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        var handler = new GetPortfolioStatisticsHandler(
            _portfolioReadModelMock.Object,
            _positionReadModelMock.Object,
            _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioStatisticsQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.PortfolioId.Should().Be(portfolio.Id);
        result.Value.TotalBalance.Amount.Should().Be(5000m);
        result.Value.AvailableBalance.Amount.Should().Be(4000m);
        result.Value.InvestedBalance.Amount.Should().Be(1000m);
        result.Value.ActivePositions.Should().Be(1);
        result.Value.PositionSlotsAvailable.Should().Be(4);
        result.Value.TotalUnrealizedPnL.Amount.Should().Be(99m);
        result.Value.OverallMargin.Percentage.Should().BeApproximately(9.8901098901m, 0.0000000001m);
        result.Value.TotalTradesCount.Should().Be(1);
        result.Value.WinningTradesCount.Should().Be(1);
        result.Value.LosingTradesCount.Should().Be(0);
        result.Value.WinRate.Should().Be(100m);
    }

    [Fact]
    public async Task GetPortfolioStatistics_WhenPortfolioMissing_ReturnsNotFound()
    {
        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((PortfolioReadModelEntry?)null);

        var handler = new GetPortfolioStatisticsHandler(
            _portfolioReadModelMock.Object,
            _positionReadModelMock.Object,
            _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioStatisticsQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetPortfolioStatistics_WhenPriceLookupFails_ReturnsFailure()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = CreatePortfolioEntry(
            total: 5000m,
            available: 4000m,
            reserved: 1000m,
            activePositions: 1,
            invested: 1000m);
        var position = CreatePositionEntry(pair, price: 50000m, quantity: 0.02m, fees: 1m);

        _portfolioReadModelMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionReadModelMock
            .Setup(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("price unavailable")));

        var handler = new GetPortfolioStatisticsHandler(
            _portfolioReadModelMock.Object,
            _positionReadModelMock.Object,
            _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioStatisticsQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("price unavailable");
    }

    private static PortfolioReadModelEntry CreatePortfolioEntry(
        decimal total,
        decimal available,
        decimal reserved,
        int activePositions,
        decimal invested)
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
            InvestedBalance = Money.Create(invested, "USDT"),
            CreatedAt = DateTimeOffset.Parse("2026-04-26T10:00:00Z"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-04-26T10:30:00Z"),
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
            DCALevel = 0,
            EntryCount = 1,
            OpenedAt = DateTimeOffset.Parse("2026-04-26T10:00:00Z")
        };
    }
}
