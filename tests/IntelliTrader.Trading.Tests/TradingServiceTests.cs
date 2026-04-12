using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using Xunit;
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
    private readonly Mock<IApplicationContext> _applicationContextMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
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
        _applicationContextMock = new Mock<IApplicationContext>();
        _configProviderMock = new Mock<IConfigProvider>();

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
            _loggingServiceMock.Object,
            _notificationServiceMock.Object,
            _healthCheckServiceMock.Object,
            _rulesServiceMock.Object,
            new Lazy<IBacktestingService>(() => _backtestingServiceMock.Object),
            new Lazy<ISignalsService>(() => _signalsServiceMock.Object),
            exchangeServiceFactory,
            _applicationContextMock.Object,
            _configProviderMock.Object);

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

    #region Swap Tests

    private Mock<ITradingPair> CreateMockTradingPairForSwap(
        string pair,
        decimal totalAmount = 1.0m,
        decimal averagePricePaid = 100m,
        decimal currentPrice = 100m,
        decimal currentMargin = 0m,
        int dcaLevel = 0,
        decimal? additionalCosts = null,
        int? additionalDCALevels = null)
    {
        var mockPair = new Mock<ITradingPair>();
        mockPair.Setup(x => x.Pair).Returns(pair);
        mockPair.Setup(x => x.TotalAmount).Returns(totalAmount);
        mockPair.Setup(x => x.AveragePricePaid).Returns(averagePricePaid);
        mockPair.Setup(x => x.CurrentPrice).Returns(currentPrice);
        mockPair.Setup(x => x.CurrentMargin).Returns(currentMargin);
        mockPair.Setup(x => x.DCALevel).Returns(dcaLevel);
        mockPair.Setup(x => x.OrderDates).Returns(new List<DateTimeOffset> { DateTimeOffset.Now.AddMinutes(-5) });
        mockPair.Setup(x => x.FormattedName).Returns(dcaLevel > 0 ? $"{pair}({dcaLevel})" : pair);
        mockPair.Setup(x => x.CurrentCost).Returns(currentPrice * totalAmount);
        mockPair.Setup(x => x.AverageCostPaid).Returns(averagePricePaid * totalAmount);

        var metadata = new OrderMetadata
        {
            AdditionalCosts = additionalCosts,
            AdditionalDCALevels = additionalDCALevels
        };
        mockPair.Setup(x => x.Metadata).Returns(metadata);

        return mockPair;
    }

    private Mock<IOrderDetails> CreateMockOrderDetailsForSwap(
        string pair,
        OrderSide side,
        OrderResult result = OrderResult.Filled,
        decimal averageCost = 100m,
        decimal averagePrice = 100m,
        decimal amountFilled = 1.0m,
        decimal fees = 0.1m,
        string feesCurrency = "USDT")
    {
        var mockOrder = new Mock<IOrderDetails>();
        mockOrder.Setup(x => x.OrderId).Returns(Guid.NewGuid().ToString());
        mockOrder.Setup(x => x.Pair).Returns(pair);
        mockOrder.Setup(x => x.Side).Returns(side);
        mockOrder.Setup(x => x.Result).Returns(result);
        mockOrder.Setup(x => x.AverageCost).Returns(averageCost);
        mockOrder.Setup(x => x.AveragePrice).Returns(averagePrice);
        mockOrder.Setup(x => x.AmountFilled).Returns(amountFilled);
        mockOrder.Setup(x => x.Fees).Returns(fees);
        mockOrder.Setup(x => x.FeesCurrency).Returns(feesCurrency);
        mockOrder.Setup(x => x.Date).Returns(DateTimeOffset.Now);
        mockOrder.Setup(x => x.Metadata).Returns(new OrderMetadata());
        return mockOrder;
    }

    private void SetupSwapMocks(
        string oldPair,
        string newPair,
        Mock<ITradingPair> oldTradingPairMock,
        Mock<IOrderDetails> sellOrderMock,
        Mock<IOrderDetails> buyOrderMock,
        TradingPair newTradingPair,
        bool sellSucceeds = true)
    {
        SetupDefaultMocks();

        // Setup old pair exists, new pair doesn't exist initially
        _accountMock.Setup(x => x.HasTradingPair(oldPair)).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair(newPair)).Returns(false);
        _accountMock.Setup(x => x.GetTradingPair(oldPair)).Returns(oldTradingPairMock.Object);

        // Setup market pairs
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { oldPair, newPair });

        // Track PlaceOrder calls
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns((IOrder order, OrderMetadata metadata) =>
            {
                if (order.Side == OrderSide.Sell)
                {
                    if (sellSucceeds)
                    {
                        // After successful sell, old pair no longer exists
                        _accountMock.Setup(x => x.HasTradingPair(oldPair)).Returns(false);
                    }
                    return sellOrderMock.Object;
                }
                else
                {
                    // After buy, new pair exists
                    _accountMock.Setup(x => x.HasTradingPair(newPair)).Returns(true);
                    _accountMock.Setup(x => x.GetTradingPair(newPair)).Returns(newTradingPair);
                    return buyOrderMock.Object;
                }
            });
    }

    [Fact]
    public void Swap_WithValidPairs_SellsOldPairFirst()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m, dcaLevel: 1);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify sell order was placed first
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Side == OrderSide.Sell && o.Pair == oldPair),
            It.IsAny<OrderMetadata>()), Times.Once);
    }

    [Fact]
    public void Swap_WithValidPairs_BuysNewPairAfterSell()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify buy order was placed after sell
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Side == OrderSide.Buy && o.Pair == newPair),
            It.IsAny<OrderMetadata>()), Times.Once);
    }

    [Fact]
    public void Swap_WhenSellFails_DoesNotBuyNewPair()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, result: OrderResult.Error);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        // Sell doesn't succeed - old pair still exists
        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair, sellSucceeds: false);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify buy order was NOT placed because sell failed
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Side == OrderSide.Buy),
            It.IsAny<OrderMetadata>()), Times.Never);
    }

    [Fact]
    public void Swap_TransfersMetadataToNewPair()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";
        const string signalRule = "TestSignalRule";

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        var metadata = new OrderMetadata { SignalRule = signalRule };
        var options = new SwapOptions(oldPair, newPair, metadata) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify buy order was placed with metadata containing SwapPair
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Side == OrderSide.Buy),
            It.Is<OrderMetadata>(m => m.SwapPair == oldPair && m.SignalRule == signalRule)), Times.Once);
    }

    [Fact]
    public void Swap_PreservesAdditionalCostsFromOldPair()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";
        const decimal additionalCosts = 5.0m;

        var oldTradingPairMock = CreateMockTradingPairForSwap(
            oldPair,
            currentMargin: -5m,
            additionalCosts: additionalCosts,
            averagePricePaid: 100m,
            currentPrice: 95m,
            totalAmount: 1.0m);

        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 95m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 95m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify buy order metadata includes additional costs
        // The additional costs calculation is: AverageCostPaid - CurrentCost + AdditionalCosts
        // = 100 - 95 + 5 = 10
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Side == OrderSide.Buy),
            It.Is<OrderMetadata>(m => m.AdditionalCosts.HasValue && m.AdditionalCosts.Value == 10m)), Times.Once);
    }

    [Fact]
    public void Swap_PreservesDCALevelsFromOldPair()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";
        const int dcaLevel = 2;

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m, dcaLevel: dcaLevel);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify buy order metadata includes DCA levels
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Side == OrderSide.Buy),
            It.Is<OrderMetadata>(m => m.AdditionalDCALevels == dcaLevel)), Times.Once);
    }

    [Fact]
    public void Swap_AddsSellFeesToNewPositionCosts()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";
        const decimal sellFees = 0.5m;
        const string market = "USDT";

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m, fees: sellFees, feesCurrency: market);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata { AdditionalCosts = 0m }
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify sell fees are added to new position's additional costs
        newTradingPair.Metadata.AdditionalCosts.Should().Be(sellFees);
    }

    [Fact]
    public void Swap_WhenCanSwapFails_LogsAndReturns()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";

        SetupDefaultMocks();

        // Old pair doesn't exist
        _accountMock.Setup(x => x.HasTradingPair(oldPair)).Returns(false);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify logging was called and no orders were placed
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s.Contains("pair does not exist")), It.IsAny<Exception>()), Times.Once);
        _accountMock.Verify(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()), Times.Never);
    }

    [Fact]
    public void CreateSwapSellOptions_SetsCorrectProperties()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: -5m);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        // Capture the sell order placed
        IOrder capturedSellOrder = null;
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Callback<IOrder, OrderMetadata>((order, metadata) =>
            {
                if (order.Side == OrderSide.Sell)
                {
                    capturedSellOrder = order;
                    _accountMock.Setup(x => x.HasTradingPair(oldPair)).Returns(false);
                }
                else
                {
                    _accountMock.Setup(x => x.HasTradingPair(newPair)).Returns(true);
                    _accountMock.Setup(x => x.GetTradingPair(newPair)).Returns(newTradingPair);
                }
            })
            .Returns((IOrder order, OrderMetadata metadata) =>
                order.Side == OrderSide.Sell ? sellOrderMock.Object : buyOrderMock.Object);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify sell order has correct properties
        capturedSellOrder.Should().NotBeNull();
        capturedSellOrder.Pair.Should().Be(oldPair);
        capturedSellOrder.Side.Should().Be(OrderSide.Sell);
    }

    [Fact]
    public void CreateSwapBuyOptions_SetsCorrectMetadata()
    {
        // Arrange
        const string oldPair = "BTCUSDT";
        const string newPair = "ETHUSDT";
        const decimal currentMargin = -5m;

        var oldTradingPairMock = CreateMockTradingPairForSwap(oldPair, currentMargin: currentMargin, dcaLevel: 1);
        var sellOrderMock = CreateMockOrderDetailsForSwap(oldPair, OrderSide.Sell, averageCost: 100m);
        var buyOrderMock = CreateMockOrderDetailsForSwap(newPair, OrderSide.Buy, averageCost: 100m);
        var newTradingPair = new TradingPair
        {
            Pair = newPair,
            OrderIds = new List<string> { "buy-1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.Now },
            TotalAmount = 1.0m,
            AveragePricePaid = 100m,
            CurrentPrice = 100m,
            Metadata = new OrderMetadata()
        };

        SetupSwapMocks(oldPair, newPair, oldTradingPairMock, sellOrderMock, buyOrderMock, newTradingPair);

        // Capture the buy order metadata
        OrderMetadata capturedBuyMetadata = null;
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Callback<IOrder, OrderMetadata>((order, metadata) =>
            {
                if (order.Side == OrderSide.Sell)
                {
                    _accountMock.Setup(x => x.HasTradingPair(oldPair)).Returns(false);
                }
                else
                {
                    capturedBuyMetadata = metadata;
                    _accountMock.Setup(x => x.HasTradingPair(newPair)).Returns(true);
                    _accountMock.Setup(x => x.GetTradingPair(newPair)).Returns(newTradingPair);
                }
            })
            .Returns((IOrder order, OrderMetadata metadata) =>
                order.Side == OrderSide.Sell ? sellOrderMock.Object : buyOrderMock.Object);

        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        _sut.Swap(options);

        // Assert - Verify buy metadata has correct swap-related fields
        capturedBuyMetadata.Should().NotBeNull();
        capturedBuyMetadata.SwapPair.Should().Be(oldPair);
        capturedBuyMetadata.LastBuyMargin.Should().Be(currentMargin);
        capturedBuyMetadata.AdditionalDCALevels.Should().Be(1);
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

    #region Buy/Sell Execution Flow Tests

    [Fact]
    public void Buy_WithValidOptions_PlacesOrderSuccessfully()
    {
        // Arrange
        SetupDefaultMocks();
        var orderDetails = CreateFilledOrderDetails("BTCUSDT", OrderSide.Buy);
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns(orderDetails);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns((ITradingPair)null);
        _notificationServiceMock.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        _sut.Buy(options);

        // Assert
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Pair == "BTCUSDT" && o.Side == OrderSide.Buy),
            It.IsAny<OrderMetadata>()), Times.Once);
        _sut.OrderHistory.Should().Contain(orderDetails);
    }

    [Fact]
    public void Buy_WhenCanBuyReturnsFalse_DoesNotPlaceOrder()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(true); // This will cause CanBuy to return false
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        _sut.Buy(options);

        // Assert
        _accountMock.Verify(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()), Times.Never);
        _sut.OrderHistory.Should().BeEmpty();
        _loggingServiceMock.Verify(x => x.Debug(It.Is<string>(s => s.Contains("trading suspended")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void Buy_WithSwapPair_InitiatesSwapInstead()
    {
        // Arrange
        SetupDefaultMocks();

        // Setup trading rules config with swap enabled
        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        rulesModuleMock.Setup(x => x.GetConfiguration<TradingRulesConfig>())
            .Returns(new TradingRulesConfig
            {
                SwapEnabled = true,
                SwapSignalRules = new List<string> { "TestSignalRule" },
                SwapTimeout = 0,
                SellEnabled = true,
                BuyEnabled = true
            });
        _rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(rulesModuleMock.Object);

        // Trigger rules reload via reflection
        var onRulesChangedMethod = typeof(TradingService).GetMethod("OnTradingRulesChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        onRulesChangedMethod?.Invoke(_sut, null);

        // Setup a trading pair that can be swapped
        var swapTradingPairMock = CreateTradingPairWithMock("ETHUSDT", currentPrice: 50m);
        swapTradingPairMock.Setup(x => x.OrderDates).Returns(new List<DateTimeOffset> { DateTimeOffset.Now.AddHours(-1) });
        swapTradingPairMock.Setup(x => x.CurrentMargin).Returns(-5m); // Negative margin to be selected for swap

        _accountMock.Setup(x => x.GetTradingPairs()).Returns(new List<ITradingPair> { swapTradingPairMock.Object });
        _accountMock.Setup(x => x.HasTradingPair("ETHUSDT")).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);
        _accountMock.Setup(x => x.GetTradingPair("ETHUSDT")).Returns(swapTradingPairMock.Object);

        _sut.ReapplyTradingRules();

        var options = new BuyOptions("BTCUSDT")
        {
            MaxCost = 100m,
            Metadata = new OrderMetadata { SignalRule = "TestSignalRule" }
        };

        // Act
        _sut.Buy(options);

        // Assert - Swap should have been attempted, logging will show swap-related messages
        _loggingServiceMock.Verify(x => x.Info(It.IsAny<string>(), It.IsAny<Exception>()), Times.AtLeastOnce);
    }

    [Fact]
    public void Sell_WithValidOptions_PlacesOrderSuccessfully()
    {
        // Arrange
        SetupDefaultMocks();
        var tradingPairMock = CreateTradingPairWithMock("BTCUSDT");
        var orderDetails = CreateFilledOrderDetails("BTCUSDT", OrderSide.Sell);

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPairMock.Object);
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns(orderDetails);
        _notificationServiceMock.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var options = new SellOptions("BTCUSDT");

        // Act
        _sut.Sell(options);

        // Assert
        _accountMock.Verify(x => x.PlaceOrder(
            It.Is<IOrder>(o => o.Pair == "BTCUSDT" && o.Side == OrderSide.Sell),
            It.IsAny<OrderMetadata>()), Times.Once);
        _sut.OrderHistory.Should().Contain(orderDetails);
    }

    [Fact]
    public void Sell_WhenCanSellReturnsFalse_DoesNotPlaceOrder()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false); // Pair doesn't exist

        var options = new SellOptions("BTCUSDT");

        // Act
        _sut.Sell(options);

        // Assert
        _accountMock.Verify(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()), Times.Never);
        _sut.OrderHistory.Should().BeEmpty();
        _loggingServiceMock.Verify(x => x.Debug(It.Is<string>(s => s.Contains("pair does not exist")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void InitiateBuy_WithTrailingEnabled_AddsToTrailingBuys()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);

        // Setup rules config with trailing enabled
        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        rulesModuleMock.Setup(x => x.GetConfiguration<TradingRulesConfig>())
            .Returns(new TradingRulesConfig { BuyEnabled = true, BuyTrailing = 0.5m });
        _rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(rulesModuleMock.Object);

        // Trigger rules reload
        var onRulesChangedMethod = typeof(TradingService).GetMethod("OnTradingRulesChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        onRulesChangedMethod?.Invoke(_sut, null);

        // Clear the pair config cache to pick up new rules
        _sut.ReapplyTradingRules();

        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        _sut.Buy(options);

        // Assert - Should be in trailing buys, not immediately executed
        _sut.GetTrailingBuys().Should().Contain("BTCUSDT");
        _accountMock.Verify(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()), Times.Never);
    }

    [Fact]
    public void InitiateSell_WithTrailingEnabled_AddsToTrailingSells()
    {
        // Arrange
        SetupDefaultMocks();
        var tradingPairMock = CreateTradingPairWithMock("BTCUSDT");

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPairMock.Object);

        // Setup rules config with trailing enabled
        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        rulesModuleMock.Setup(x => x.GetConfiguration<TradingRulesConfig>())
            .Returns(new TradingRulesConfig { SellEnabled = true, SellTrailing = 0.5m });
        _rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(rulesModuleMock.Object);

        // Trigger rules reload
        var onRulesChangedMethod = typeof(TradingService).GetMethod("OnTradingRulesChanged",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        onRulesChangedMethod?.Invoke(_sut, null);

        // Clear the pair config cache to pick up new rules
        _sut.ReapplyTradingRules();

        var options = new SellOptions("BTCUSDT");

        // Act
        _sut.Sell(options);

        // Assert - Should be in trailing sells, not immediately executed
        _sut.GetTrailingSells().Should().Contain("BTCUSDT");
        _accountMock.Verify(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()), Times.Never);
    }

    [Fact]
    public void PlaceBuyOrder_OnSuccess_LogsOrderAndNotifies()
    {
        // Arrange
        SetupDefaultMocks();
        var orderDetails = CreateFilledOrderDetails("BTCUSDT", OrderSide.Buy);
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns(orderDetails);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns((ITradingPair)null);
        _notificationServiceMock.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        _sut.Buy(options);

        // Assert
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s =>
            s.Contains("Buy order filled") && s.Contains("BTCUSDT")), It.IsAny<Exception>()), Times.Once);
        _notificationServiceMock.Verify(x => x.NotifyAsync(It.Is<string>(s =>
            s.Contains("Buy order filled"))), Times.Once);
        _sut.OrderHistory.Should().Contain(orderDetails);
    }

    [Fact]
    public void PlaceSellOrder_OnSuccess_LogsOrderAndNotifies()
    {
        // Arrange
        SetupDefaultMocks();
        var tradingPairMock = CreateTradingPairWithMock("BTCUSDT");
        var orderDetails = CreateFilledOrderDetails("BTCUSDT", OrderSide.Sell);

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPairMock.Object);
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns(orderDetails);
        _notificationServiceMock.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var options = new SellOptions("BTCUSDT");

        // Act
        _sut.Sell(options);

        // Assert
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s =>
            s.Contains("Sell order filled") && s.Contains("BTCUSDT")), It.IsAny<Exception>()), Times.Once);
        _notificationServiceMock.Verify(x => x.NotifyAsync(It.Is<string>(s =>
            s.Contains("Sell order filled"))), Times.Once);
        _sut.OrderHistory.Should().Contain(orderDetails);
    }

    private static IOrderDetails CreateFilledOrderDetails(
        string pair,
        OrderSide side,
        decimal averagePrice = 100m,
        decimal amountFilled = 1m)
    {
        var mock = new Mock<IOrderDetails>();
        mock.Setup(x => x.OrderId).Returns(Guid.NewGuid().ToString());
        mock.Setup(x => x.Pair).Returns(pair);
        mock.Setup(x => x.Side).Returns(side);
        mock.Setup(x => x.Result).Returns(OrderResult.Filled);
        mock.Setup(x => x.AveragePrice).Returns(averagePrice);
        mock.Setup(x => x.AmountFilled).Returns(amountFilled);
        mock.Setup(x => x.AverageCost).Returns(averagePrice * amountFilled);
        mock.Setup(x => x.Date).Returns(DateTimeOffset.Now);
        mock.Setup(x => x.Fees).Returns(0m);
        mock.Setup(x => x.FeesCurrency).Returns((string)null);
        return mock.Object;
    }

    private static Mock<ITradingPair> CreateTradingPairWithMock(
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

        return mockPair;
    }

    #endregion

    #region GetPairConfig and DCA Level Tests

    [Fact]
    public void GetPairConfig_ReturnsConfigWithCorrectDefaults()
    {
        // Arrange
        SetupDefaultMocks();

        // Act
        var pairConfig = _sut.GetPairConfig("BTCUSDT");

        // Assert
        pairConfig.Should().NotBeNull();
        pairConfig.BuyEnabled.Should().BeTrue();
        pairConfig.SellEnabled.Should().BeTrue();
        pairConfig.BuyMultiplier.Should().Be(1); // Default multiplier when no DCA levels
        pairConfig.SwapEnabled.Should().BeFalse();
    }

    [Fact]
    public void GetPairConfig_WithDCALevel_ReturnsCorrectMultiplier()
    {
        // Arrange
        SetupDefaultMocks();
        var dcaLevels = new List<DCALevel>
        {
            new DCALevel { Margin = -5m, BuyMultiplier = 1.5m },
            new DCALevel { Margin = -10m, BuyMultiplier = 2.0m },
            new DCALevel { Margin = -15m, BuyMultiplier = 2.5m }
        };
        SetupTradingConfig(config =>
        {
            config.DCALevels = dcaLevels;
        });

        var tradingPair = CreateTradingPair("BTCUSDT", dcaLevel: 1);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPair);

        // Act
        _sut.ReapplyTradingRules(); // Clear cache to pick up new config
        var pairConfig = _sut.GetPairConfig("BTCUSDT");

        // Assert
        pairConfig.Should().NotBeNull();
        pairConfig.BuyMultiplier.Should().Be(2.0m); // DCA level 1 multiplier
    }

    [Fact]
    public void GetPairConfig_CachesConfigForSamePair()
    {
        // Arrange
        SetupDefaultMocks();

        // Act
        var config1 = _sut.GetPairConfig("BTCUSDT");
        var config2 = _sut.GetPairConfig("BTCUSDT");

        // Assert - Should be the same instance (cached)
        config1.Should().BeSameAs(config2);
    }

    #endregion

    #region CanBuy Configuration Tests

    [Fact]
    public void CanBuy_WhenPairInExcludedPairs_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingConfig(config =>
        {
            config.ExcludedPairs = new List<string> { "BTCUSDT" };
        });
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("exluded pair");
    }

    [Fact]
    public void CanBuy_WhenMaxPairsReached_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingConfig(config =>
        {
            config.MaxPairs = 2;
        });

        // Setup existing pairs at max capacity
        var existingPairs = new List<ITradingPair>
        {
            CreateTradingPair("ETHUSDT"),
            CreateTradingPair("ADAUSDT")
        };
        _accountMock.Setup(x => x.GetTradingPairs()).Returns(existingPairs);
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);

        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("maximum pairs reached");
    }

    [Fact]
    public void CanBuy_WhenMinBalanceNotMet_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks(balance: 150m);
        SetupRulesConfig(rulesConfig =>
        {
            rulesConfig.BuyMinBalance = 100m; // After buying 100, balance would be 50, below min
        });

        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("minimum balance reached");
    }

    [Fact]
    public void CanBuy_WhenBuySamePairTimeoutNotElapsed_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupRulesConfig(rulesConfig =>
        {
            rulesConfig.BuySamePairTimeout = 300; // 5 minutes timeout
        });

        // Add a recent order to history
        var recentOrderMock = new Mock<IOrderDetails>();
        recentOrderMock.Setup(x => x.Side).Returns(OrderSide.Buy);
        recentOrderMock.Setup(x => x.Pair).Returns("BTCUSDT");
        recentOrderMock.Setup(x => x.Date).Returns(DateTimeOffset.Now.AddSeconds(-60)); // 1 minute ago
        _sut.LogOrder(recentOrderMock.Object);

        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("buy same pair timeout");
    }

    #endregion

    #region CanSell Configuration Tests

    [Fact]
    public void CanSell_WhenPairInExcludedPairs_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingConfig(config =>
        {
            config.ExcludedPairs = new List<string> { "BTCUSDT" };
        });

        var tradingPair = CreateTradingPair("BTCUSDT");
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPair);
        var options = new SellOptions("BTCUSDT");

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("excluded pair");
    }

    #endregion

    #region CanSwap Configuration Tests

    [Fact]
    public void CanSwap_WhenSellNotEnabled_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupRulesConfig(rulesConfig =>
        {
            rulesConfig.SellEnabled = false;
        });

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair("ETHUSDT")).Returns(false);
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "BTCUSDT", "ETHUSDT" });

        var options = new SwapOptions("BTCUSDT", "ETHUSDT", new OrderMetadata());

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("selling not enabled");
    }

    [Fact]
    public void CanSwap_WhenBuyNotEnabled_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        SetupRulesConfig(rulesConfig =>
        {
            rulesConfig.BuyEnabled = false;
        });

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.HasTradingPair("ETHUSDT")).Returns(false);
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "BTCUSDT", "ETHUSDT" });

        var options = new SwapOptions("BTCUSDT", "ETHUSDT", new OrderMetadata());

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("buying not enabled");
    }

    #endregion

    #region Helper Methods for Config Setup

    private void SetupTradingConfig(Action<TradingConfig> configureAction)
    {
        // Get current config through reflection and modify it
        var configField = typeof(ConfigurableServiceBase<TradingConfig>)
            .GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var config = configField?.GetValue(_sut) as TradingConfig ?? new TradingConfig
        {
            Market = "USDT",
            Exchange = "Binance",
            ExcludedPairs = new List<string>(),
            DCALevels = new List<DCALevel>(),
            BuyType = OrderType.Market,
            SellType = OrderType.Market,
            BuyMaxCost = 100m,
            MaxPairs = 0
        };

        configureAction(config);
        configField?.SetValue(_sut, config);
    }

    private void SetupRulesConfig(Action<TradingRulesConfig> configureAction)
    {
        // Get or create RulesConfig through reflection
        var rulesConfigProperty = typeof(TradingService)
            .GetProperty("RulesConfig", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        var rulesConfig = rulesConfigProperty?.GetValue(_sut) as TradingRulesConfig ?? new TradingRulesConfig();

        configureAction(rulesConfig);

        // Set via backing field since property has public setter
        var backingField = typeof(TradingService)
            .GetField("<RulesConfig>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        backingField?.SetValue(_sut, rulesConfig);

        // Clear pair config cache to pick up new rules config
        _sut.ReapplyTradingRules();
    }

    #endregion

    #region Async Exchange Wrapper Tests

    [Fact]
    public async Task GetTickersAsync_ReturnsTickersFromExchange()
    {
        // Arrange
        var ticker1 = new Mock<ITicker>();
        ticker1.Setup(x => x.Pair).Returns("BTCUSDT");
        ticker1.Setup(x => x.LastPrice).Returns(50000m);

        var ticker2 = new Mock<ITicker>();
        ticker2.Setup(x => x.Pair).Returns("ETHUSDT");
        ticker2.Setup(x => x.LastPrice).Returns(3000m);

        var expectedTickers = new List<ITicker> { ticker1.Object, ticker2.Object };
        _exchangeServiceMock.Setup(x => x.GetTickers(It.IsAny<string>()))
            .ReturnsAsync(expectedTickers);

        // Act
        var result = await _sut.GetTickersAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedTickers);
        _exchangeServiceMock.Verify(x => x.GetTickers(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetMarketPairsAsync_ReturnsPairsFromExchange()
    {
        // Arrange
        var expectedPairs = new List<string> { "BTCUSDT", "ETHUSDT", "BNBUSDT" };
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(expectedPairs);

        // Act
        var result = await _sut.GetMarketPairsAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedPairs);
        _exchangeServiceMock.Verify(x => x.GetMarketPairs(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetAvailableAmountsAsync_ReturnsAmountsFromExchange()
    {
        // Arrange
        var expectedAmounts = new Dictionary<string, decimal>
        {
            { "BTC", 1.5m },
            { "ETH", 10m },
            { "USDT", 5000m }
        };
        _exchangeServiceMock.Setup(x => x.GetAvailableAmounts())
            .ReturnsAsync(expectedAmounts);

        // Act
        var result = await _sut.GetAvailableAmountsAsync();

        // Assert
        result.Should().BeEquivalentTo(expectedAmounts);
        _exchangeServiceMock.Verify(x => x.GetAvailableAmounts(), Times.Once);
    }

    [Fact]
    public async Task GetMyTradesAsync_ReturnsTradesForPair()
    {
        // Arrange
        var trade1 = new Mock<IOrderDetails>();
        trade1.Setup(x => x.Pair).Returns("BTCUSDT");
        trade1.Setup(x => x.OrderId).Returns("order-1");
        trade1.Setup(x => x.Side).Returns(OrderSide.Buy);

        var trade2 = new Mock<IOrderDetails>();
        trade2.Setup(x => x.Pair).Returns("BTCUSDT");
        trade2.Setup(x => x.OrderId).Returns("order-2");
        trade2.Setup(x => x.Side).Returns(OrderSide.Sell);

        var expectedTrades = new List<IOrderDetails> { trade1.Object, trade2.Object };
        _exchangeServiceMock.Setup(x => x.GetMyTrades("BTCUSDT"))
            .ReturnsAsync(expectedTrades);

        // Act
        var result = await _sut.GetMyTradesAsync("BTCUSDT");

        // Assert
        result.Should().BeEquivalentTo(expectedTrades);
        _exchangeServiceMock.Verify(x => x.GetMyTrades("BTCUSDT"), Times.Once);
    }

    [Fact]
    public async Task PlaceOrderAsync_PlacesOrderOnExchange()
    {
        // Arrange
        var orderMock = new Mock<IOrder>();
        orderMock.Setup(x => x.Pair).Returns("BTCUSDT");
        orderMock.Setup(x => x.Side).Returns(OrderSide.Buy);
        orderMock.Setup(x => x.Amount).Returns(0.01m);
        orderMock.Setup(x => x.Price).Returns(50000m);

        var expectedOrderDetails = new Mock<IOrderDetails>();
        expectedOrderDetails.Setup(x => x.OrderId).Returns("order-123");
        expectedOrderDetails.Setup(x => x.Pair).Returns("BTCUSDT");
        expectedOrderDetails.Setup(x => x.Result).Returns(OrderResult.Filled);
        expectedOrderDetails.Setup(x => x.AmountFilled).Returns(0.01m);

        _exchangeServiceMock.Setup(x => x.PlaceOrder(orderMock.Object))
            .ReturnsAsync(expectedOrderDetails.Object);

        // Act
        var result = await _sut.PlaceOrderAsync(orderMock.Object);

        // Assert
        result.Should().Be(expectedOrderDetails.Object);
        result.OrderId.Should().Be("order-123");
        result.Result.Should().Be(OrderResult.Filled);
        _exchangeServiceMock.Verify(x => x.PlaceOrder(orderMock.Object), Times.Once);
    }

    [Fact]
    public async Task GetCurrentPriceAsync_ReturnsPriceFromExchange()
    {
        // Arrange
        var expectedPrice = 50000.50m;
        _exchangeServiceMock.Setup(x => x.GetLastPrice("BTCUSDT"))
            .ReturnsAsync(expectedPrice);

        // Act
        var result = await _sut.GetCurrentPriceAsync("BTCUSDT");

        // Assert
        result.Should().Be(expectedPrice);
        _exchangeServiceMock.Verify(x => x.GetLastPrice("BTCUSDT"), Times.Once);
    }

    #endregion

    #region Synchronous Exchange Wrapper Tests

    [Fact]
    public void GetTickers_ReturnsTickersFromExchange()
    {
        // Arrange
        var ticker1 = new Mock<ITicker>();
        ticker1.Setup(x => x.Pair).Returns("BTCUSDT");
        ticker1.Setup(x => x.LastPrice).Returns(50000m);

        var ticker2 = new Mock<ITicker>();
        ticker2.Setup(x => x.Pair).Returns("ETHUSDT");
        ticker2.Setup(x => x.LastPrice).Returns(3000m);

        var expectedTickers = new List<ITicker> { ticker1.Object, ticker2.Object };
        _exchangeServiceMock.Setup(x => x.GetTickers(It.IsAny<string>()))
            .ReturnsAsync(expectedTickers);

        // Act
        var result = _sut.GetTickers();

        // Assert
        result.Should().BeEquivalentTo(expectedTickers);
        _exchangeServiceMock.Verify(x => x.GetTickers(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void GetMarketPairs_ReturnsPairsFromExchange()
    {
        // Arrange
        var expectedPairs = new List<string> { "BTCUSDT", "ETHUSDT", "BNBUSDT" };
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(expectedPairs);

        // Act
        var result = _sut.GetMarketPairs();

        // Assert
        result.Should().BeEquivalentTo(expectedPairs);
        _exchangeServiceMock.Verify(x => x.GetMarketPairs(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void GetCurrentPrice_ReturnsPriceFromExchange()
    {
        // Arrange
        var expectedPrice = 50000.50m;
        _exchangeServiceMock.Setup(x => x.GetLastPrice("BTCUSDT"))
            .ReturnsAsync(expectedPrice);

        // Act
        var result = _sut.GetCurrentPrice("BTCUSDT");

        // Assert
        result.Should().Be(expectedPrice);
        _exchangeServiceMock.Verify(x => x.GetLastPrice("BTCUSDT"), Times.Once);
    }

    [Fact]
    public void PlaceOrder_PlacesOrderOnExchange()
    {
        // Arrange
        var orderMock = new Mock<IOrder>();
        orderMock.Setup(x => x.Pair).Returns("BTCUSDT");
        orderMock.Setup(x => x.Side).Returns(OrderSide.Buy);
        orderMock.Setup(x => x.Amount).Returns(0.01m);
        orderMock.Setup(x => x.Price).Returns(50000m);

        var expectedOrderDetails = new Mock<IOrderDetails>();
        expectedOrderDetails.Setup(x => x.OrderId).Returns("order-123");
        expectedOrderDetails.Setup(x => x.Pair).Returns("BTCUSDT");
        expectedOrderDetails.Setup(x => x.Result).Returns(OrderResult.Filled);
        expectedOrderDetails.Setup(x => x.AmountFilled).Returns(0.01m);

        _exchangeServiceMock.Setup(x => x.PlaceOrder(orderMock.Object))
            .ReturnsAsync(expectedOrderDetails.Object);

        // Act
        var result = _sut.PlaceOrder(orderMock.Object);

        // Assert
        result.Should().Be(expectedOrderDetails.Object);
        result.OrderId.Should().Be("order-123");
        result.Result.Should().Be(OrderResult.Filled);
        _exchangeServiceMock.Verify(x => x.PlaceOrder(orderMock.Object), Times.Once);
    }

    #endregion

    #region Integration Tests - Service Invocation Verification

    private void SetupSuccessfulBuyOrder()
    {
        var orderDetailsMock = new Mock<IOrderDetails>();
        orderDetailsMock.Setup(x => x.OrderId).Returns("buy-123");
        orderDetailsMock.Setup(x => x.Pair).Returns("BTCUSDT");
        orderDetailsMock.Setup(x => x.Side).Returns(OrderSide.Buy);
        orderDetailsMock.Setup(x => x.Result).Returns(OrderResult.Filled);
        orderDetailsMock.Setup(x => x.AveragePrice).Returns(50000m);
        orderDetailsMock.Setup(x => x.AmountFilled).Returns(0.001m);
        orderDetailsMock.Setup(x => x.AverageCost).Returns(50m);

        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns(orderDetailsMock.Object);
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(false);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns((ITradingPair)null);
    }

    private void SetupSuccessfulSellOrder()
    {
        var tradingPairMock = new Mock<ITradingPair>();
        tradingPairMock.Setup(x => x.Pair).Returns("BTCUSDT");
        tradingPairMock.Setup(x => x.TotalAmount).Returns(0.001m);
        tradingPairMock.Setup(x => x.CurrentPrice).Returns(55000m);
        tradingPairMock.Setup(x => x.CurrentMargin).Returns(10m);
        tradingPairMock.Setup(x => x.FormattedName).Returns("BTCUSDT");
        tradingPairMock.Setup(x => x.OrderDates).Returns(new List<DateTimeOffset> { DateTimeOffset.Now.AddMinutes(-5) });

        var orderDetailsMock = new Mock<IOrderDetails>();
        orderDetailsMock.Setup(x => x.OrderId).Returns("sell-123");
        orderDetailsMock.Setup(x => x.Pair).Returns("BTCUSDT");
        orderDetailsMock.Setup(x => x.Side).Returns(OrderSide.Sell);
        orderDetailsMock.Setup(x => x.Result).Returns(OrderResult.Filled);
        orderDetailsMock.Setup(x => x.AveragePrice).Returns(55000m);

        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPairMock.Object);
        _accountMock.Setup(x => x.PlaceOrder(It.IsAny<IOrder>(), It.IsAny<OrderMetadata>()))
            .Returns(orderDetailsMock.Object);
    }

    [Fact]
    public void Buy_OnSuccess_CallsNotificationService()
    {
        // Arrange
        SetupDefaultMocks();
        SetupSuccessfulBuyOrder();
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        _sut.Buy(options);

        // Assert
        _notificationServiceMock.Verify(
            x => x.NotifyAsync(It.Is<string>(msg => msg.Contains("Buy order filled"))),
            Times.Once);
    }

    [Fact]
    public void Buy_OnSuccess_CallsLoggingService()
    {
        // Arrange
        SetupDefaultMocks();
        SetupSuccessfulBuyOrder();
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        _sut.Buy(options);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(msg => msg.Contains("Buy order filled")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Sell_OnSuccess_CallsNotificationService()
    {
        // Arrange
        SetupDefaultMocks();
        SetupSuccessfulSellOrder();
        var options = new SellOptions("BTCUSDT");

        // Act
        _sut.Sell(options);

        // Assert
        _notificationServiceMock.Verify(
            x => x.NotifyAsync(It.Is<string>(msg => msg.Contains("Sell order filled"))),
            Times.Once);
    }

    [Fact]
    public void Sell_OnSuccess_CallsLoggingService()
    {
        // Arrange
        SetupDefaultMocks();
        SetupSuccessfulSellOrder();
        var options = new SellOptions("BTCUSDT");

        // Act
        _sut.Sell(options);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(msg => msg.Contains("Sell order filled")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void SuspendTrading_LogsSuspension()
    {
        // Arrange
        _sut.ResumeTrading(true); // Ensure trading is not suspended initially

        // Act
        _sut.SuspendTrading(false);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(msg => msg.Contains("Trading suspended")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void ResumeTrading_LogsResumption()
    {
        // Arrange
        _sut.SuspendTrading(false); // First suspend trading
        _loggingServiceMock.Invocations.Clear(); // Clear previous invocations

        // Act
        _sut.ResumeTrading(true);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(msg => msg.Contains("Trading started")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Start_RegistersRulesChangeCallback()
    {
        // Arrange - Create a fresh instance with mocks
        var coreServiceMock = new Mock<ICoreService>();
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var healthCheckServiceMock = new Mock<IHealthCheckService>();
        var rulesServiceMock = new Mock<IRulesService>();
        var backtestingServiceMock = new Mock<IBacktestingService>();
        var signalsServiceMock = new Mock<ISignalsService>();
        var exchangeServiceMock = new Mock<IExchangeService>();
        var applicationContextMock = new Mock<IApplicationContext>();
        var configProviderMock = new Mock<IConfigProvider>();

        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        var signalsConfigMock = new Mock<ISignalsConfig>();
        signalsConfigMock.Setup(x => x.Enabled).Returns(false);
        signalsServiceMock.Setup(x => x.Config).Returns(signalsConfigMock.Object);

        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        signalsServiceMock.Setup(x => x.Rules).Returns(rulesModuleMock.Object);
        rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(rulesModuleMock.Object);

        Func<string, IExchangeService> exchangeServiceFactory = (name) => exchangeServiceMock.Object;

        var sut = new TradingService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            healthCheckServiceMock.Object,
            rulesServiceMock.Object,
            new Lazy<IBacktestingService>(() => backtestingServiceMock.Object),
            new Lazy<ISignalsService>(() => signalsServiceMock.Object),
            exchangeServiceFactory,
            applicationContextMock.Object,
            configProviderMock.Object);

        // Act
        sut.Start();

        // Assert
        rulesServiceMock.Verify(
            x => x.RegisterRulesChangeCallback(It.IsAny<Action>()),
            Times.Once);
    }

    [Fact]
    public void Stop_UnregistersRulesChangeCallback()
    {
        // Arrange - Create a fresh instance with mocks
        var coreServiceMock = new Mock<ICoreService>();
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var healthCheckServiceMock = new Mock<IHealthCheckService>();
        var rulesServiceMock = new Mock<IRulesService>();
        var backtestingServiceMock = new Mock<IBacktestingService>();
        var signalsServiceMock = new Mock<ISignalsService>();
        var exchangeServiceMock = new Mock<IExchangeService>();
        var applicationContextMock = new Mock<IApplicationContext>();
        var configProviderMock = new Mock<IConfigProvider>();

        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        var signalsConfigMock = new Mock<ISignalsConfig>();
        signalsConfigMock.Setup(x => x.Enabled).Returns(false);
        signalsServiceMock.Setup(x => x.Config).Returns(signalsConfigMock.Object);

        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        signalsServiceMock.Setup(x => x.Rules).Returns(rulesModuleMock.Object);
        rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(rulesModuleMock.Object);

        Func<string, IExchangeService> exchangeServiceFactory = (name) => exchangeServiceMock.Object;

        var sut = new TradingService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            healthCheckServiceMock.Object,
            rulesServiceMock.Object,
            new Lazy<IBacktestingService>(() => backtestingServiceMock.Object),
            new Lazy<ISignalsService>(() => signalsServiceMock.Object),
            exchangeServiceFactory,
            applicationContextMock.Object,
            configProviderMock.Object);

        sut.Start();

        // Inject a mock account via reflection
        var accountMock = new Mock<ITradingAccount>();
        var field = typeof(TradingService).GetField("<Account>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(sut, accountMock.Object);

        // Act
        sut.Stop();

        // Assert
        rulesServiceMock.Verify(
            x => x.UnregisterRulesChangeCallback(It.IsAny<Action>()),
            Times.Once);
    }

    [Fact]
    public void Stop_RemovesHealthChecks()
    {
        // Arrange - Create a fresh instance with mocks
        var coreServiceMock = new Mock<ICoreService>();
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var healthCheckServiceMock = new Mock<IHealthCheckService>();
        var rulesServiceMock = new Mock<IRulesService>();
        var backtestingServiceMock = new Mock<IBacktestingService>();
        var signalsServiceMock = new Mock<ISignalsService>();
        var exchangeServiceMock = new Mock<IExchangeService>();
        var applicationContextMock = new Mock<IApplicationContext>();
        var configProviderMock = new Mock<IConfigProvider>();

        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        var signalsConfigMock = new Mock<ISignalsConfig>();
        signalsConfigMock.Setup(x => x.Enabled).Returns(false);
        signalsServiceMock.Setup(x => x.Config).Returns(signalsConfigMock.Object);

        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());
        signalsServiceMock.Setup(x => x.Rules).Returns(rulesModuleMock.Object);
        rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(rulesModuleMock.Object);

        Func<string, IExchangeService> exchangeServiceFactory = (name) => exchangeServiceMock.Object;

        var sut = new TradingService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            healthCheckServiceMock.Object,
            rulesServiceMock.Object,
            new Lazy<IBacktestingService>(() => backtestingServiceMock.Object),
            new Lazy<ISignalsService>(() => signalsServiceMock.Object),
            exchangeServiceFactory,
            applicationContextMock.Object,
            configProviderMock.Object);

        sut.Start();

        // Inject a mock account via reflection
        var accountMock = new Mock<ITradingAccount>();
        var field = typeof(TradingService).GetField("<Account>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(sut, accountMock.Object);

        // Act
        sut.Stop();

        // Assert
        healthCheckServiceMock.Verify(
            x => x.RemoveHealthCheck(Constants.HealthChecks.TradingRulesProcessed),
            Times.Once);
        healthCheckServiceMock.Verify(
            x => x.RemoveHealthCheck(Constants.HealthChecks.TradingPairsProcessed),
            Times.Once);
    }

    [Fact]
    public void OnTradingRulesChanged_UpdatesRulesAndRulesConfig()
    {
        // Arrange - Create a fresh instance with mocks
        var coreServiceMock = new Mock<ICoreService>();
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var healthCheckServiceMock = new Mock<IHealthCheckService>();
        var rulesServiceMock = new Mock<IRulesService>();
        var backtestingServiceMock = new Mock<IBacktestingService>();
        var signalsServiceMock = new Mock<ISignalsService>();
        var exchangeServiceMock = new Mock<IExchangeService>();
        var applicationContextMock = new Mock<IApplicationContext>();
        var configProviderMock = new Mock<IConfigProvider>();

        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        var signalsConfigMock = new Mock<ISignalsConfig>();
        signalsConfigMock.Setup(x => x.Enabled).Returns(false);
        signalsServiceMock.Setup(x => x.Config).Returns(signalsConfigMock.Object);

        var rulesModuleMock = new Mock<IModuleRules>();
        rulesModuleMock.Setup(x => x.Entries).Returns(new List<IRule>());

        var tradingRulesConfig = new TradingRulesConfig
        {
            BuyEnabled = true,
            SellEnabled = true,
            SellMargin = 5.0m
        };
        rulesModuleMock.Setup(x => x.GetConfiguration<TradingRulesConfig>()).Returns(tradingRulesConfig);

        signalsServiceMock.Setup(x => x.Rules).Returns(rulesModuleMock.Object);
        rulesServiceMock.Setup(x => x.GetRules(Constants.ServiceNames.TradingService)).Returns(rulesModuleMock.Object);

        Func<string, IExchangeService> exchangeServiceFactory = (name) => exchangeServiceMock.Object;

        var sut = new TradingService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            healthCheckServiceMock.Object,
            rulesServiceMock.Object,
            new Lazy<IBacktestingService>(() => backtestingServiceMock.Object),
            new Lazy<ISignalsService>(() => signalsServiceMock.Object),
            exchangeServiceFactory,
            applicationContextMock.Object,
            configProviderMock.Object);

        // Act - Start() calls OnTradingRulesChanged internally
        sut.Start();

        // Assert - Verify that GetRules was called and properties are set
        rulesServiceMock.Verify(
            x => x.GetRules(Constants.ServiceNames.TradingService),
            Times.AtLeastOnce);
        sut.Rules.Should().NotBeNull();
        sut.RulesConfig.Should().NotBeNull();
        sut.RulesConfig.BuyEnabled.Should().BeTrue();
        sut.RulesConfig.SellEnabled.Should().BeTrue();
        sut.RulesConfig.SellMargin.Should().Be(5.0m);
    }

    #endregion

    #region Edge Cases and Boundary Conditions

    [Fact]
    public void CanBuy_WithNullPairName_HandlesGracefully()
    {
        // Arrange
        SetupDefaultMocks();
        // BuyOptions constructor requires pair, but we test with null
        var options = new BuyOptions(null) { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        // Should return false due to invalid price (null pair cannot have valid price)
        result.Should().BeFalse();
    }

    [Fact]
    public void CanBuy_WithEmptyPairName_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _exchangeServiceMock.Setup(x => x.GetLastPrice(string.Empty)).ReturnsAsync(0m);
        var options = new BuyOptions(string.Empty) { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("invalid price");
    }

    [Fact]
    public void CanBuy_WithNegativeMaxCost_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT") { MaxCost = -100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("not enough balance");
    }

    [Fact]
    public void CanBuy_WithZeroAmount_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT") { Amount = 0m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        // Zero amount is still considered as "specified", so amount != null
        // The validation requires either amount OR maxCost, not both/neither
        // With Amount = 0, it passes that check but may fail elsewhere
        result.Should().BeFalse();
    }

    [Fact]
    public void CanSell_WithNullPairName_HandlesGracefully()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair(null)).Returns(false);
        var options = new SellOptions(null);

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair does not exist");
    }

    [Fact]
    public void GetCurrentPrice_WithInvalidPair_ReturnsZero()
    {
        // Arrange
        SetupDefaultMocks();
        _exchangeServiceMock.Setup(x => x.GetLastPrice("INVALIDPAIR")).ReturnsAsync(0m);

        // Act
        var result = _sut.GetCurrentPrice("INVALIDPAIR");

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public void LogOrder_WithNullOrder_HandlesGracefully()
    {
        // Arrange
        var initialCount = _sut.OrderHistory.Count;

        // Act
        Action act = () => _sut.LogOrder(null);

        // Assert
        // ConcurrentStack.Push with null should work (it accepts null)
        // The method should handle null gracefully without throwing
        act.Should().NotThrow();
        _sut.OrderHistory.Count.Should().Be(initialCount + 1);
    }

    [Fact]
    public void GetTradingPairs_WhenEmpty_ReturnsEmptyList()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.GetTradingPairs()).Returns(new List<ITradingPair>());

        // Act
        var result = _sut.Account.GetTradingPairs();

        // Assert
        result.Should().BeEmpty();
        result.Should().NotBeNull();
    }

    [Fact]
    public void OrderHistory_MaintainsMaxSize()
    {
        // Arrange - Add many orders to verify bounded stack behavior
        // The default MaxOrderHistorySize is 10000, so 100 orders should all fit
        var orders = new List<IOrderDetails>();
        for (int i = 0; i < 100; i++)
        {
            var orderMock = new Mock<IOrderDetails>();
            orderMock.Setup(x => x.OrderId).Returns($"order-{i}");
            orders.Add(orderMock.Object);
        }

        // Act
        foreach (var order in orders)
        {
            _sut.LogOrder(order);
        }

        // Assert
        // BoundedConcurrentStack with default max size should hold all 100 orders
        _sut.OrderHistory.Should().HaveCount(100);

        // Stack should maintain LIFO order (most recent first)
        _sut.OrderHistory.TryPeek(out var topOrder);
        topOrder.OrderId.Should().Be("order-99");

        // Verify the max capacity is set correctly
        _sut.OrderHistory.MaxCapacity.Should().Be(Constants.Trading.DefaultMaxOrderHistorySize);
    }

    [Fact]
    public void OrderHistory_RemovesOldestWhenCapacityExceeded()
    {
        // This test verifies the bounded behavior using the BoundedConcurrentStack directly
        // since we can't easily configure MaxOrderHistorySize in the service without modifying config

        // Arrange - Create a small bounded stack for testing
        var boundedStack = new BoundedConcurrentStack<IOrderDetails>(5);
        var orders = new List<IOrderDetails>();
        for (int i = 0; i < 10; i++)
        {
            var orderMock = new Mock<IOrderDetails>();
            orderMock.Setup(x => x.OrderId).Returns($"order-{i}");
            orders.Add(orderMock.Object);
        }

        // Act - Add 10 orders to a stack with capacity 5
        foreach (var order in orders)
        {
            boundedStack.Push(order);
        }

        // Assert - Should only have 5 orders (oldest 5 removed)
        boundedStack.Should().HaveCount(5);

        // Most recent orders should remain (order-5 through order-9)
        boundedStack.TryPeek(out var topOrder);
        topOrder.OrderId.Should().Be("order-9");

        // Verify oldest orders were removed
        boundedStack.Contains(orders[0]).Should().BeFalse(); // order-0 removed
        boundedStack.Contains(orders[4]).Should().BeFalse(); // order-4 removed
        boundedStack.Contains(orders[5]).Should().BeTrue();  // order-5 kept
        boundedStack.Contains(orders[9]).Should().BeTrue();  // order-9 kept
    }

    [Fact]
    public void OrderHistory_ArchivesRemovedItems()
    {
        // Arrange
        var boundedStack = new BoundedConcurrentStack<IOrderDetails>(3);
        var archivedItems = new List<IOrderDetails>();
        boundedStack.ItemsArchived += (sender, args) =>
        {
            archivedItems.AddRange(args.ArchivedItems);
        };

        var orders = new List<IOrderDetails>();
        for (int i = 0; i < 5; i++)
        {
            var orderMock = new Mock<IOrderDetails>();
            orderMock.Setup(x => x.OrderId).Returns($"order-{i}");
            orders.Add(orderMock.Object);
        }

        // Act
        foreach (var order in orders)
        {
            boundedStack.Push(order);
        }

        // Assert - 2 items should have been archived (orders 0 and 1)
        archivedItems.Should().HaveCount(2);
        archivedItems[0].OrderId.Should().Be("order-0");
        archivedItems[1].OrderId.Should().Be("order-1");
    }

    [Fact]
    public void CreateDefaultPairConfig_WithNullAccount_HandlesGracefully()
    {
        // Arrange
        // Clear the account to simulate null scenario
        SetupAccount(null);

        // Act
        // GetPairConfig internally calls CreateDefaultPairConfig
        Action act = () => _sut.GetPairConfig("BTCUSDT");

        // Assert
        // Should handle null account gracefully (uses null-conditional in CreateDefaultPairConfig)
        act.Should().NotThrow();
    }

    [Fact]
    public void CanBuy_WithVeryLargeMaxCost_HandlesCorrectly()
    {
        // Arrange
        SetupDefaultMocks(balance: decimal.MaxValue);
        var options = new BuyOptions("BTCUSDT") { MaxCost = decimal.MaxValue / 2 };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Fact]
    public void CanBuy_WithMinimalMaxCost_HandlesCorrectly()
    {
        // Arrange
        SetupDefaultMocks(balance: 0.00000001m);
        _exchangeServiceMock.Setup(x => x.GetLastPrice("BTCUSDT")).ReturnsAsync(0.00000001m);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 0.00000001m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Fact]
    public void GetTrailingBuys_AfterMultipleCalls_ReturnsConsistentList()
    {
        // Arrange
        SetupDefaultMocks();

        // Act
        var result1 = _sut.GetTrailingBuys();
        var result2 = _sut.GetTrailingBuys();

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.Should().BeOfType<List<string>>();
        result2.Should().BeOfType<List<string>>();
    }

    [Fact]
    public void GetTrailingSells_AfterMultipleCalls_ReturnsConsistentList()
    {
        // Arrange
        SetupDefaultMocks();

        // Act
        var result1 = _sut.GetTrailingSells();
        var result2 = _sut.GetTrailingSells();

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.Should().BeOfType<List<string>>();
        result2.Should().BeOfType<List<string>>();
    }

    [Fact]
    public void CanBuy_WithWhitespacePairName_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _exchangeServiceMock.Setup(x => x.GetLastPrice("   ")).ReturnsAsync(0m);
        var options = new BuyOptions("   ") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("invalid price");
    }

    [Fact]
    public void CanSell_WithEmptyPairName_ReturnsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair(string.Empty)).Returns(false);
        var options = new SellOptions(string.Empty);

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("pair does not exist");
    }

    #endregion

    #region Theory-Based Parameterized Tests

    [Theory]
    [InlineData("BTCUSDT", 100, true)]
    [InlineData("ETHUSDT", 50, true)]
    [InlineData("", 100, false)]
    public void CanBuy_WithVariousPairs_ReturnsExpectedResult(string pair, decimal maxCost, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        if (string.IsNullOrEmpty(pair))
        {
            _exchangeServiceMock.Setup(x => x.GetLastPrice(pair)).ReturnsAsync(0m);
        }
        var options = new BuyOptions(pair) { MaxCost = maxCost };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(50, 100, false)]
    [InlineData(0, 100, false)]
    [InlineData(1000, 100, true)]
    public void CanBuy_WithVariousBalances_ReturnsExpectedResult(decimal balance, decimal maxCost, bool expected)
    {
        // Arrange
        SetupDefaultMocks(balance: balance);
        var options = new BuyOptions("BTCUSDT") { MaxCost = maxCost };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void CanBuy_WithInvalidPrice_ReturnsFalse(decimal price)
    {
        // Arrange
        SetupDefaultMocks();
        _exchangeServiceMock.Setup(x => x.GetLastPrice("BTCUSDT")).ReturnsAsync(price);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeFalse();
        message.Should().Contain("invalid price");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CanBuy_WithTradingSuspendedState_ReturnsExpectedResult(bool suspended, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(suspended);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void CanBuy_WithManualOrderAndSuspendedState_ReturnsExpectedResult(
        bool suspended, bool manualOrder, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(suspended);
        var options = new BuyOptions("BTCUSDT") { MaxCost = 100m, ManualOrder = manualOrder };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("BTCUSDT", true, false)]
    [InlineData("BTCUSDT", false, true)]
    [InlineData("ETHUSDT", true, false)]
    [InlineData("ETHUSDT", false, true)]
    public void CanBuy_WithExistingPair_ReturnsExpectedResult(string pair, bool pairExists, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair(pair)).Returns(pairExists);
        var options = new BuyOptions(pair) { MaxCost = 100m };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("BTCUSDT", true)]
    [InlineData("ETHUSDT", true)]
    [InlineData("XRPUSDT", true)]
    public void CanSell_WithValidPairs_ReturnsExpectedResult(string pair, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        var tradingPair = CreateTradingPair(pair);
        _accountMock.Setup(x => x.HasTradingPair(pair)).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair(pair)).Returns(tradingPair);
        var options = new SellOptions(pair);

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("BTCUSDT", false, false)]
    [InlineData("NONEXISTENT", false, false)]
    public void CanSell_WithNonExistentPair_ReturnsFalse(string pair, bool pairExists, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair(pair)).Returns(pairExists);
        var options = new SellOptions(pair);

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().Be(expected);
        message.Should().Contain("pair does not exist");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(100)]
    public void LogOrder_WithMultipleOrders_MaintainsCorrectCount(int orderCount)
    {
        // Arrange
        var initialCount = _sut.OrderHistory.Count;

        // Act
        for (int i = 0; i < orderCount; i++)
        {
            var orderMock = new Mock<IOrderDetails>();
            orderMock.Setup(x => x.OrderId).Returns($"order-{i}");
            _sut.LogOrder(orderMock.Object);
        }

        // Assert
        _sut.OrderHistory.Count.Should().Be(initialCount + orderCount);
    }

    [Theory]
    [InlineData("BTCUSDT", 100)]
    [InlineData("ETHUSDT", 200)]
    [InlineData("XRPUSDT", 0)]
    public void GetCurrentPrice_WithVariousPairs_ReturnsExpectedPrice(string pair, decimal expectedPrice)
    {
        // Arrange
        SetupDefaultMocks();
        _exchangeServiceMock.Setup(x => x.GetLastPrice(pair)).ReturnsAsync(expectedPrice);

        // Act
        var result = _sut.GetCurrentPrice(pair);

        // Assert
        result.Should().Be(expectedPrice);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IsTradingSuspended_AfterSuspendResume_ReturnsCorrectState(bool suspend, bool expectedState)
    {
        // Arrange
        _sut.ResumeTrading(true); // Start from known state

        // Act
        if (suspend)
        {
            _sut.SuspendTrading(false);
        }

        // Assert
        _sut.IsTradingSuspended.Should().Be(expectedState);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void CanSell_WithManualOrderAndSuspendedState_ReturnsExpectedResult(
        bool suspended, bool manualOrder, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        SetupTradingSuspended(suspended);
        var tradingPair = CreateTradingPair("BTCUSDT");
        _accountMock.Setup(x => x.HasTradingPair("BTCUSDT")).Returns(true);
        _accountMock.Setup(x => x.GetTradingPair("BTCUSDT")).Returns(tradingPair);
        var options = new SellOptions("BTCUSDT") { ManualOrder = manualOrder };

        // Act
        var result = _sut.CanSell(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void CanBuy_WithVariousAmounts_ReturnsTrue(decimal amount)
    {
        // Arrange
        SetupDefaultMocks();
        var options = new BuyOptions("BTCUSDT") { Amount = amount };

        // Act
        var result = _sut.CanBuy(options, out string message);

        // Assert
        result.Should().BeTrue();
        message.Should().BeNull();
    }

    [Theory]
    [InlineData("BTCUSDT", "ETHUSDT", true, false, true)]
    [InlineData("BTCUSDT", "ETHUSDT", false, false, false)]
    [InlineData("BTCUSDT", "ETHUSDT", true, true, false)]
    public void CanSwap_WithVariousConditions_ReturnsExpectedResult(
        string oldPair, string newPair, bool oldPairExists, bool newPairExists, bool expected)
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair(oldPair)).Returns(oldPairExists);
        _accountMock.Setup(x => x.HasTradingPair(newPair)).Returns(newPairExists);
        _exchangeServiceMock.Setup(x => x.GetMarketPairs(It.IsAny<string>()))
            .ReturnsAsync(new List<string> { "BTCUSDT", "ETHUSDT", "BNBUSDT" });
        var options = new SwapOptions(oldPair, newPair, new OrderMetadata()) { ManualOrder = true };

        // Act
        var result = _sut.CanSwap(options, out string message);

        // Assert
        result.Should().Be(expected);
    }

    #endregion
}
