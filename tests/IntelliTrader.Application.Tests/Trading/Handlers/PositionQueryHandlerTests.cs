using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class PositionQueryHandlerTests
{
    private readonly Mock<IPositionReadModel> _positionReadModelMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();

    [Fact]
    public async Task GetPosition_WithPair_ReturnsMappedViewUsingCurrentPrice()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var openedAt = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var position = CreatePosition(pair, 50000m, 0.02m, 1m, openedAt: openedAt);

        _positionReadModelMock
            .Setup(x => x.GetByPairAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        var handler = new GetPositionHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPositionQuery { Pair = pair });

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(position.Id);
        result.Value.Pair.Should().Be(pair);
        result.Value.CurrentPrice.Value.Should().Be(55000m);
        result.Value.CurrentValue.Amount.Should().Be(1100m);
        result.Value.UnrealizedPnL.Amount.Should().Be(99m);
        result.Value.CurrentMargin.Percentage.Should().BeApproximately(9.8901098901m, 0.0000000001m);
        result.Value.EntryCount.Should().Be(1);
        result.Value.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task GetPosition_WithoutIdentifier_ReturnsValidationFailure()
    {
        var handler = new GetPositionHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPositionQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("PositionId or Pair");
    }

    [Fact]
    public async Task GetPosition_WithMissingPositionId_ReturnsNotFound()
    {
        var positionId = PositionId.Create();
        _positionReadModelMock
            .Setup(x => x.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionReadModelEntry?)null);

        var handler = new GetPositionHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPositionQuery { PositionId = positionId });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
        result.Error.Message.Should().Contain(positionId.ToString());
    }

    [Fact]
    public async Task GetPosition_WithClosedPosition_UsesAveragePriceWithoutExchangeLookup()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateClosedPosition(pair, 50000m, 0.02m, 1m, DateTimeOffset.UtcNow);

        _positionReadModelMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var handler = new GetPositionHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPositionQuery { PositionId = position.Id });

        result.IsSuccess.Should().BeTrue();
        result.Value.IsClosed.Should().BeTrue();
        result.Value.CurrentPrice.Should().Be(position.AveragePrice);
        _exchangePortMock.Verify(
            x => x.GetCurrentPriceAsync(It.IsAny<TradingPair>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetPosition_WhenActivePositionPriceLookupFails_ReturnsFailure()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, 50000m, 0.02m, 1m);

        _positionReadModelMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("price unavailable")));

        var handler = new GetPositionHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetPositionQuery { PositionId = position.Id });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task GetActivePositions_FiltersByMarketAndSortsByMarginDescending()
    {
        var btc = TradingPair.Create("BTCUSDT", "USDT");
        var eth = TradingPair.Create("ETHUSDT", "USDT");
        var btcPosition = CreatePosition(btc, 50000m, 0.02m, 1m);
        var ethPosition = CreatePosition(eth, 2500m, 0.4m, 1m);

        _positionReadModelMock
            .Setup(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ethPosition, btcPosition });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(btc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(eth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(2400m)));

        var handler = new GetActivePositionsHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetActivePositionsQuery
        {
            Market = "USDT",
            SortBy = PositionSortOrder.Margin,
            Descending = true
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(position => position.Pair).Should().Equal(btc, eth);
        result.Value.Should().OnlyContain(position => position.Pair.QuoteCurrency == "USDT");
        result.Value[0].CurrentMargin.Percentage.Should().BeGreaterThan(result.Value[1].CurrentMargin.Percentage);
        _positionReadModelMock.Verify(x => x.GetActiveAsync("USDT", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActivePositions_WhenPriceLookupFails_ReturnsFailure()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, 50000m, 0.02m, 1m);

        _positionReadModelMock
            .Setup(x => x.GetActiveAsync((string?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("price unavailable")));

        var handler = new GetActivePositionsHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetActivePositionsQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
        result.Error.Message.Should().Contain("price unavailable");
    }

    [Theory]
    [InlineData(PositionSortOrder.Pair, false, "ADAUSDT", "BTCUSDT")]
    [InlineData(PositionSortOrder.Pair, true, "BTCUSDT", "ADAUSDT")]
    [InlineData(PositionSortOrder.Cost, true, "BTCUSDT", "ADAUSDT")]
    [InlineData(PositionSortOrder.Cost, false, "ADAUSDT", "BTCUSDT")]
    [InlineData(PositionSortOrder.DCALevel, true, "BTCUSDT", "ADAUSDT")]
    [InlineData(PositionSortOrder.DCALevel, false, "ADAUSDT", "BTCUSDT")]
    [InlineData(PositionSortOrder.OpenedAt, false, "ADAUSDT", "BTCUSDT")]
    [InlineData(PositionSortOrder.OpenedAt, true, "BTCUSDT", "ADAUSDT")]
    public async Task GetActivePositions_AppliesSortOrder(
        PositionSortOrder sortOrder,
        bool descending,
        string firstSymbol,
        string secondSymbol)
    {
        var btc = TradingPair.Create("BTCUSDT", "USDT");
        var ada = TradingPair.Create("ADAUSDT", "USDT");
        var newerBtc = CreatePosition(
            btc,
            price: 49666.666666666666666666666667m,
            quantity: 0.03m,
            fees: 1.5m,
            totalCost: 1490m,
            dcaLevel: 1,
            entryCount: 2,
            openedAt: DateTimeOffset.UtcNow);
        var olderAda = CreatePosition(ada, 1m, 100m, 1m, openedAt: DateTimeOffset.UtcNow.AddHours(-1));

        _positionReadModelMock
            .Setup(x => x.GetActiveAsync((string?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { newerBtc, olderAda });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(btc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(51000m)));
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(ada, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(1.01m)));

        var handler = new GetActivePositionsHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetActivePositionsQuery
        {
            SortBy = sortOrder,
            Descending = descending
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(position => position.Pair.Symbol).Should().Equal(firstSymbol, secondSymbol);
    }

    [Fact]
    public async Task GetActivePositions_AppliesMarginRangeFilters()
    {
        var btc = TradingPair.Create("BTCUSDT", "USDT");
        var eth = TradingPair.Create("ETHUSDT", "USDT");
        var btcPosition = CreatePosition(btc, 50000m, 0.02m, 1m);
        var ethPosition = CreatePosition(eth, 2500m, 0.4m, 1m);

        _positionReadModelMock
            .Setup(x => x.GetActiveAsync((string?)null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { btcPosition, ethPosition });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(btc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(eth, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(2400m)));

        var handler = new GetActivePositionsHandler(_positionReadModelMock.Object, _exchangePortMock.Object);

        var result = await handler.HandleAsync(new GetActivePositionsQuery
        {
            MinMargin = 0m,
            MaxMargin = 20m
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Pair.Should().Be(btc);
    }

    [Fact]
    public async Task GetClosedPositions_FiltersByPairAndLimitsResults()
    {
        var btc = TradingPair.Create("BTCUSDT", "USDT");
        var newerBtc = CreateClosedPosition(btc, 50000m, 0.02m, 1m, DateTimeOffset.UtcNow);
        var olderBtc = CreateClosedPosition(btc, 51000m, 0.02m, 1m, DateTimeOffset.UtcNow.AddMinutes(-10));

        _positionReadModelMock
            .Setup(x => x.GetClosedAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                btc,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderBtc, newerBtc });

        var handler = new GetClosedPositionsHandler(_positionReadModelMock.Object);

        var result = await handler.HandleAsync(new GetClosedPositionsQuery
        {
            Pair = btc,
            Limit = 1
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().Be(newerBtc.Id);
        result.Value[0].IsClosed.Should().BeTrue();
        result.Value[0].CurrentPrice.Should().Be(newerBtc.AveragePrice);

        _exchangePortMock.Verify(
            x => x.GetCurrentPriceAsync(It.IsAny<TradingPair>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetClosedPositions_WithProfitableOnlyFilter_ReturnsValidationFailure()
    {
        var handler = new GetClosedPositionsHandler(_positionReadModelMock.Object);

        var result = await handler.HandleAsync(new GetClosedPositionsQuery { ProfitableOnly = true });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("closed-position PnL projection");
    }

    [Fact]
    public async Task GetClosedPositions_WithInvalidRange_ReturnsValidationFailure()
    {
        var handler = new GetClosedPositionsHandler(_positionReadModelMock.Object);

        var result = await handler.HandleAsync(new GetClosedPositionsQuery
        {
            From = DateTimeOffset.UtcNow,
            To = DateTimeOffset.UtcNow.AddDays(-1)
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
    }

    private static PositionReadModelEntry CreatePosition(
        TradingPair pair,
        decimal price,
        decimal quantity,
        decimal fees,
        decimal? totalCost = null,
        int dcaLevel = 0,
        int entryCount = 1,
        DateTimeOffset? openedAt = null)
    {
        var effectiveCost = totalCost ?? price * quantity;
        var averagePrice = quantity == 0m ? price : effectiveCost / quantity;

        return new PositionReadModelEntry
        {
            Id = PositionId.Create(),
            Pair = pair,
            AveragePrice = Price.Create(averagePrice),
            TotalQuantity = Quantity.Create(quantity),
            TotalCost = Money.Create(effectiveCost, pair.QuoteCurrency),
            TotalFees = Money.Create(fees, pair.QuoteCurrency),
            DCALevel = dcaLevel,
            EntryCount = entryCount,
            OpenedAt = openedAt ?? DateTimeOffset.UtcNow,
            SignalRule = "MomentumBreakout"
        };
    }

    private static PositionReadModelEntry CreateClosedPosition(
        TradingPair pair,
        decimal entryPrice,
        decimal quantity,
        decimal entryFees,
        DateTimeOffset closedAt)
    {
        return CreatePosition(
            pair,
            entryPrice,
            quantity,
            entryFees,
            openedAt: closedAt.AddHours(-4)) with
        {
            IsClosed = true,
            ClosedAt = closedAt
        };
    }
}
