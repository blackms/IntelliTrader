using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using System.Collections.Concurrent;

namespace IntelliTrader.Exchange.Tests;

/// <summary>
/// Testable implementation of ExchangeService that allows injection of test data
/// without requiring real exchange API connections.
/// </summary>
public class TestableExchangeService : ExchangeService
{
    private readonly ConcurrentDictionary<string, Ticker> _tickers = new();
    private readonly Dictionary<string, decimal> _availableAmounts = new();
    private readonly List<IOrderDetails> _myTrades = new();
    private readonly Func<IOrder, IOrderDetails>? _placeOrderHandler;
    private bool _isStarted;

    public TestableExchangeService(
        ILoggingService loggingService,
        IHealthCheckService healthCheckService,
        ICoreService coreService,
        Func<IOrder, IOrderDetails>? placeOrderHandler = null)
        : base(loggingService, healthCheckService, coreService, new Mock<IConfigProvider>().Object)
    {
        _placeOrderHandler = placeOrderHandler;
    }

    public bool IsStarted => _isStarted;
    public bool VirtualTradingEnabled { get; private set; }

    public void SetTickers(IEnumerable<Ticker> tickers)
    {
        _tickers.Clear();
        foreach (var ticker in tickers)
        {
            _tickers[ticker.Pair] = ticker;
        }
    }

    public void AddTicker(Ticker ticker)
    {
        _tickers[ticker.Pair] = ticker;
    }

    public void SetAvailableAmounts(Dictionary<string, decimal> amounts)
    {
        _availableAmounts.Clear();
        foreach (var kvp in amounts)
        {
            _availableAmounts[kvp.Key] = kvp.Value;
        }
    }

    public void SetMyTrades(List<IOrderDetails> trades)
    {
        _myTrades.Clear();
        _myTrades.AddRange(trades);
    }

    public override void Start(bool virtualTrading)
    {
        VirtualTradingEnabled = virtualTrading;
        _isStarted = true;
        LoggingService.Info("TestableExchangeService started");
    }

    public override void Stop()
    {
        _isStarted = false;
        _tickers.Clear();
        LoggingService.Info("TestableExchangeService stopped");
    }

    public override Task<IEnumerable<ITicker>> GetTickers(string market)
    {
        var result = _tickers.Values
            .Where(t => t.Pair.EndsWith(market))
            .Cast<ITicker>();
        return Task.FromResult(result);
    }

    public override Task<IEnumerable<string>> GetMarketPairs(string market)
    {
        var result = _tickers.Keys.Where(t => t.EndsWith(market));
        return Task.FromResult(result);
    }

    public override Task<Dictionary<string, decimal>> GetAvailableAmounts()
    {
        return Task.FromResult(new Dictionary<string, decimal>(_availableAmounts));
    }

    public override Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair)
    {
        var result = _myTrades.Where(t => t.Pair == pair);
        return Task.FromResult(result);
    }

    public override Task<decimal> GetLastPrice(string pair)
    {
        if (_tickers.TryGetValue(pair, out var ticker))
        {
            return Task.FromResult(ticker.LastPrice);
        }
        return Task.FromResult(0m);
    }

    public override Task<IOrderDetails> PlaceOrder(IOrder order)
    {
        if (_placeOrderHandler != null)
        {
            return Task.FromResult(_placeOrderHandler(order));
        }

        // Default behavior: return a filled order
        var orderDetails = new OrderDetails
        {
            Side = order.Side,
            Result = OrderResult.Filled,
            Date = DateTimeOffset.Now,
            OrderId = Guid.NewGuid().ToString(),
            Pair = order.Pair,
            Amount = order.Amount,
            AmountFilled = order.Amount,
            Price = order.Price,
            AveragePrice = order.Price,
            Fees = order.Amount * order.Price * 0.001m, // 0.1% fee
            FeesCurrency = "BNB"
        };

        return Task.FromResult<IOrderDetails>(orderDetails);
    }
}

