using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Exchange;
using Moq;
using Xunit;
using LegacyOrderSide = IntelliTrader.Core.OrderSide;
using LegacyOrderType = IntelliTrader.Core.OrderType;
using NewOrderSide = IntelliTrader.Application.Ports.Driven.OrderSide;
using NewOrderType = IntelliTrader.Application.Ports.Driven.OrderType;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Exchange;

public class BinanceExchangeAdapterTests
{
    private readonly Mock<IExchangeService> _exchangeServiceMock;
    private readonly BinanceExchangeAdapter _sut;

    public BinanceExchangeAdapterTests()
    {
        _exchangeServiceMock = new Mock<IExchangeService>();
        _sut = new BinanceExchangeAdapter(_exchangeServiceMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullExchangeService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new BinanceExchangeAdapter(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("legacyExchange");
    }

    [Fact]
    public void Constructor_WithValidExchangeService_CreatesInstance()
    {
        // Act
        var adapter = new BinanceExchangeAdapter(_exchangeServiceMock.Object);

        // Assert
        adapter.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithCustomQuoteCurrency_CreatesInstance()
    {
        // Act
        var adapter = new BinanceExchangeAdapter(_exchangeServiceMock.Object, "BTC");

        // Assert
        adapter.Should().NotBeNull();
    }

    #endregion

    #region PlaceMarketBuyAsync Tests

    [Fact]
    public async Task PlaceMarketBuyAsync_WithValidParameters_ReturnsSuccess()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");
        var currentPrice = 50000m;

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(currentPrice);

        var legacyOrder = CreateMockOrderDetails(
            orderId: "12345",
            pair: pair.Symbol,
            side: LegacyOrderSide.Buy,
            amount: 0.002m,
            amountFilled: 0.002m,
            price: currentPrice,
            averagePrice: currentPrice,
            fees: 0.01m);

        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .ReturnsAsync(legacyOrder);

        // Act
        var result = await _sut.PlaceMarketBuyAsync(pair, cost);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be("12345");
        result.Value.Pair.Should().Be(pair);
        result.Value.Side.Should().Be(NewOrderSide.Buy);
    }

    [Fact]
    public async Task PlaceMarketBuyAsync_WhenPriceNotAvailable_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(0m);

        // Act
        var result = await _sut.PlaceMarketBuyAsync(pair, cost);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task PlaceMarketBuyAsync_WhenExchangeThrows_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act
        var result = await _sut.PlaceMarketBuyAsync(pair, cost);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Connection failed");
    }

    [Fact]
    public async Task PlaceMarketBuyAsync_CalculatesCorrectAmount()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(1000m, "USDT");
        var currentPrice = 50000m;
        var expectedAmount = cost.Amount / currentPrice; // 0.02 BTC

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(currentPrice);

        IOrder? capturedOrder = null;
        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .Callback<IOrder>(order => capturedOrder = order)
            .ReturnsAsync(CreateMockOrderDetails("1", pair.Symbol, LegacyOrderSide.Buy, expectedAmount, expectedAmount, currentPrice, currentPrice, 0));

        // Act
        await _sut.PlaceMarketBuyAsync(pair, cost);

        // Assert
        capturedOrder.Should().NotBeNull();
        capturedOrder!.Amount.Should().Be(expectedAmount);
        capturedOrder.Side.Should().Be(LegacyOrderSide.Buy);
        capturedOrder.Type.Should().Be(LegacyOrderType.Market);
    }

    #endregion

    #region PlaceMarketSellAsync Tests

    [Fact]
    public async Task PlaceMarketSellAsync_WithValidParameters_ReturnsSuccess()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var quantity = Quantity.Create(0.5m);
        var currentPrice = 50000m;

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(currentPrice);

        var legacyOrder = CreateMockOrderDetails(
            orderId: "12345",
            pair: pair.Symbol,
            side: LegacyOrderSide.Sell,
            amount: 0.5m,
            amountFilled: 0.5m,
            price: currentPrice,
            averagePrice: currentPrice,
            fees: 0.05m);

        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .ReturnsAsync(legacyOrder);

