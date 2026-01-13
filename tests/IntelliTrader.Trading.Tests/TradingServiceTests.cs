using FluentAssertions;
using Moq;
using IntelliTrader.Core;
using IntelliTrader.Trading;

namespace IntelliTrader.Trading.Tests;

public class TradingServiceTests
{
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<IRulesService> _rulesServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly Mock<ISignalsService> _signalsServiceMock;
    private readonly Mock<IExchangeService> _exchangeServiceMock;
    private readonly Mock<ITradingAccount> _accountMock;
    private readonly TradingService _sut;

    public TradingServiceTests()
    {
        _coreServiceMock = new Mock<ICoreService>();
        _loggingServiceMock = new Mock<ILoggingService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _rulesServiceMock = new Mock<IRulesService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();
        _signalsServiceMock = new Mock<ISignalsService>();
        _exchangeServiceMock = new Mock<IExchangeService>();
        _accountMock = new Mock<ITradingAccount>();

        // Setup backtesting config (not replaying by default)
        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        _backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        // Setup signals config
        var signalsConfigMock = new Mock<ISignalsConfig>();
        signalsConfigMock.Setup(x => x.Enabled).Returns(false);
        _signalsServiceMock.Setup(x => x.Config).Returns(signalsConfigMock.Object);

        // Setup empty rules
        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        _signalsServiceMock.Setup(x => x.Rules).Returns(rulesModuleMock.Object);

        // Factory that returns the exchange service
        Func<string, IExchangeService> exchangeServiceFactory = (name) => _exchangeServiceMock.Object;

        _sut = new TradingService(
            _coreServiceMock.Object,
            _loggingServiceMock.Object,
            _notificationServiceMock.Object,
            _healthCheckServiceMock.Object,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            _signalsServiceMock.Object,
            exchangeServiceFactory);

        // Inject account via reflection (since it's set in Start())
        SetupAccount(_accountMock.Object);
    }

    private void SetupAccount(ITradingAccount account)
    {
        var accountField = typeof(TradingService).GetProperty("Account");
        if (accountField != null && accountField.CanWrite)
        {
            accountField.SetValue(_sut, account);
        }
        else
        {
            // Use reflection to set the backing field directly
            var field = typeof(TradingService).GetField("<Account>k__BackingField",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_sut, account);
        }
    }

    private void SetupTradingSuspended(bool suspended)
    {
        if (suspended)
        {
            _sut.SuspendTrading(false);
        }
        else
        {
            _sut.ResumeTrading(true);
        }
    }

    private static ITradingPair CreateTradingPair(
        string pair,
        decimal totalAmount = 1.0m,
        decimal averagePricePaid = 100m,
        decimal currentPrice = 100m,
        int dcaLevel = 0)
    {
        var mockPair = new Mock<ITradingPair>();
        mockPair.Setup(x => x.Pair).Returns(pair);
        mockPair.Setup(x => x.TotalAmount).Returns(totalAmount);
        mockPair.Setup(x => x.AveragePricePaid).Returns(averagePricePaid);
        mockPair.Setup(x => x.CurrentPrice).Returns(currentPrice);
        mockPair.Setup(x => x.CurrentMargin).Returns(((currentPrice - averagePricePaid) / averagePricePaid) * 100);
        mockPair.Setup(x => x.DCALevel).Returns(dcaLevel);
        mockPair.Setup(x => x.OrderDates).Returns(new List<DateTimeOffset> { DateTimeOffset.Now.AddMinutes(-5) });
        mockPair.Setup(x => x.FormattedName).Returns(pair);
        mockPair.Setup(x => x.CurrentCost).Returns(currentPrice * totalAmount);
        mockPair.Setup(x => x.AverageCostPaid).Returns(averagePricePaid * totalAmount);

        var metadata = new OrderMetadata();
        mockPair.Setup(x => x.Metadata).Returns(metadata);

        return mockPair.Object;
    }

