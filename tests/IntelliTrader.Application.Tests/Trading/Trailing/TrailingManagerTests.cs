using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Trailing;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;
using Xunit;

namespace IntelliTrader.Application.Tests.Trading.Trailing;

public class TrailingManagerTests
{
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly TrailingManager _sut;

    public TrailingManagerTests()
    {
        _exchangePortMock = new Mock<IExchangePort>();
        _sut = new TrailingManager(_exchangePortMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullExchangePort_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TrailingManager(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("exchangePort");
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Act
        var manager = new TrailingManager(_exchangePortMock.Object);

        // Assert
        manager.Should().NotBeNull();
        manager.BuyTrailingCount.Should().Be(0);
        manager.SellTrailingCount.Should().Be(0);
    }

    #endregion

    #region InitiateSellTrailing Tests

    [Fact]
    public void InitiateSellTrailing_WithValidParameters_AddsSellTrailing()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var margin = 2.5m;
        var targetMargin = 1.5m;
        var config = new TrailingConfig
        {
            TrailingPercentage = 0.5m,
            StopMargin = -5m
        };

        // Act
        var result = _sut.InitiateSellTrailing(positionId, pair, price, margin, targetMargin, config);

        // Assert
        result.Should().BeTrue();
        _sut.HasSellTrailing(pair).Should().BeTrue();
        _sut.SellTrailingCount.Should().Be(1);
        _sut.ActiveSellTrailings.Should().HaveCount(1);
    }

    [Fact]
    public void InitiateSellTrailing_WhenAlreadyExists_ReturnsFalse()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.5m, 1.5m, config);

        // Act
        var result = _sut.InitiateSellTrailing(positionId, pair, price, 3.0m, 1.5m, config);