        // Act
        var result = await _sut.PlaceMarketSellAsync(pair, quantity);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Side.Should().Be(NewOrderSide.Sell);
        result.Value.FilledQuantity.Value.Should().Be(0.5m);
    }

    [Fact]
    public async Task PlaceMarketSellAsync_WhenPriceNotAvailable_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var quantity = Quantity.Create(0.5m);

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(0m);

        // Act
        var result = await _sut.PlaceMarketSellAsync(pair, quantity);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region PlaceLimitBuyAsync Tests

    [Fact]
    public async Task PlaceLimitBuyAsync_WithValidParameters_ReturnsSuccess()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var quantity = Quantity.Create(0.1m);
        var price = Price.Create(49000m);

        var legacyOrder = CreateMockOrderDetails(
            orderId: "12345",
            pair: pair.Symbol,
            side: LegacyOrderSide.Buy,
            amount: 0.1m,
            amountFilled: 0.1m,
            price: 49000m,
            averagePrice: 49000m,
            fees: 0.01m);

        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .ReturnsAsync(legacyOrder);

        // Act
        var result = await _sut.PlaceLimitBuyAsync(pair, quantity, price);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Type.Should().Be(NewOrderType.Market); // Legacy doesn't expose type, defaults to Market
    }

    [Fact]
    public async Task PlaceLimitBuyAsync_UsesLimitOrderType()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var quantity = Quantity.Create(0.1m);
        var price = Price.Create(49000m);

        IOrder? capturedOrder = null;
        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .Callback<IOrder>(order => capturedOrder = order)
            .ReturnsAsync(CreateMockOrderDetails("1", pair.Symbol, LegacyOrderSide.Buy, 0.1m, 0.1m, 49000m, 49000m, 0));

        // Act
        await _sut.PlaceLimitBuyAsync(pair, quantity, price);

        // Assert
        capturedOrder.Should().NotBeNull();
        capturedOrder!.Type.Should().Be(LegacyOrderType.Limit);
        capturedOrder.Price.Should().Be(49000m);
    }

    #endregion

    #region PlaceLimitSellAsync Tests

    [Fact]
    public async Task PlaceLimitSellAsync_WithValidParameters_ReturnsSuccess()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var quantity = Quantity.Create(0.1m);
        var price = Price.Create(51000m);

        var legacyOrder = CreateMockOrderDetails(
            orderId: "12345",
            pair: pair.Symbol,
            side: LegacyOrderSide.Sell,
            amount: 0.1m,
            amountFilled: 0.1m,
            price: 51000m,
            averagePrice: 51000m,
            fees: 0.01m);

        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .ReturnsAsync(legacyOrder);

        // Act
        var result = await _sut.PlaceLimitSellAsync(pair, quantity, price);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Side.Should().Be(NewOrderSide.Sell);
    }

    #endregion

    #region GetCurrentPriceAsync Tests

    [Fact]
    public async Task GetCurrentPriceAsync_WithValidPair_ReturnsPrice()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var expectedPrice = 50000m;

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(expectedPrice);

        // Act
        var result = await _sut.GetCurrentPriceAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expectedPrice);
    }

    [Fact]
    public async Task GetCurrentPriceAsync_WhenPriceIsZero_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(0m);

        // Act
        var result = await _sut.GetCurrentPriceAsync(pair);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetCurrentPriceAsync_WhenExchangeThrows_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        var result = await _sut.GetCurrentPriceAsync(pair);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    #endregion

    #region GetCurrentPricesAsync Tests

    [Fact]
    public async Task GetCurrentPricesAsync_WithMultiplePairs_ReturnsPrices()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var ethPair = TradingPair.Create("ETHUSDT", "USDT");
        var pairs = new[] { btcPair, ethPair };

        var tickers = new List<ITicker>
        {
            CreateMockTicker("BTCUSDT", 50000m),
            CreateMockTicker("ETHUSDT", 3000m)
        };

        _exchangeServiceMock
            .Setup(x => x.GetTickers("USDT"))
            .ReturnsAsync(tickers);

        // Act
        var result = await _sut.GetCurrentPricesAsync(pairs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[btcPair].Value.Should().Be(50000m);
        result.Value[ethPair].Value.Should().Be(3000m);
    }

    [Fact]
    public async Task GetCurrentPricesAsync_WhenPairNotFound_OmitsPair()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var unknownPair = TradingPair.Create("XYZUSDT", "USDT");
        var pairs = new[] { btcPair, unknownPair };

        var tickers = new List<ITicker>
        {
            CreateMockTicker("BTCUSDT", 50000m)
        };

        _exchangeServiceMock
            .Setup(x => x.GetTickers("USDT"))
            .ReturnsAsync(tickers);

        // Act
        var result = await _sut.GetCurrentPricesAsync(pairs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.ContainsKey(btcPair).Should().BeTrue();
        result.Value.ContainsKey(unknownPair).Should().BeFalse();
    }

    #endregion

    #region GetBalanceAsync Tests

    [Fact]
    public async Task GetBalanceAsync_WithValidCurrency_ReturnsBalance()
    {
        // Arrange
        var amounts = new Dictionary<string, decimal>
        {
            { "USDT", 1000m },
            { "BTC", 0.5m }
        };

        _exchangeServiceMock
            .Setup(x => x.GetAvailableAmounts())
            .ReturnsAsync(amounts);

        // Act
        var result = await _sut.GetBalanceAsync("USDT");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Currency.Should().Be("USDT");
        result.Value.Available.Should().Be(1000m);
        result.Value.Total.Should().Be(1000m);
    }

    [Fact]
    public async Task GetBalanceAsync_WhenCurrencyNotFound_ReturnsZeroBalance()
    {
        // Arrange
        var amounts = new Dictionary<string, decimal>
        {
            { "USDT", 1000m }
        };

        _exchangeServiceMock
            .Setup(x => x.GetAvailableAmounts())
            .ReturnsAsync(amounts);

        // Act
        var result = await _sut.GetBalanceAsync("BTC");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Currency.Should().Be("BTC");
        result.Value.Available.Should().Be(0m);
    }

    #endregion

    #region GetAllBalancesAsync Tests

    [Fact]
    public async Task GetAllBalancesAsync_ReturnsNonZeroBalances()
    {
        // Arrange
        var amounts = new Dictionary<string, decimal>
        {
            { "USDT", 1000m },
            { "BTC", 0.5m },
            { "ETH", 0m } // Should be excluded
        };

        _exchangeServiceMock
            .Setup(x => x.GetAvailableAmounts())
            .ReturnsAsync(amounts);

        // Act
        var result = await _sut.GetAllBalancesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(b => b.Currency == "USDT");
        result.Value.Should().Contain(b => b.Currency == "BTC");
        result.Value.Should().NotContain(b => b.Currency == "ETH");
    }

    #endregion

    #region GetOrderAsync Tests

    [Fact]
    public async Task GetOrderAsync_WhenOrderFound_ReturnsOrderInfo()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderId = "12345";

        var trades = new List<IOrderDetails>
        {
            CreateMockOrderDetails(orderId, pair.Symbol, LegacyOrderSide.Buy, 0.1m, 0.1m, 50000m, 50000m, 0.01m)
        };

        _exchangeServiceMock
            .Setup(x => x.GetMyTrades(pair.Symbol))
            .ReturnsAsync(trades);

        // Act
        var result = await _sut.GetOrderAsync(pair, orderId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.OrderId.Should().Be(orderId);
        result.Value.Pair.Should().Be(pair);
    }

    [Fact]
    public async Task GetOrderAsync_WhenOrderNotFound_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderId = "99999";

        var trades = new List<IOrderDetails>
        {
            CreateMockOrderDetails("12345", pair.Symbol, LegacyOrderSide.Buy, 0.1m, 0.1m, 50000m, 50000m, 0.01m)
        };

        _exchangeServiceMock
            .Setup(x => x.GetMyTrades(pair.Symbol))
            .ReturnsAsync(trades);

        // Act
        var result = await _sut.GetOrderAsync(pair, orderId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    #endregion

    #region CancelOrderAsync Tests

    [Fact]
    public async Task CancelOrderAsync_ReturnsNotSupported()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var orderId = "12345";

        // Act
        var result = await _sut.CancelOrderAsync(pair, orderId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotSupported");
    }

    #endregion

    #region GetTradingRulesAsync Tests

    [Fact]
    public async Task GetTradingRulesAsync_ReturnsDefaultRules()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act
        var result = await _sut.GetTradingRulesAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Pair.Should().Be(pair);
        result.Value.MinOrderValue.Should().Be(10m);
        result.Value.IsTradingEnabled.Should().BeTrue();
    }

    #endregion

    #region TestConnectivityAsync Tests

    [Fact]
    public async Task TestConnectivityAsync_WhenConnected_ReturnsTrue()
    {
        // Arrange
        var tickers = new List<ITicker>
        {
            CreateMockTicker("BTCUSDT", 50000m)
        };

        _exchangeServiceMock
            .Setup(x => x.GetTickers("USDT"))
            .ReturnsAsync(tickers);

        // Act
        var result = await _sut.TestConnectivityAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectivityAsync_WhenDisconnected_ReturnsFailure()
    {
        // Arrange
        _exchangeServiceMock
            .Setup(x => x.GetTickers("USDT"))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act
        var result = await _sut.TestConnectivityAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    #endregion

    #region Order Status Conversion Tests

    [Fact]
    public async Task PlaceOrder_WithFilledResult_ReturnsFilledStatus()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(50000m);

        var legacyOrder = CreateMockOrderDetails(
            orderId: "1",
            pair: pair.Symbol,
            side: LegacyOrderSide.Buy,
            amount: 0.002m,
            amountFilled: 0.002m,
            price: 50000m,
            averagePrice: 50000m,
            fees: 0,
            result: OrderResult.Filled);

        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .ReturnsAsync(legacyOrder);

        // Act
        var result = await _sut.PlaceMarketBuyAsync(pair, cost);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.Filled);
        result.Value.IsFullyFilled.Should().BeTrue();
    }

    [Fact]
    public async Task PlaceOrder_WithPartiallyFilledResult_ReturnsPartiallyFilledStatus()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var cost = Money.Create(100m, "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(50000m);

        var legacyOrder = CreateMockOrderDetails(
            orderId: "1",
            pair: pair.Symbol,
            side: LegacyOrderSide.Buy,
            amount: 0.002m,
            amountFilled: 0.001m,
            price: 50000m,
            averagePrice: 50000m,
            fees: 0,
            result: OrderResult.FilledPartially);

        _exchangeServiceMock
            .Setup(x => x.PlaceOrder(It.IsAny<IOrder>()))
            .ReturnsAsync(legacyOrder);

        // Act
        var result = await _sut.PlaceMarketBuyAsync(pair, cost);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(OrderStatus.PartiallyFilled);
        result.Value.IsPartiallyFilled.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static IOrderDetails CreateMockOrderDetails(
        string orderId,
        string pair,
        LegacyOrderSide side,
        decimal amount,
        decimal amountFilled,
        decimal price,
        decimal averagePrice,
        decimal fees,
        OrderResult result = OrderResult.Filled,
        string? feesCurrency = null)
    {
        var mock = new Mock<IOrderDetails>();
        mock.Setup(x => x.OrderId).Returns(orderId);
        mock.Setup(x => x.Pair).Returns(pair);
        mock.Setup(x => x.Side).Returns(side);
        mock.Setup(x => x.Result).Returns(result);
        mock.Setup(x => x.Amount).Returns(amount);
        mock.Setup(x => x.AmountFilled).Returns(amountFilled);
        mock.Setup(x => x.Price).Returns(price);
        mock.Setup(x => x.AveragePrice).Returns(averagePrice);
        mock.Setup(x => x.Fees).Returns(fees);
        mock.Setup(x => x.FeesCurrency).Returns(feesCurrency);
        mock.Setup(x => x.AverageCost).Returns(amountFilled * averagePrice);
        mock.Setup(x => x.Date).Returns(DateTimeOffset.UtcNow);
        return mock.Object;
    }

    private static ITicker CreateMockTicker(string pair, decimal lastPrice)
    {
        var mock = new Mock<ITicker>();
        mock.Setup(x => x.Pair).Returns(pair);
        mock.Setup(x => x.LastPrice).Returns(lastPrice);
        mock.Setup(x => x.AskPrice).Returns(lastPrice * 1.001m);
        mock.Setup(x => x.BidPrice).Returns(lastPrice * 0.999m);
        return mock.Object;
    }

    #endregion
}