public class ExchangeServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ICoreService> _coreServiceMock;
    private TestableExchangeService _sut = null!;

    public ExchangeServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _coreServiceMock = new Mock<ICoreService>();
    }

    private TestableExchangeService CreateService(Func<IOrder, IOrderDetails>? placeOrderHandler = null)
    {
        return new TestableExchangeService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _coreServiceMock.Object,
            placeOrderHandler);
    }

    private void SetupDefaultTickers()
    {
        _sut.SetTickers(new List<Ticker>
        {
            new() { Pair = "BTCUSDT", LastPrice = 50000m, BidPrice = 49999m, AskPrice = 50001m },
            new() { Pair = "ETHUSDT", LastPrice = 3000m, BidPrice = 2999m, AskPrice = 3001m },
            new() { Pair = "BNBUSDT", LastPrice = 400m, BidPrice = 399m, AskPrice = 401m },
            new() { Pair = "BTCETH", LastPrice = 16.5m, BidPrice = 16.4m, AskPrice = 16.6m },
            new() { Pair = "ADAUSDT", LastPrice = 0.5m, BidPrice = 0.49m, AskPrice = 0.51m }
        });
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLoggingService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TestableExchangeService(
            null!,
            _healthCheckServiceMock.Object,
            _coreServiceMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("loggingService");
    }

    [Fact]
    public void Constructor_WithNullHealthCheckService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TestableExchangeService(
            _loggingServiceMock.Object,
            null!,
            _coreServiceMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("healthCheckService");
    }

    [Fact]
    public void Constructor_WithNullCoreService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TestableExchangeService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("coreService");
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
        service.ServiceName.Should().Be(Constants.ServiceNames.ExchangeService);
    }

    #endregion

    #region Start / Stop Tests

    [Fact]
    public void Start_InitializesService()
    {
        // Arrange
        _sut = CreateService();

        // Act
        _sut.Start(virtualTrading: false);

        // Assert
        _sut.IsStarted.Should().BeTrue();
        _sut.VirtualTradingEnabled.Should().BeFalse();
        _loggingServiceMock.Verify(x => x.Info(It.IsAny<string>(), It.IsAny<Exception>()), Times.AtLeastOnce);
    }

    [Fact]
    public void Start_WithVirtualTrading_SetsVirtualTradingEnabled()
    {
        // Arrange
        _sut = CreateService();

        // Act
        _sut.Start(virtualTrading: true);

        // Assert
        _sut.IsStarted.Should().BeTrue();
        _sut.VirtualTradingEnabled.Should().BeTrue();
    }

    [Fact]
    public void Stop_CleansUpResources()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        _sut.Stop();

        // Assert
        _sut.IsStarted.Should().BeFalse();
        _loggingServiceMock.Verify(x => x.Info(It.IsAny<string>(), It.IsAny<Exception>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task Stop_ClearsTickers()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Verify tickers exist before stop
        var tickersBefore = await _sut.GetTickers("USDT");
        tickersBefore.Should().NotBeEmpty();

        // Act
        _sut.Stop();

        // Assert
        var tickersAfter = await _sut.GetTickers("USDT");
        tickersAfter.Should().BeEmpty();
    }

    #endregion

    #region GetTickers Tests

    [Fact]
    public async Task GetTickers_ReturnsTickerData()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetTickers("USDT");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(4); // BTCUSDT, ETHUSDT, BNBUSDT, ADAUSDT
    }

    [Fact]
    public async Task GetTickers_FiltersTickersByMarket()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var usdtTickers = await _sut.GetTickers("USDT");
        var ethTickers = await _sut.GetTickers("ETH");

        // Assert
        usdtTickers.Should().HaveCount(4);
        usdtTickers.All(t => t.Pair.EndsWith("USDT")).Should().BeTrue();

        ethTickers.Should().HaveCount(1);
        ethTickers.All(t => t.Pair.EndsWith("ETH")).Should().BeTrue();
    }

    [Fact]
    public async Task GetTickers_WithNoMatchingMarket_ReturnsEmptyCollection()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetTickers("INVALID");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTickers_ReturnsCorrectTickerProperties()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        _sut.AddTicker(new Ticker
        {
            Pair = "TESTUSDT",
            LastPrice = 100m,
            BidPrice = 99m,
            AskPrice = 101m
        });

        // Act
        var result = (await _sut.GetTickers("USDT")).First();

        // Assert
        result.Pair.Should().Be("TESTUSDT");
        result.LastPrice.Should().Be(100m);
        result.BidPrice.Should().Be(99m);
        result.AskPrice.Should().Be(101m);
    }

    #endregion

    #region GetMarketPairs Tests

    [Fact]
    public async Task GetMarketPairs_ReturnsAvailablePairs()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetMarketPairs("USDT");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(4);
        result.Should().Contain(new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "ADAUSDT" });
    }

    [Fact]
    public async Task GetMarketPairs_FiltersCorrectly()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var ethPairs = await _sut.GetMarketPairs("ETH");

        // Assert
        ethPairs.Should().HaveCount(1);
        ethPairs.Should().Contain("BTCETH");
    }

    [Fact]
    public async Task GetMarketPairs_WithNoMatchingMarket_ReturnsEmptyCollection()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetMarketPairs("XYZ");

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetLastPrice Tests

    [Fact]
    public async Task GetLastPrice_WithValidPair_ReturnsPrice()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetLastPrice("BTCUSDT");

        // Assert
        result.Should().Be(50000m);
    }

    [Fact]
    public async Task GetLastPrice_WithInvalidPair_ReturnsZero()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetLastPrice("INVALIDPAIR");

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public async Task GetLastPrice_WithEmptyPair_ReturnsZero()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetLastPrice("");

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public async Task GetLastPrice_ReturnsCorrectPriceForEachPair()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act & Assert
        (await _sut.GetLastPrice("BTCUSDT")).Should().Be(50000m);
        (await _sut.GetLastPrice("ETHUSDT")).Should().Be(3000m);
        (await _sut.GetLastPrice("BNBUSDT")).Should().Be(400m);
        (await _sut.GetLastPrice("ADAUSDT")).Should().Be(0.5m);
    }

    #endregion

    #region PlaceOrder Tests

    [Fact]
    public async Task PlaceOrder_WithValidBuyOrder_ReturnsFilledOrder()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        var buyOrder = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 0.1m,
            Price = 50000m,
            Type = OrderType.Market,
            Date = DateTimeOffset.Now
        };

        // Act
        var result = await _sut.PlaceOrder(buyOrder);

        // Assert
        result.Should().NotBeNull();
        result.Side.Should().Be(OrderSide.Buy);
        result.Result.Should().Be(OrderResult.Filled);
        result.Pair.Should().Be("BTCUSDT");
        result.Amount.Should().Be(0.1m);
        result.AmountFilled.Should().Be(0.1m);
        result.Price.Should().Be(50000m);
        result.OrderId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PlaceOrder_WithValidSellOrder_ReturnsFilledOrder()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        var sellOrder = new SellOrder
        {
            Pair = "ETHUSDT",
            Amount = 1.5m,
            Price = 3000m,
            Type = OrderType.Limit,
            Date = DateTimeOffset.Now
        };

        // Act
        var result = await _sut.PlaceOrder(sellOrder);

        // Assert
        result.Should().NotBeNull();
        result.Side.Should().Be(OrderSide.Sell);
        result.Result.Should().Be(OrderResult.Filled);
        result.Pair.Should().Be("ETHUSDT");
        result.Amount.Should().Be(1.5m);
        result.AmountFilled.Should().Be(1.5m);
    }

    [Fact]
    public async Task PlaceOrder_WithInsufficientBalance_ReturnsFailedOrder()
    {
        // Arrange
        IOrderDetails placeOrderHandler(IOrder order) => new OrderDetails
        {
            Side = order.Side,
            Result = OrderResult.Error,
            Date = DateTimeOffset.Now,
            OrderId = "",
            Pair = order.Pair,
            Message = "Insufficient balance",
            Amount = order.Amount,
            AmountFilled = 0m,
            Price = order.Price,
            AveragePrice = 0m
        };

        _sut = CreateService(placeOrderHandler);
        _sut.Start(virtualTrading: false);

        var buyOrder = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 1000m, // Large amount
            Price = 50000m,
            Type = OrderType.Market,
            Date = DateTimeOffset.Now
        };

        // Act
        var result = await _sut.PlaceOrder(buyOrder);

        // Assert
        result.Should().NotBeNull();
        result.Result.Should().Be(OrderResult.Error);
        result.AmountFilled.Should().Be(0m);
        result.Message.Should().Contain("Insufficient balance");
    }

    [Fact]
    public async Task PlaceOrder_CalculatesFees()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        var buyOrder = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 1m,
            Price = 50000m,
            Type = OrderType.Market,
            Date = DateTimeOffset.Now
        };

        // Act
        var result = await _sut.PlaceOrder(buyOrder);

        // Assert
        result.Fees.Should().BeGreaterThan(0);
        result.FeesCurrency.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PlaceOrder_WithLimitOrder_SetsCorrectOrderType()
    {
        // Arrange
        var capturedOrder = (IOrder?)null;
        IOrderDetails placeOrderHandler(IOrder order)
        {
            capturedOrder = order;
            return new OrderDetails
            {
                Side = order.Side,
                Result = OrderResult.Pending,
                OrderId = Guid.NewGuid().ToString(),
                Pair = order.Pair,
                Amount = order.Amount,
                Price = order.Price
            };
        }

        _sut = CreateService(placeOrderHandler);
        _sut.Start(virtualTrading: false);

        var limitOrder = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 0.1m,
            Price = 49000m, // Below market price
            Type = OrderType.Limit,
            Date = DateTimeOffset.Now
        };

        // Act
        await _sut.PlaceOrder(limitOrder);

        // Assert
        capturedOrder.Should().NotBeNull();
        capturedOrder!.Type.Should().Be(OrderType.Limit);
        capturedOrder.Price.Should().Be(49000m);
    }

    [Fact]
    public async Task PlaceOrder_WithMarketOrder_SetsCorrectOrderType()
    {
        // Arrange
        var capturedOrder = (IOrder?)null;
        IOrderDetails placeOrderHandler(IOrder order)
        {
            capturedOrder = order;
            return new OrderDetails
            {
                Side = order.Side,
                Result = OrderResult.Filled,
                OrderId = Guid.NewGuid().ToString(),
                Pair = order.Pair,
                Amount = order.Amount,
                AmountFilled = order.Amount,
                Price = order.Price,
                AveragePrice = order.Price
            };
        }

        _sut = CreateService(placeOrderHandler);
        _sut.Start(virtualTrading: false);

        var marketOrder = new SellOrder
        {
            Pair = "ETHUSDT",
            Amount = 1m,
            Price = 0m, // Market orders don't need price
            Type = OrderType.Market,
            Date = DateTimeOffset.Now
        };

        // Act
        await _sut.PlaceOrder(marketOrder);

        // Assert
        capturedOrder.Should().NotBeNull();
        capturedOrder!.Type.Should().Be(OrderType.Market);
    }

    [Fact]
    public async Task PlaceOrder_GeneratesUniqueOrderId()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        var order1 = new BuyOrder { Pair = "BTCUSDT", Amount = 0.1m, Price = 50000m };
        var order2 = new BuyOrder { Pair = "BTCUSDT", Amount = 0.1m, Price = 50000m };

        // Act
        var result1 = await _sut.PlaceOrder(order1);
        var result2 = await _sut.PlaceOrder(order2);

        // Assert
        result1.OrderId.Should().NotBe(result2.OrderId);
    }

    [Fact]
    public async Task PlaceOrder_PartiallyFilled_ReturnsCorrectResult()
    {
        // Arrange
        IOrderDetails placeOrderHandler(IOrder order) => new OrderDetails
        {
            Side = order.Side,
            Result = OrderResult.FilledPartially,
            OrderId = Guid.NewGuid().ToString(),
            Pair = order.Pair,
            Amount = order.Amount,
            AmountFilled = order.Amount * 0.5m, // 50% filled
            Price = order.Price,
            AveragePrice = order.Price
        };

        _sut = CreateService(placeOrderHandler);
        _sut.Start(virtualTrading: false);

        var order = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 1m,
            Price = 50000m,
            Type = OrderType.Limit
        };

        // Act
        var result = await _sut.PlaceOrder(order);

        // Assert
        result.Result.Should().Be(OrderResult.FilledPartially);
        result.Amount.Should().Be(1m);
        result.AmountFilled.Should().Be(0.5m);
    }

    #endregion

    #region GetAvailableAmounts Tests

    [Fact]
    public async Task GetAvailableAmounts_ReturnsDictionary()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        _sut.SetAvailableAmounts(new Dictionary<string, decimal>
        {
            { "BTC", 1.5m },
            { "ETH", 10m },
            { "USDT", 5000m }
        });

        // Act
        var result = await _sut.GetAvailableAmounts();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result["BTC"].Should().Be(1.5m);
        result["ETH"].Should().Be(10m);
        result["USDT"].Should().Be(5000m);
    }

    [Fact]
    public async Task GetAvailableAmounts_WithNoBalances_ReturnsEmptyDictionary()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        // Act
        var result = await _sut.GetAvailableAmounts();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableAmounts_ReturnsCorrectDecimals()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        _sut.SetAvailableAmounts(new Dictionary<string, decimal>
        {
            { "BTC", 0.00012345m },
            { "SHIB", 1000000000m }
        });

        // Act
        var result = await _sut.GetAvailableAmounts();

        // Assert
        result["BTC"].Should().Be(0.00012345m);
        result["SHIB"].Should().Be(1000000000m);
    }

    #endregion

    #region GetMyTrades Tests

    [Fact]
    public async Task GetMyTrades_WithValidPair_ReturnsTradeHistory()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        var trades = new List<IOrderDetails>
        {
            new OrderDetails
            {
                Side = OrderSide.Buy,
                Result = OrderResult.Filled,
                Date = DateTimeOffset.Now.AddDays(-1),
                OrderId = "order1",
                Pair = "BTCUSDT",
                Amount = 0.1m,
                AmountFilled = 0.1m,
                Price = 49000m,
                AveragePrice = 49000m
            },
            new OrderDetails
            {
                Side = OrderSide.Sell,
                Result = OrderResult.Filled,
                Date = DateTimeOffset.Now,
                OrderId = "order2",
                Pair = "BTCUSDT",
                Amount = 0.1m,
                AmountFilled = 0.1m,
                Price = 51000m,
                AveragePrice = 51000m
            }
        };

        _sut.SetMyTrades(trades);

        // Act
        var result = await _sut.GetMyTrades("BTCUSDT");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMyTrades_WithNoTrades_ReturnsEmptyCollection()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        // Act
        var result = await _sut.GetMyTrades("BTCUSDT");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyTrades_FiltersByPair()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        var trades = new List<IOrderDetails>
        {
            new OrderDetails { OrderId = "1", Pair = "BTCUSDT", Side = OrderSide.Buy, Result = OrderResult.Filled },
            new OrderDetails { OrderId = "2", Pair = "ETHUSDT", Side = OrderSide.Buy, Result = OrderResult.Filled },
            new OrderDetails { OrderId = "3", Pair = "BTCUSDT", Side = OrderSide.Sell, Result = OrderResult.Filled }
        };

        _sut.SetMyTrades(trades);

        // Act
        var btcTrades = await _sut.GetMyTrades("BTCUSDT");
        var ethTrades = await _sut.GetMyTrades("ETHUSDT");

        // Assert
        btcTrades.Should().HaveCount(2);
        btcTrades.All(t => t.Pair == "BTCUSDT").Should().BeTrue();

        ethTrades.Should().HaveCount(1);
        ethTrades.All(t => t.Pair == "ETHUSDT").Should().BeTrue();
    }

    [Fact]
    public async Task GetMyTrades_ReturnsCorrectOrderDetails()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        var trade = new OrderDetails
        {
            Side = OrderSide.Buy,
            Result = OrderResult.Filled,
            Date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            OrderId = "test-order-123",
            Pair = "BTCUSDT",
            Message = "Order filled successfully",
            Amount = 0.5m,
            AmountFilled = 0.5m,
            Price = 45000m,
            AveragePrice = 44950m,
            Fees = 2.25m,
            FeesCurrency = "BNB"
        };

        _sut.SetMyTrades(new List<IOrderDetails> { trade });

        // Act
        var result = (await _sut.GetMyTrades("BTCUSDT")).First();

        // Assert
        result.Side.Should().Be(OrderSide.Buy);
        result.Result.Should().Be(OrderResult.Filled);
        result.OrderId.Should().Be("test-order-123");
        result.Pair.Should().Be("BTCUSDT");
        result.Amount.Should().Be(0.5m);
        result.AmountFilled.Should().Be(0.5m);
        result.Price.Should().Be(45000m);
        result.AveragePrice.Should().Be(44950m);
        result.Fees.Should().Be(2.25m);
        result.FeesCurrency.Should().Be("BNB");
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsExchangeServiceConstant()
    {
        // Arrange
        _sut = CreateService();

        // Act
        var serviceName = _sut.ServiceName;

        // Assert
        serviceName.Should().Be(Constants.ServiceNames.ExchangeService);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task GetTickers_IsThreadSafe()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act - Multiple concurrent calls
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _sut.GetTickers("USDT"))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().HaveCount(4));
    }

    [Fact]
    public async Task GetLastPrice_IsThreadSafe()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act - Multiple concurrent calls
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => _sut.GetLastPrice("BTCUSDT"))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().Be(50000m));
    }

    [Fact]
    public async Task PlaceOrder_IsThreadSafe()
    {
        // Arrange
        var orderCount = 0;
        IOrderDetails placeOrderHandler(IOrder order)
        {
            Interlocked.Increment(ref orderCount);
            return new OrderDetails
            {
                Side = order.Side,
                Result = OrderResult.Filled,
                OrderId = Guid.NewGuid().ToString(),
                Pair = order.Pair,
                Amount = order.Amount,
                AmountFilled = order.Amount
            };
        }

        _sut = CreateService(placeOrderHandler);
        _sut.Start(virtualTrading: false);

        // Act - Multiple concurrent order placements
        var tasks = Enumerable.Range(0, 50)
            .Select(i => _sut.PlaceOrder(new BuyOrder
            {
                Pair = "BTCUSDT",
                Amount = 0.01m,
                Price = 50000m
            }))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(50);
        results.Should().AllSatisfy(r => r.Result.Should().Be(OrderResult.Filled));
        orderCount.Should().Be(50);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GetLastPrice_WithZeroPrice_ReturnsZero()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        _sut.AddTicker(new Ticker { Pair = "DEADUSDT", LastPrice = 0m });

        // Act
        var result = await _sut.GetLastPrice("DEADUSDT");

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public async Task PlaceOrder_WithZeroAmount_ProcessesOrder()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);

        var order = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 0m,
            Price = 50000m
        };

        // Act
        var result = await _sut.PlaceOrder(order);

        // Assert
        result.Should().NotBeNull();
        result.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task GetTickers_WithSpecialCharactersInMarket_HandlesGracefully()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var result = await _sut.GetTickers("!@#$%");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMarketPairs_CaseSensitive_ReturnsCorrectPairs()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();

        // Act
        var upperResult = await _sut.GetMarketPairs("USDT");
        var lowerResult = await _sut.GetMarketPairs("usdt");

        // Assert
        upperResult.Should().HaveCount(4);
        lowerResult.Should().BeEmpty(); // Case sensitive
    }

    #endregion

    #region Integration-like Tests

    [Fact]
    public async Task FullTradingCycle_BuyAndSell_WorksCorrectly()
    {
        // Arrange
        _sut = CreateService();
        _sut.Start(virtualTrading: false);
        SetupDefaultTickers();
        _sut.SetAvailableAmounts(new Dictionary<string, decimal>
        {
            { "USDT", 10000m },
            { "BTC", 0m }
        });

        // Act - Buy
        var buyOrder = new BuyOrder
        {
            Pair = "BTCUSDT",
            Amount = 0.1m,
            Price = 50000m,
            Type = OrderType.Market
        };
        var buyResult = await _sut.PlaceOrder(buyOrder);

        // Act - Sell
        var sellOrder = new SellOrder
        {
            Pair = "BTCUSDT",
            Amount = 0.1m,
            Price = 51000m,
            Type = OrderType.Market
        };
        var sellResult = await _sut.PlaceOrder(sellOrder);

        // Assert
        buyResult.Should().NotBeNull();
        buyResult.Side.Should().Be(OrderSide.Buy);
        buyResult.Result.Should().Be(OrderResult.Filled);

        sellResult.Should().NotBeNull();
        sellResult.Side.Should().Be(OrderSide.Sell);
        sellResult.Result.Should().Be(OrderResult.Filled);
    }

    [Fact]
    public async Task ServiceLifecycle_StartStopRestart_WorksCorrectly()
    {
        // Arrange
        _sut = CreateService();

        // Act & Assert - Start
        _sut.Start(virtualTrading: false);
        _sut.IsStarted.Should().BeTrue();
        SetupDefaultTickers();
        (await _sut.GetTickers("USDT")).Should().HaveCount(4);

        // Act & Assert - Stop
        _sut.Stop();
        _sut.IsStarted.Should().BeFalse();
        (await _sut.GetTickers("USDT")).Should().BeEmpty();

        // Act & Assert - Restart
        _sut.Start(virtualTrading: true);
        _sut.IsStarted.Should().BeTrue();
        _sut.VirtualTradingEnabled.Should().BeTrue();
    }

    #endregion
}