        // Assert
        result.Should().BeFalse();
        _sut.SellTrailingCount.Should().Be(1);
    }

    [Fact]
    public void InitiateSellTrailing_RemovesExistingBuyTrailing()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var sellConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };
        var buyConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };

        _sut.InitiateBuyTrailing(pair, price, cost, buyConfig);
        _sut.HasBuyTrailing(pair).Should().BeTrue();

        // Act
        _sut.InitiateSellTrailing(positionId, pair, price, 2.5m, 1.5m, sellConfig);

        // Assert
        _sut.HasBuyTrailing(pair).Should().BeFalse();
        _sut.HasSellTrailing(pair).Should().BeTrue();
    }

    [Fact]
    public void InitiateSellTrailing_WithNullPositionId_ThrowsArgumentNullException()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };

        // Act
        var act = () => _sut.InitiateSellTrailing(null!, pair, price, 2.5m, 1.5m, config);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("positionId");
    }

    #endregion

    #region InitiateBuyTrailing Tests

    [Fact]
    public void InitiateBuyTrailing_WithValidParameters_AddsBuyTrailing()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig
        {
            TrailingPercentage = 0.5m,
            StopMargin = 2m
        };

        // Act
        var result = _sut.InitiateBuyTrailing(pair, price, cost, config);

        // Assert
        result.Should().BeTrue();
        _sut.HasBuyTrailing(pair).Should().BeTrue();
        _sut.BuyTrailingCount.Should().Be(1);
        _sut.ActiveBuyTrailings.Should().HaveCount(1);
    }

    [Fact]
    public void InitiateBuyTrailing_WithPositionId_TracksDCA()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };

        // Act
        var result = _sut.InitiateBuyTrailing(pair, price, cost, config, positionId, "RSI_Buy");

        // Assert
        result.Should().BeTrue();
        var state = _sut.ActiveBuyTrailings.First();
        state.PositionId.Should().Be(positionId);
        state.SignalRule.Should().Be("RSI_Buy");
    }

    [Fact]
    public void InitiateBuyTrailing_WhenAlreadyExists_ReturnsFalse()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };

        _sut.InitiateBuyTrailing(pair, price, cost, config);

        // Act
        var result = _sut.InitiateBuyTrailing(pair, price, cost, config);

        // Assert
        result.Should().BeFalse();
        _sut.BuyTrailingCount.Should().Be(1);
    }

    [Fact]
    public void InitiateBuyTrailing_RemovesExistingSellTrailing()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var sellConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };
        var buyConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.5m, 1.5m, sellConfig);
        _sut.HasSellTrailing(pair).Should().BeTrue();

        // Act
        _sut.InitiateBuyTrailing(pair, price, cost, buyConfig);

        // Assert
        _sut.HasSellTrailing(pair).Should().BeFalse();
        _sut.HasBuyTrailing(pair).Should().BeTrue();
    }

    #endregion

    #region CancelTrailing Tests

    [Fact]
    public void CancelSellTrailing_WhenExists_RemovesTrailing()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.5m, 1.5m, config);

        // Act
        var result = _sut.CancelSellTrailing(pair);

        // Assert
        result.Should().BeTrue();
        _sut.HasSellTrailing(pair).Should().BeFalse();
    }

    [Fact]
    public void CancelSellTrailing_WhenNotExists_ReturnsFalse()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act
        var result = _sut.CancelSellTrailing(pair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CancelBuyTrailing_WhenExists_RemovesTrailing()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };

        _sut.InitiateBuyTrailing(pair, price, cost, config);

        // Act
        var result = _sut.CancelBuyTrailing(pair);

        // Assert
        result.Should().BeTrue();
        _sut.HasBuyTrailing(pair).Should().BeFalse();
    }

    [Fact]
    public void ClearAll_RemovesAllTrailings()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var price = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var positionId = PositionId.Create();
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };
        var sellConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };

        _sut.InitiateBuyTrailing(pair1, price, cost, config);
        _sut.InitiateSellTrailing(positionId, pair2, price, 2.5m, 1.5m, sellConfig);

        // Act
        _sut.ClearAll();

        // Assert
        _sut.BuyTrailingCount.Should().Be(0);
        _sut.SellTrailingCount.Should().Be(0);
    }

    #endregion

    #region UpdateSellTrailing Tests

    [Fact]
    public void UpdateSellTrailing_WhenMarginIncreases_UpdatesBestMargin()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.5m, 1.5m, config);

        // Act - price goes up, margin increases
        var newPrice = Price.Create(51000m);
        var newMargin = 3.5m;
        var result = _sut.UpdateSellTrailing(pair, newPrice, newMargin);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Continue);
        result.BestMargin.Should().Be(3.5m);
        result.CurrentMargin.Should().Be(3.5m);
    }

    [Fact]
    public void UpdateSellTrailing_WhenMarginDropsBelowTrailing_Triggers()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, price, 3.0m, 1.5m, config);

        // First update - margin goes up to 4.0%
        _sut.UpdateSellTrailing(pair, Price.Create(51000m), 4.0m);

        // Reinitiate since previous would have continued
        _sut.CancelSellTrailing(pair);
        _sut.InitiateSellTrailing(positionId, pair, price, 4.0m, 1.5m, config);

        // Act - margin drops by more than 1% (trailing percentage)
        var result = _sut.UpdateSellTrailing(pair, Price.Create(49500m), 2.8m);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Triggered);
        result.Reason.Should().Contain("Trailing triggered");
    }

    [Fact]
    public void UpdateSellTrailing_WhenMarginHitsStopMargin_TriggersWithExecuteAction()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig
        {
            TrailingPercentage = 1.0m,
            StopMargin = 0m,
            StopAction = TrailingStopAction.Execute
        };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.0m, 1.5m, config);

        // Act - margin drops to stop margin
        var result = _sut.UpdateSellTrailing(pair, Price.Create(49000m), -0.5m);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Triggered);
        result.Reason.Should().Contain("Stop margin");
    }

    [Fact]
    public void UpdateSellTrailing_WhenMarginHitsStopMargin_CancelsWithCancelAction()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig
        {
            TrailingPercentage = 1.0m,
            StopMargin = 0m,
            StopAction = TrailingStopAction.Cancel
        };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.0m, 1.5m, config);

        // Act - margin drops to stop margin
        var result = _sut.UpdateSellTrailing(pair, Price.Create(49000m), -0.5m);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Cancelled);
        result.Reason.Should().Contain("cancelled");
    }

    [Fact]
    public void UpdateSellTrailing_WhenTradingDisabled_ReturnsDisabled()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, price, 2.0m, 1.5m, config);

        // Act
        var result = _sut.UpdateSellTrailing(pair, Price.Create(50500m), 2.5m, isTradingEnabled: false);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Disabled);
        _sut.HasSellTrailing(pair).Should().BeFalse();
    }

    [Fact]
    public void UpdateSellTrailing_WhenNoTrailingExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);

        // Act
        var act = () => _sut.UpdateSellTrailing(pair, price, 2.0m);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{pair}*");
    }

    [Fact]
    public void UpdateSellTrailing_WhenMarginGoesNegativeDuringTrailing_Cancels()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -10m };

        // Target margin is positive (1.5%), meaning we expected to sell at profit
        _sut.InitiateSellTrailing(positionId, pair, price, 3.0m, 1.5m, config);

        // First, let best margin go up
        _sut.CancelSellTrailing(pair);
        _sut.InitiateSellTrailing(positionId, pair, price, 5.0m, 1.5m, config);

        // Act - margin drops significantly and goes negative but above stop margin
        // Best was 5.0, dropped by more than 1% trailing, now at -0.5%
        var result = _sut.UpdateSellTrailing(pair, Price.Create(48000m), -0.5m);

        // Assert - since target margin was positive but current is negative, should cancel
        result.Result.Should().Be(TrailingCheckResult.Cancelled);
        result.Reason.Should().Contain("negative");
    }

    #endregion

    #region UpdateBuyTrailing Tests

    [Fact]
    public void UpdateBuyTrailing_WhenPriceDrops_UpdatesBestMargin()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = 3m };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);

        // Act - price drops (margin becomes negative, which is good for buying)
        var newPrice = Price.Create(49000m); // -2% from initial
        var result = _sut.UpdateBuyTrailing(pair, newPrice);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Continue);
        result.BestMargin.Should().Be(-2m);
        result.CurrentMargin.Should().Be(-2m);
    }

    [Fact]
    public void UpdateBuyTrailing_WhenPriceBouncesUp_Triggers()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 5m };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);

        // First, let price drop to -3%
        _sut.UpdateBuyTrailing(pair, Price.Create(48500m)); // -3%

        // Re-setup since we want to test trigger
        _sut.CancelBuyTrailing(pair);
        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);

        // Simulate best margin at -3%
        var state = _sut.ActiveBuyTrailings.First();
        state.BestMargin = -3m;
        state.LastMargin = -3m;

        // Act - price bounces up by more than 0.5% trailing
        var result = _sut.UpdateBuyTrailing(pair, Price.Create(48750m)); // -2.5%, up 0.5% from best

        // Recalculate: -2.5 > -3 + 0.5 = -2.5, which is just at the boundary
        // Let's use 49000 instead (-2%), which is clearly above -2.5
        _sut.CancelBuyTrailing(pair);
        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);
        var state2 = _sut.ActiveBuyTrailings.First();
        state2.BestMargin = -3m;
        state2.LastMargin = -3m;

        var result2 = _sut.UpdateBuyTrailing(pair, Price.Create(49000m)); // -2%

        // Assert
        result2.Result.Should().Be(TrailingCheckResult.Triggered);
        result2.Reason.Should().Contain("Trailing triggered");
    }

    [Fact]
    public void UpdateBuyTrailing_WhenPriceHitsStopMargin_TriggersWithExecuteAction()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig
        {
            TrailingPercentage = 1.0m,
            StopMargin = 2m,
            StopAction = TrailingStopAction.Execute
        };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);

        // Act - price goes up to stop margin
        var result = _sut.UpdateBuyTrailing(pair, Price.Create(51000m)); // +2%

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Triggered);
        result.Reason.Should().Contain("Stop margin");
    }

    [Fact]
    public void UpdateBuyTrailing_WhenPriceHitsStopMargin_CancelsWithCancelAction()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig
        {
            TrailingPercentage = 1.0m,
            StopMargin = 2m,
            StopAction = TrailingStopAction.Cancel
        };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);

        // Act - price goes up to stop margin
        var result = _sut.UpdateBuyTrailing(pair, Price.Create(51500m)); // +3%

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Cancelled);
        result.Reason.Should().Contain("cancelled");
    }

    [Fact]
    public void UpdateBuyTrailing_WhenTradingDisabled_ReturnsDisabled()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = 3m };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);

        // Act
        var result = _sut.UpdateBuyTrailing(pair, Price.Create(49500m), isTradingEnabled: false);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Disabled);
        _sut.HasBuyTrailing(pair).Should().BeFalse();
    }

    [Fact]
    public void UpdateBuyTrailing_WhenNoTrailingExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);

        // Act
        var act = () => _sut.UpdateBuyTrailing(pair, price);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{pair}*");
    }

    [Fact]
    public void UpdateBuyTrailing_PreservesSignalRuleAndCost()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(200m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 2m };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config, positionId, "MACD_DCA");

        // Act
        var result = _sut.UpdateBuyTrailing(pair, Price.Create(51000m)); // triggers stop margin

        // Assert
        result.Cost.Should().Be(cost);
        result.SignalRule.Should().Be("MACD_DCA");
        result.PositionId.Should().Be(positionId);
    }

    #endregion

    #region UpdateSellTrailings (Batch) Tests

    [Fact]
    public void UpdateSellTrailings_ProcessesMultiplePairs()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var positionId1 = PositionId.Create();
        var positionId2 = PositionId.Create();
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId1, pair1, Price.Create(50000m), 2.0m, 1.5m, config);
        _sut.InitiateSellTrailing(positionId2, pair2, Price.Create(3000m), 3.0m, 1.5m, config);

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair1, Price.Create(50500m) },
            { pair2, Price.Create(3050m) }
        };

        // Act
        var results = _sut.UpdateSellTrailings(
            prices,
            (pair, price) => pair == pair1 ? 2.5m : 3.5m);

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Result == TrailingCheckResult.Continue);
    }

    [Fact]
    public void UpdateSellTrailings_SkipsPairsWithoutPrice()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var positionId1 = PositionId.Create();
        var positionId2 = PositionId.Create();
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId1, pair1, Price.Create(50000m), 2.0m, 1.5m, config);
        _sut.InitiateSellTrailing(positionId2, pair2, Price.Create(3000m), 3.0m, 1.5m, config);

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair1, Price.Create(50500m) }
            // pair2 has no price
        };

        // Act
        var results = _sut.UpdateSellTrailings(
            prices,
            (pair, price) => 2.5m);

        // Assert
        results.Should().HaveCount(1);
        results.Single().Pair.Should().Be(pair1);
    }

    [Fact]
    public void UpdateSellTrailings_RemovesTriggeredTrailings()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var positionId = PositionId.Create();
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, Price.Create(50000m), 5.0m, 1.5m, config);
        var state = _sut.ActiveSellTrailings.First();
        state.BestMargin = 5.0m;

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair, Price.Create(48000m) } // big drop
        };

        // Act
        var results = _sut.UpdateSellTrailings(
            prices,
            (p, pr) => 2.0m); // margin dropped from 5% to 2%, more than 1% trailing

        // Assert
        results.Single().Result.Should().Be(TrailingCheckResult.Triggered);
        _sut.HasSellTrailing(pair).Should().BeFalse();
    }

    #endregion

    #region UpdateBuyTrailings (Batch) Tests

    [Fact]
    public void UpdateBuyTrailings_ProcessesMultiplePairs()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = 5m };

        _sut.InitiateBuyTrailing(pair1, Price.Create(50000m), cost, config);
        _sut.InitiateBuyTrailing(pair2, Price.Create(3000m), cost, config);

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair1, Price.Create(49500m) }, // -1%
            { pair2, Price.Create(2970m) }   // -1%
        };

        // Act
        var results = _sut.UpdateBuyTrailings(prices);

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Result == TrailingCheckResult.Continue);
    }

    [Fact]
    public void UpdateBuyTrailings_SkipsPairsWithoutPrice()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = 5m };

        _sut.InitiateBuyTrailing(pair1, Price.Create(50000m), cost, config);
        _sut.InitiateBuyTrailing(pair2, Price.Create(3000m), cost, config);

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair1, Price.Create(49500m) }
            // pair2 has no price
        };

        // Act
        var results = _sut.UpdateBuyTrailings(prices);

        // Assert
        results.Should().HaveCount(1);
        results.Single().Pair.Should().Be(pair1);
    }

    [Fact]
    public void UpdateBuyTrailings_WithTradingDisabledCheck_RemovesDisabledPairs()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = 5m };

        _sut.InitiateBuyTrailing(pair1, Price.Create(50000m), cost, config);
        _sut.InitiateBuyTrailing(pair2, Price.Create(3000m), cost, config);

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair1, Price.Create(49500m) },
            { pair2, Price.Create(2970m) }
        };

        // Act - pair2 trading is disabled
        var results = _sut.UpdateBuyTrailings(
            prices,
            p => p != pair2);

        // Assert
        var disabledResult = results.FirstOrDefault(r => r.Pair == pair2);
        disabledResult.Should().NotBeNull();
        disabledResult!.Result.Should().Be(TrailingCheckResult.Disabled);
        _sut.HasBuyTrailing(pair2).Should().BeFalse();
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void MultipleTrailings_DifferentPairs_WorkIndependently()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var ethPair = TradingPair.Create("ETHUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");
        var positionId = PositionId.Create();
        var buyConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = 3m };
        var sellConfig = new TrailingConfig { TrailingPercentage = 0.5m, StopMargin = -5m };

        // BTC has buy trailing, ETH has sell trailing
        _sut.InitiateBuyTrailing(btcPair, Price.Create(50000m), cost, buyConfig);
        _sut.InitiateSellTrailing(positionId, ethPair, Price.Create(3000m), 2.0m, 1.5m, sellConfig);

        // Act & Assert
        _sut.BuyTrailingCount.Should().Be(1);
        _sut.SellTrailingCount.Should().Be(1);
        _sut.HasBuyTrailing(btcPair).Should().BeTrue();
        _sut.HasSellTrailing(ethPair).Should().BeTrue();
        _sut.HasBuyTrailing(ethPair).Should().BeFalse();
        _sut.HasSellTrailing(btcPair).Should().BeFalse();
    }

    [Fact]
    public void SellTrailing_AtExactBoundary_ContinuesTrailing()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = -5m };

        _sut.InitiateSellTrailing(positionId, pair, price, 3.0m, 1.5m, config);
        var state = _sut.ActiveSellTrailings.First();
        state.BestMargin = 3.0m;

        // Act - margin at exactly best - trailing (3.0 - 1.0 = 2.0)
        // currentMargin (2.0) < bestMargin - trailing (2.0) is false (equal, not less)
        var result = _sut.UpdateSellTrailing(pair, Price.Create(49900m), 2.0m);

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Continue);
    }

    [Fact]
    public void BuyTrailing_AtExactBoundary_ContinuesTrailing()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var cost = Money.Create(100m, "USDT");
        var config = new TrailingConfig { TrailingPercentage = 1.0m, StopMargin = 5m };

        _sut.InitiateBuyTrailing(pair, initialPrice, cost, config);
        var state = _sut.ActiveBuyTrailings.First();
        state.BestMargin = -3.0m;
        state.LastMargin = -3.0m;

        // Act - margin at exactly best + trailing (-3.0 + 1.0 = -2.0)
        // currentMargin (-2.0) > bestMargin + trailing (-2.0) is false (equal, not greater)
        var result = _sut.UpdateBuyTrailing(pair, Price.Create(49000m)); // -2%

        // Assert
        result.Result.Should().Be(TrailingCheckResult.Continue);
    }

    [Fact]
    public void TrailingState_PreservesAllProperties()
    {
        // Arrange
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var price = Price.Create(50000m);
        var config = new TrailingConfig
        {
            TrailingPercentage = 1.5m,
            StopMargin = -3m,
            StopAction = TrailingStopAction.Cancel
        };

        // Act
        _sut.InitiateSellTrailing(positionId, pair, price, 5.0m, 2.0m, config);

        // Assert
        var state = _sut.ActiveSellTrailings.First();
        state.PositionId.Should().Be(positionId);
        state.Pair.Should().Be(pair);
        state.Config.TrailingPercentage.Should().Be(1.5m);
        state.Config.StopMargin.Should().Be(-3m);
        state.Config.StopAction.Should().Be(TrailingStopAction.Cancel);
        state.TargetMargin.Should().Be(2.0m);
        state.InitialPrice.Should().Be(price);
        state.InitialMargin.Should().Be(5.0m);
        state.BestMargin.Should().Be(5.0m);
        state.LastMargin.Should().Be(5.0m);
        state.StartedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion
}
