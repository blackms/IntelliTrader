using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class PositionReadModelQueryHandlerTests
{
    private readonly Mock<IPositionReadModel> _positionReadModelMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();

    [Fact]
    public async Task GetPosition_WithPair_ReturnsProjectedViewUsingCurrentExchangePrice()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateProjection(pair, averagePrice: 50000m, quantity: 0.02m, cost: 1000m, fees: 1m);

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
        result.Value.CurrentPrice.Value.Should().Be(55000m);
        result.Value.CurrentValue.Amount.Should().Be(1100m);
        result.Value.UnrealizedPnL.Amount.Should().Be(99m);
        result.Value.CurrentMargin.Percentage.Should().BeApproximately(9.8901098901m, 0.0000000001m);
        result.Value.EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task GetClosedPositions_ReadsClosedProjectionWithoutExchangeLookup()
    {
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var closedAt = DateTimeOffset.Parse("2026-04-26T12:00:00Z");
        var position = CreateProjection(pair, averagePrice: 1000m, quantity: 0.5m, cost: 500m, fees: 2m) with
        {
            IsClosed = true,
            ClosedAt = closedAt
        };

        _positionReadModelMock
            .Setup(x => x.GetClosedAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                pair,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });

        var handler = new GetClosedPositionsHandler(_positionReadModelMock.Object);

        var result = await handler.HandleAsync(new GetClosedPositionsQuery
        {
            Pair = pair,
            Limit = 1
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().Be(position.Id);
        result.Value[0].IsClosed.Should().BeTrue();
        result.Value[0].CurrentPrice.Should().Be(position.AveragePrice);
        _exchangePortMock.Verify(
            x => x.GetCurrentPriceAsync(It.IsAny<TradingPair>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PositionReadModelEntry CreateProjection(
        TradingPair pair,
        decimal averagePrice,
        decimal quantity,
        decimal cost,
        decimal fees)
    {
        return new PositionReadModelEntry
        {
            Id = PositionId.Create(),
            Pair = pair,
            AveragePrice = Price.Create(averagePrice),
            TotalQuantity = Quantity.Create(quantity),
            TotalCost = Money.Create(cost, pair.QuoteCurrency),
            TotalFees = Money.Create(fees, pair.QuoteCurrency),
            DCALevel = 0,
            EntryCount = 1,
            OpenedAt = DateTimeOffset.Parse("2026-04-26T10:00:00Z"),
            SignalRule = "MomentumBreakout"
        };
    }
}
