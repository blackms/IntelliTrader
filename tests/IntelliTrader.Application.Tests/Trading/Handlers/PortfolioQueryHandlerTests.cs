using IntelliTrader.Application.Common;
using FluentAssertions;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class PortfolioQueryHandlerTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock = new();
    private readonly Mock<IPositionRepository> _positionRepositoryMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();

    [Fact]
    public async Task GetPortfolio_WithDefaultPortfolio_ReturnsMappedBalances()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var positionId = PositionId.Create();
        var portfolio = Portfolio.Create("Default", "USDT", 10000m, 5, 100m);
        portfolio.RecordPositionOpened(positionId, pair, Money.Create(1000m, "USDT"));
        portfolio.ClearDomainEvents();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var handler = new GetPortfolioHandler(_portfolioRepositoryMock.Object);

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
        result.Value.LastUpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetPortfolio_WithMissingNamedPortfolio_ReturnsNotFound()
    {
        _portfolioRepositoryMock
            .Setup(x => x.GetByNameAsync("Missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        var handler = new GetPortfolioHandler(_portfolioRepositoryMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioQuery { Name = "Missing" });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
        result.Error.Message.Should().Contain("Missing");
    }

    [Fact]
    public async Task GetPortfolioStatistics_WithActivePositions_ReturnsCurrentPortfolioMetrics()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = Portfolio.Create("Default", "USDT", 5000m, 5, 100m);
        var position = Position.Open(
            pair,
            OrderId.From("order-btc-1"),
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, "USDT"));
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(1000m, "USDT"));
        portfolio.ClearDomainEvents();
        position.ClearDomainEvents();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionRepositoryMock
            .Setup(x => x.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });
        _positionRepositoryMock
            .Setup(x => x.GetClosedPositionsAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Position>());
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        var handler = new GetPortfolioStatisticsHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
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
        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        var handler = new GetPortfolioStatisticsHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioStatisticsQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetPortfolioStatistics_WhenPriceLookupFails_ReturnsFailure()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = Portfolio.Create("Default", "USDT", 5000m, 5, 100m);
        var position = Position.Open(
            pair,
            OrderId.From("order-btc-1"),
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, "USDT"));
        position.ClearDomainEvents();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _positionRepositoryMock
            .Setup(x => x.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("price unavailable")));

        var handler = new GetPortfolioStatisticsHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPortfolioStatisticsQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("price unavailable");
    }
}