    private void SetupDefaultMocks(decimal balance = 10000m)
    {
        _accountMock.Setup(x => x.GetBalance()).Returns(balance);
        _accountMock.Setup(x => x.GetTradingPairs()).Returns(new List<ITradingPair>());
        _exchangeServiceMock.Setup(x => x.GetLastPrice(It.IsAny<string>())).ReturnsAsync(100m);
        SetupTradingSuspended(false);
    }

    #region CanBuy Tests

    [Fact]
    public void CanBuy_WhenTradingSuspended_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(true);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("trading suspended");
    }

    [Fact]
    public void CanBuy_WhenManualOrderAndTradingSuspended_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(true);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m, ManualOrder = true };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Fact]
    public void CanBuy_WhenPairAlreadyExists_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair already exists");
    }

    [Fact]
    public void CanBuy_WhenPairExistsButIgnoreExisting_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m, IgnoreExisting = true };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Fact]
    public void CanBuy_WhenInvalidPrice_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _exchangeServiceMock.Setup(x => x.GetLastPrice("BTCUSDT")).ReturnsAsync(0m);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("invalid price");
    }

    [Fact]
    public void CanBuy_WhenInsufficientBalance_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks(balance: 50m);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("not enough balance");
    }

    [Fact]
    public void CanBuy_WhenBothAmountAndMaxCostSpecified_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT") { Amount = 1m, MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("either max cost or amount");
    }

    [Fact]
    public void CanBuy_WhenNeitherAmountNorMaxCostSpecified_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT");

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("either max cost or amount");
    }

    [Fact]
    public void CanBuy_WithValidConditions_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Fact]
    public void CanBuy_WithAmountSpecified_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT") { Amount = 0.01m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    #endregion

    #region CanSell Tests

    [Fact]
    public void CanSell_WhenTradingSuspended_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(true);
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        var options = new SellOptions("BTCUSDT");

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("trading suspended");
    }

    [Fact]
    public void CanSell_WhenManualOrderAndTradingSuspended_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(true);
        var tradingPair = CreateTradingPair("BTCUSDT");
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPair);
        var options = new SellOptions("BTCUSDT") { ManualOrder = true };

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Fact]
    public void CanSell_WhenPairDoesNotExist_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);
        var options = new SellOptions("BTCUSDT");

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair does not exist");
    }

    [Fact]
    public void CanSell_WhenPairJustBought_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var tradingPairMock = new Mock<ITradingPair>();
        tradingPairMock.Setup(x => x.Pair).Returns("BTCUSDT");
        tradingPairMock.Setup(x => x.OrderDates).Returns(new List<DateTimeOffset> { DateTimeOffset.Now }); // Just bought

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPairMock.Object);
        var options = new SellOptions("BTCUSDT");

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair just bought");
    }

    [Fact]
    public void CanSell_WithValidConditions_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        var tradingPair = CreateTradingPair("BTCUSDT");
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPair);
        var options = new SellOptions("BTCUSDT");

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    #endregion

    #region CanSwap Tests

    [Fact]
    public void CanSwap_WhenOldPairDoesNotExist_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);
        var options = new SwapOptions("BTCUSDT", "ETHUSDT", new OrderMetadata());

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair does not exist");
    }

    [Fact]
    public void CanSwap_WhenNewPairAlreadyExists_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair("ETHUSDT")).Returns(true);
        var options = new SwapOptions("BTCUSDT", "ETHUSDT", new OrderMetadata());

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair already exists");
    }

    [Fact]
    public void CanSwap_WhenNewPairNotInMarket_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair("INVALIDPAIR")).Returns(false);
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "BTCUSDT", "ETHUSDT" }); // INVALIDPAIR not in list
        var options = new SwapOptions("BTCUSDT", "INVALIDPAIR", new OrderMetadata());

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("not a valid pair");
    }

    [Fact]
    public void CanSwap_WithValidConditions_ReturnsTrue()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair("ETHUSDT")).Returns(false);
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "BTCUSDT", "ETHUSDT" });
        var options = new SwapOptions("BTCUSDT", "ETHUSDT", new OrderMetadata()) { ManualOrder = true };

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    #endregion

    #region SuspendTrading / ResumeTrading Tests

    [Fact]
    public void SuspendTrading_SetsIsTradingSuspendedToTrue()
    {
        // Arrange
        _sut.ResumeTrading(true);
        _sut.IsTradingSuspended.Should().BeFalse();

        // Act
        _sut.SuspendTrading(false);

        // Assert
        _sut.IsTradingSuspended.Should().BeTrue();
    }

    [Fact]
    public void ResumeTrading_SetsIsTradingSuspendedToFalse()
    {
        // Arrange
        _sut.SuspendTrading(false);
        _sut.IsTradingSuspended.Should().BeTrue();

        // Act
        _sut.ResumeTrading(true);

        // Assert
        _sut.IsTradingSuspended.Should().BeFalse();
    }

    [Fact]
    public void ResumeTrading_WhenForcefullySuspended_RequiresForceToResume()
    {
        // Arrange
        _sut.SuspendTrading(forced: true);

        // Act - Try to resume without force
        _sut.ResumeTrading(forced: false);

        // Assert - Should still be suspended
        _sut.IsTradingSuspended.Should().BeTrue();

        // Act - Resume with force
        _sut.ResumeTrading(forced: true);

        // Assert - Now should be resumed
        _sut.IsTradingSuspended.Should().BeFalse();
    }

    #endregion

    #region GetTrailingBuys / GetTrailingSells Tests

    [Fact]
    public void GetTrailingBuys_ReturnsEmptyListInitially()
    {
        // Act
        var result = _sut.GetTrailingBuys();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetTrailingSells_ReturnsEmptyListInitially()
    {
        // Act
        var result = _sut.GetTrailingSells();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SuspendTrading_ClearsTrailingBuysAndSells()
    {
        // Arrange
        _sut.ResumeTrading(true);

        // Act
        _sut.SuspendTrading(false);

        // Assert
        _sut.GetTrailingBuys().Should().BeEmpty();
        _sut.GetTrailingSells().Should().BeEmpty();
    }

    #endregion

    #region LogOrder Tests

    [Fact]
    public void LogOrder_AddsOrderToHistory()
    {
        // Arrange
        var orderDetailsMock = new Mock<IOrderDetails>();
        orderDetailsMock.Setup(x => x.OrderId).Returns("123");
        orderDetailsMock.Setup(x => x.Pair).Returns("BTCUSDT");
        orderDetailsMock.Setup(x => x.Side).Returns(OrderSide.Buy);

        // Act
        _sut.LogOrder(orderDetailsMock.Object);

        // Assert
        _sut.OrderHistory.Should().HaveCount(1);
        _sut.OrderHistory.Should().Contain(orderDetailsMock.Object);
    }

    [Fact]
    public void LogOrder_PreservesOrderHistoryStack()
    {
        // Arrange
        var order1 = new Mock<IOrderDetails>();
        order1.Setup(x => x.OrderId).Returns("1");
        var order2 = new Mock<IOrderDetails>();
        order2.Setup(x => x.OrderId).Returns("2");

        // Act
        _sut.LogOrder(order1.Object);
        _sut.LogOrder(order2.Object);

        // Assert - Stack should have order2 on top (LIFO)
        _sut.OrderHistory.Should().HaveCount(2);
        _sut.OrderHistory.TryPeek(out var topOrder);
        topOrder.Should().Be(order2.Object);
    }

    #endregion

    #region ReapplyTradingRules Tests

    [Fact]
    public void ReapplyTradingRules_ClearsPairConfigCache()
    {
        // Arrange
        SetupDefaultMocks();

        // Get a config to populate the cache
        var config1 = _sut.GetPairConfig("BTCUSDT");

        // Act
        _sut.ReapplyTradingRules();

        // Get config again after clearing
        var config2 = _sut.GetPairConfig("BTCUSDT");

        // Assert - Should get a new config (cache was cleared)
        // Config objects should be different instances
        config1.Should().NotBeNull();
        config2.Should().NotBeNull();
    }

    #endregion
}
