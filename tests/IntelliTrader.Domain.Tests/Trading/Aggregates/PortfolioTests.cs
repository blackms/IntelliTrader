using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.Aggregates;

public class PortfolioTests
{
    private static TradingPair CreatePair(string symbol = "BTCUSDT") => TradingPair.Create(symbol, "USDT");
    private static PositionId CreatePositionId() => PositionId.Create();
    private static Money CreateMoney(decimal amount) => Money.Create(amount, "USDT");

    #region Create Portfolio

    [Fact]
    public void Create_WithValidParameters_CreatesPortfolio()
    {
        // Act
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Assert
        portfolio.Id.Should().NotBeNull();
        portfolio.Name.Should().Be("Main");
        portfolio.Market.Should().Be("USDT");
        portfolio.Balance.Total.Amount.Should().Be(10000m);
        portfolio.Balance.Available.Amount.Should().Be(10000m);
        portfolio.Balance.Reserved.Amount.Should().Be(0m);
        portfolio.MaxPositions.Should().Be(10);
        portfolio.MinPositionCost.Amount.Should().Be(100m);
        portfolio.ActivePositionCount.Should().Be(0);
        portfolio.CanOpenNewPosition.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "USDT", 10000, 10, 100)]
    [InlineData(null, "USDT", 10000, 10, 100)]
    public void Create_WithInvalidName_ThrowsArgumentException(string? name, string market, decimal balance, int maxPos, decimal minCost)
    {
        // Act
        var act = () => Portfolio.Create(name!, market, balance, maxPos, minCost);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Theory]
    [InlineData("Main", "", 10000, 10, 100)]
    [InlineData("Main", null, 10000, 10, 100)]
    public void Create_WithInvalidMarket_ThrowsArgumentException(string name, string? market, decimal balance, int maxPos, decimal minCost)
    {
        // Act
        var act = () => Portfolio.Create(name, market!, balance, maxPos, minCost);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("market");
    }

    [Fact]
    public void Create_WithNegativeBalance_ThrowsArgumentException()
    {
        // Act
        var act = () => Portfolio.Create("Main", "USDT", -100m, 10, 100m);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("initialBalance");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidMaxPositions_ThrowsArgumentException(int maxPositions)
    {
        // Act
        var act = () => Portfolio.Create("Main", "USDT", 10000m, maxPositions, 100m);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("maxPositions");
    }

    [Fact]
    public void Create_WithNegativeMinPositionCost_ThrowsArgumentException()
    {
        // Act
        var act = () => Portfolio.Create("Main", "USDT", 10000m, 10, -100m);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("minPositionCost");
    }

    #endregion

    #region CanAfford

    [Theory]
    [InlineData(10000, 5000, true)]
    [InlineData(10000, 10000, true)]
    [InlineData(10000, 10001, false)]
    [InlineData(0, 100, false)]
    public void CanAfford_ReturnsCorrectResult(decimal balance, decimal cost, bool expected)
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", balance, 10, 100m);

        // Act
        var result = portfolio.CanAfford(CreateMoney(cost));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void CanAfford_WithDifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        var act = () => portfolio.CanAfford(Money.Create(100m, "BTC"));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*currency*");
    }

    #endregion

    #region Record Position Opened

    [Fact]
    public void RecordPositionOpened_WithValidParameters_AddsPosition()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        var cost = CreateMoney(1000m);

        // Act
        portfolio.RecordPositionOpened(positionId, pair, cost);

        // Assert
        portfolio.ActivePositionCount.Should().Be(1);
        portfolio.HasPositionFor(pair).Should().BeTrue();
        portfolio.GetPositionId(pair).Should().Be(positionId);
        portfolio.Balance.Available.Amount.Should().Be(9000m);
        portfolio.Balance.Reserved.Amount.Should().Be(1000m);
    }

    [Fact]
    public void RecordPositionOpened_RaisesPositionAddedEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();

        // Act
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));

        // Assert
        portfolio.DomainEvents.Should().Contain(e => e is PositionAddedToPortfolio);
        var evt = portfolio.DomainEvents.OfType<PositionAddedToPortfolio>().Single();
        evt.PositionId.Should().Be(positionId);
        evt.Pair.Should().Be(pair);
        evt.Cost.Amount.Should().Be(1000m);
    }

    [Fact]
    public void RecordPositionOpened_RaisesBalanceChangedEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair(), CreateMoney(1000m));

        // Assert
        portfolio.DomainEvents.Should().Contain(e => e is PortfolioBalanceChanged);
    }

    [Fact]
    public void RecordPositionOpened_WhenPositionAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var pair = CreatePair();
        portfolio.RecordPositionOpened(CreatePositionId(), pair, CreateMoney(1000m));

        // Act
        var act = () => portfolio.RecordPositionOpened(CreatePositionId(), pair, CreateMoney(1000m));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public void RecordPositionOpened_WhenMaxPositionsReached_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 2, 100m);
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("BTCUSDT"), CreateMoney(1000m));
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("ETHUSDT"), CreateMoney(1000m));

        // Act
        var act = () => portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("SOLUSDT"), CreateMoney(1000m));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Maximum positions*");
    }

    [Fact]
    public void RecordPositionOpened_WhenMaxPositionsReached_RaisesMaxPositionsReachedEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 2, 100m);
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("BTCUSDT"), CreateMoney(1000m));
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("ETHUSDT"), CreateMoney(1000m));
        portfolio.ClearDomainEvents();

        // Act
        try
        {
            portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("SOLUSDT"), CreateMoney(1000m));
        }
        catch { /* Expected */ }

        // Assert
        portfolio.DomainEvents.Should().Contain(e => e is MaxPositionsReached);
    }

    [Fact]
    public void RecordPositionOpened_WhenInsufficientFunds_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 1000m, 10, 100m);

        // Act
        var act = () => portfolio.RecordPositionOpened(CreatePositionId(), CreatePair(), CreateMoney(2000m));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Insufficient funds*");
    }

    #endregion

    #region Record Position Cost Increased (DCA)

    [Fact]
    public void RecordPositionCostIncreased_WithValidParameters_UpdatesCost()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));

        // Act
        portfolio.RecordPositionCostIncreased(positionId, pair, CreateMoney(500m));

        // Assert
        portfolio.Balance.Available.Amount.Should().Be(8500m);
        portfolio.Balance.Reserved.Amount.Should().Be(1500m);
        portfolio.GetPositionCost(positionId)!.Amount.Should().Be(1500m);
    }

    [Fact]
    public void RecordPositionCostIncreased_RaisesBalanceChangedEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));
        portfolio.ClearDomainEvents();

        // Act
        portfolio.RecordPositionCostIncreased(positionId, pair, CreateMoney(500m));

        // Assert
        portfolio.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PortfolioBalanceChanged>();
    }

    [Fact]
    public void RecordPositionCostIncreased_WhenNoPositionExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        var act = () => portfolio.RecordPositionCostIncreased(CreatePositionId(), CreatePair(), CreateMoney(500m));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*No position exists*");
    }

    [Fact]
    public void RecordPositionCostIncreased_WhenInsufficientFunds_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 1500m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));

        // Act
        var act = () => portfolio.RecordPositionCostIncreased(positionId, pair, CreateMoney(1000m));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Insufficient funds*");
    }

    #endregion

    #region Record Position Closed

    [Fact]
    public void RecordPositionClosed_WithProfit_UpdatesBalanceCorrectly()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));

        // Act - Sell for profit (1100 proceeds from 1000 cost)
        portfolio.RecordPositionClosed(positionId, pair, CreateMoney(1100m));

        // Assert
        portfolio.ActivePositionCount.Should().Be(0);
        portfolio.HasPositionFor(pair).Should().BeFalse();
        portfolio.Balance.Available.Amount.Should().Be(10100m); // Original 10000 + 100 profit
        portfolio.Balance.Reserved.Amount.Should().Be(0m);
        portfolio.Balance.Total.Amount.Should().Be(10100m);
    }

    [Fact]
    public void RecordPositionClosed_WithLoss_UpdatesBalanceCorrectly()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));

        // Act - Sell at loss (900 proceeds from 1000 cost)
        portfolio.RecordPositionClosed(positionId, pair, CreateMoney(900m));

        // Assert
        portfolio.Balance.Available.Amount.Should().Be(9900m); // Original 10000 - 100 loss
        portfolio.Balance.Total.Amount.Should().Be(9900m);
    }

    [Fact]
    public void RecordPositionClosed_RaisesPositionRemovedEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var positionId = CreatePositionId();
        var pair = CreatePair();
        portfolio.RecordPositionOpened(positionId, pair, CreateMoney(1000m));
        portfolio.ClearDomainEvents();

        // Act
        portfolio.RecordPositionClosed(positionId, pair, CreateMoney(1100m));

        // Assert
        portfolio.DomainEvents.Should().Contain(e => e is PositionRemovedFromPortfolio);
        var evt = portfolio.DomainEvents.OfType<PositionRemovedFromPortfolio>().Single();
        evt.PositionId.Should().Be(positionId);
        evt.Proceeds.Amount.Should().Be(1100m);
        evt.PnL.Amount.Should().Be(100m);
    }

    [Fact]
    public void RecordPositionClosed_WhenNoPositionExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        var act = () => portfolio.RecordPositionClosed(CreatePositionId(), CreatePair(), CreateMoney(1100m));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*No position exists*");
    }

    #endregion

    #region CanOpenNewPosition

    [Fact]
    public void CanOpenNewPosition_WhenHasFundsAndSlots_ReturnsTrue()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Assert
        portfolio.CanOpenNewPosition.Should().BeTrue();
    }

    [Fact]
    public void CanOpenNewPosition_WhenAtMaxPositions_ReturnsFalse()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 1, 100m);
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair(), CreateMoney(1000m));

        // Assert
        portfolio.CanOpenNewPosition.Should().BeFalse();
        portfolio.IsAtMaxPositions.Should().BeTrue();
    }

    [Fact]
    public void CanOpenNewPosition_WhenInsufficientFunds_ReturnsFalse()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 50m, 10, 100m);

        // Assert
        portfolio.CanOpenNewPosition.Should().BeFalse();
    }

    #endregion

    #region Update Configuration

    [Fact]
    public void UpdateConfiguration_UpdatesMaxPositions()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        portfolio.UpdateConfiguration(maxPositions: 20);

        // Assert
        portfolio.MaxPositions.Should().Be(20);
    }

    [Fact]
    public void UpdateConfiguration_UpdatesMinPositionCost()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        portfolio.UpdateConfiguration(minPositionCost: 200m);

        // Assert
        portfolio.MinPositionCost.Amount.Should().Be(200m);
    }

    [Fact]
    public void UpdateConfiguration_WithInvalidMaxPositions_ThrowsArgumentException()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        var act = () => portfolio.UpdateConfiguration(maxPositions: 0);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Sync Balance

    [Fact]
    public void SyncBalance_UpdatesTotalAndAvailable()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        portfolio.SyncBalance(12000m);

        // Assert
        portfolio.Balance.Total.Amount.Should().Be(12000m);
        portfolio.Balance.Available.Amount.Should().Be(12000m);
    }

    [Fact]
    public void SyncBalance_WithReservedFunds_MaintainsReserved()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair(), CreateMoney(1000m));
        // Now: Total=10000, Available=9000, Reserved=1000

        // Act - Exchange shows 11000 total
        portfolio.SyncBalance(11000m);

        // Assert
        portfolio.Balance.Total.Amount.Should().Be(11000m);
        portfolio.Balance.Reserved.Amount.Should().Be(1000m);
        portfolio.Balance.Available.Amount.Should().Be(10000m); // 11000 - 1000 reserved
    }

    [Fact]
    public void SyncBalance_RaisesBalanceChangedEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        portfolio.ClearDomainEvents();

        // Act
        portfolio.SyncBalance(12000m);

        // Assert
        portfolio.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PortfolioBalanceChanged>();
    }

    [Fact]
    public void SyncBalance_WhenNoChange_DoesNotRaiseEvent()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        portfolio.ClearDomainEvents();

        // Act
        portfolio.SyncBalance(10000m);

        // Assert
        portfolio.DomainEvents.Should().BeEmpty();
    }

    #endregion

    #region Get Total Invested Cost

    [Fact]
    public void GetTotalInvestedCost_WithNoPositions_ReturnsZero()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        var totalCost = portfolio.GetTotalInvestedCost();

        // Assert
        totalCost.Amount.Should().Be(0m);
    }

    [Fact]
    public void GetTotalInvestedCost_WithMultiplePositions_ReturnsSumOfCosts()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("BTCUSDT"), CreateMoney(1000m));
        portfolio.RecordPositionOpened(CreatePositionId(), CreatePair("ETHUSDT"), CreateMoney(500m));

        // Act
        var totalCost = portfolio.GetTotalInvestedCost();

        // Assert
        totalCost.Amount.Should().Be(1500m);
    }

    #endregion

    #region Get Active Pairs

    [Fact]
    public void GetActivePairs_ReturnsAllActivePairs()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);
        var btc = CreatePair("BTCUSDT");
        var eth = CreatePair("ETHUSDT");
        portfolio.RecordPositionOpened(CreatePositionId(), btc, CreateMoney(1000m));
        portfolio.RecordPositionOpened(CreatePositionId(), eth, CreateMoney(500m));

        // Act
        var pairs = portfolio.GetActivePairs();

        // Assert
        pairs.Should().HaveCount(2);
        pairs.Should().Contain(btc);
        pairs.Should().Contain(eth);
    }

    #endregion

    #region Calculate Max Position Cost

    [Theory]
    [InlineData(10000, 100, 10000)] // 100% of 10000
    [InlineData(10000, 50, 5000)]   // 50% of 10000
    [InlineData(10000, 10, 1000)]   // 10% of 10000
    public void CalculateMaxPositionCost_CalculatesCorrectly(decimal balance, decimal percentage, decimal expected)
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", balance, 10, 100m);

        // Act
        var maxCost = portfolio.CalculateMaxPositionCost(percentage);

        // Assert
        maxCost.Amount.Should().Be(expected);
    }

    [Fact]
    public void CalculateMaxPositionCost_WhenBelowMinButHasFunds_ReturnsMin()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 500m);

        // Act - 1% of 10000 = 100, but min is 500
        var maxCost = portfolio.CalculateMaxPositionCost(1m);

        // Assert
        maxCost.Amount.Should().Be(500m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void CalculateMaxPositionCost_WithInvalidPercentage_ThrowsArgumentException(decimal percentage)
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 10, 100m);

        // Act
        var act = () => portfolio.CalculateMaxPositionCost(percentage);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Multiple Position Workflow

    [Fact]
    public void CompleteWorkflow_MultiplePositionsWithDCAAndClose()
    {
        // Arrange
        var portfolio = Portfolio.Create("Main", "USDT", 10000m, 5, 100m);
        var btcPosId = CreatePositionId();
        var ethPosId = CreatePositionId();
        var btc = CreatePair("BTCUSDT");
        var eth = CreatePair("ETHUSDT");

        // Act - Open BTC position
        portfolio.RecordPositionOpened(btcPosId, btc, CreateMoney(2000m));
        portfolio.Balance.Available.Amount.Should().Be(8000m);

        // Act - Open ETH position
        portfolio.RecordPositionOpened(ethPosId, eth, CreateMoney(1500m));
        portfolio.Balance.Available.Amount.Should().Be(6500m);

        // Act - DCA into BTC
        portfolio.RecordPositionCostIncreased(btcPosId, btc, CreateMoney(1000m));
        portfolio.Balance.Available.Amount.Should().Be(5500m);
        portfolio.GetPositionCost(btcPosId)!.Amount.Should().Be(3000m);

        // Act - Close ETH with profit
        portfolio.RecordPositionClosed(ethPosId, eth, CreateMoney(1800m));
        portfolio.Balance.Available.Amount.Should().Be(7300m); // 5500 + 1800
        portfolio.Balance.Total.Amount.Should().Be(10300m); // 10000 + 300 profit

        // Act - Close BTC with loss
        portfolio.RecordPositionClosed(btcPosId, btc, CreateMoney(2700m));
        portfolio.Balance.Available.Amount.Should().Be(10000m); // 7300 + 2700
        portfolio.Balance.Total.Amount.Should().Be(10000m); // 10300 - 300 loss

        // Assert final state
        portfolio.ActivePositionCount.Should().Be(0);
        portfolio.Balance.Reserved.Amount.Should().Be(0m);
    }

    #endregion
}
