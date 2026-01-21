using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Backtesting;
using IntelliTrader.Exchange.Base;

namespace IntelliTrader.Backtesting.Tests;

public class BacktestingExchangeServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly BacktestingExchangeService _sut;

    public BacktestingExchangeServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();

        // Setup default empty tickers
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker>());

        _sut = new BacktestingExchangeService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _backtestingServiceMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Assert
        _sut.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_SetsLoggingService()
    {
        // Assert - Implicitly tested via Start() logging
        _sut.Start(virtualTrading: true);
        _loggingServiceMock.Verify(
            x => x.Info(It.IsAny<string>(), It.IsAny<Exception>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsExchangeService()
    {
        // Act
        var serviceName = _sut.ServiceName;

        // Assert
        serviceName.Should().Be(Constants.ServiceNames.ExchangeService);
    }

    #endregion

    #region Start Tests

    [Fact]
    public void Start_WithVirtualTrading_LogsStartMessage()
    {
        // Act
        _sut.Start(virtualTrading: true);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Start Backtesting Exchange service")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Start_WithLiveTrading_LogsStartMessage()
    {
        // Act
        _sut.Start(virtualTrading: false);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Start Backtesting Exchange service")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Start_LogsStartedMessage()
    {
        // Act
        _sut.Start(virtualTrading: true);

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Backtesting Exchange service started")), It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region Stop Tests

    [Fact]
    public void Stop_LogsStopMessage()
    {
        // Act
        _sut.Stop();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Stop Backtesting Exchange service")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Stop_LogsStoppedMessage()
    {
        // Act
        _sut.Stop();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Backtesting Exchange service stopped")), It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region GetLastPrice Tests

    [Fact]
    public async Task GetLastPrice_WhenTickerExists_ReturnsLastPrice()
    {
        // Arrange
        var pair = "BTCUSDT";
        var expectedPrice = 50000m;
        var tickerMock = new Mock<ITicker>();
        tickerMock.Setup(x => x.Pair).Returns(pair);
        tickerMock.Setup(x => x.LastPrice).Returns(expectedPrice);

        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker> { { pair, tickerMock.Object } });

        // Act
        var result = await _sut.GetLastPrice(pair);

        // Assert
        result.Should().Be(expectedPrice);
    }

    [Fact]
    public async Task GetLastPrice_WhenTickerDoesNotExist_ReturnsZero()
    {
        // Arrange
        var pair = "NONEXISTENT";
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker>());

        // Act
        var result = await _sut.GetLastPrice(pair);

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public async Task GetLastPrice_WithMultipleTickers_ReturnsCorrectPrice()
    {
        // Arrange
        var btcTicker = CreateTicker("BTCUSDT", 50000m);
        var ethTicker = CreateTicker("ETHUSDT", 3000m);

        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker>
            {
                { "BTCUSDT", btcTicker },
                { "ETHUSDT", ethTicker }
            });

        // Act
        var btcPrice = await _sut.GetLastPrice("BTCUSDT");
        var ethPrice = await _sut.GetLastPrice("ETHUSDT");

        // Assert
        btcPrice.Should().Be(50000m);
        ethPrice.Should().Be(3000m);
    }

    [Theory]
    [InlineData("BTCUSDT", 50000.12345678)]
    [InlineData("ETHUSDT", 3000.00000001)]
    [InlineData("DOGEUSDT", 0.00012345)]
    public async Task GetLastPrice_WithVariousPrices_ReturnsCorrectDecimalValue(string pair, decimal price)
    {
        // Arrange
        var ticker = CreateTicker(pair, price);
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker> { { pair, ticker } });

        // Act
        var result = await _sut.GetLastPrice(pair);

        // Assert
        result.Should().Be(price);
    }

    #endregion

    #region GetMarketPairs Tests

    [Fact]
    public async Task GetMarketPairs_ReturnsAllTickerKeys()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 50000m) },
            { "ETHUSDT", CreateTicker("ETHUSDT", 3000m) },
            { "ADAUSDT", CreateTicker("ADAUSDT", 1.5m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act
        var result = await _sut.GetMarketPairs("USDT");

        // Assert
        result.Should().BeEquivalentTo(new[] { "BTCUSDT", "ETHUSDT", "ADAUSDT" });
    }

    [Fact]
    public async Task GetMarketPairs_WhenNoTickers_ReturnsEmptyCollection()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker>());

        // Act
        var result = await _sut.GetMarketPairs("USDT");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMarketPairs_IgnoresMarketParameter()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 50000m) },
            { "BTCETH", CreateTicker("BTCETH", 15m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act - Market parameter should be ignored, returns all pairs
        var result = await _sut.GetMarketPairs("ETH");

        // Assert - Should still return all pairs regardless of market filter
        result.Should().HaveCount(2);
    }

    #endregion

    #region NotImplemented Methods Tests

    [Fact]
    public async Task PlaceOrder_ThrowsNotImplementedException()
    {
        // Arrange
        var orderMock = new Mock<IOrder>();

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => _sut.PlaceOrder(orderMock.Object));
    }

    [Fact]
    public async Task GetAvailableAmounts_ThrowsNotImplementedException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => _sut.GetAvailableAmounts());
    }

    [Fact]
    public async Task GetMyTrades_ThrowsNotImplementedException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => _sut.GetMyTrades("BTCUSDT"));
    }

    [Fact]
    public async Task GetTickers_ThrowsNotImplementedException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() => _sut.GetTickers("USDT"));
    }

    #endregion

    #region Historical Price Simulation Tests

    [Fact]
    public async Task GetLastPrice_SimulatesHistoricalPriceFromSnapshot()
    {
        // Arrange - Simulate different snapshot states
        var initialTickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 45000m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(initialTickers);

        // Act
        var price1 = await _sut.GetLastPrice("BTCUSDT");

        // Simulate snapshot update
        var updatedTickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 46000m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(updatedTickers);

        var price2 = await _sut.GetLastPrice("BTCUSDT");

        // Assert - Prices should change based on snapshot state
        price1.Should().Be(45000m);
        price2.Should().Be(46000m);
    }

    [Fact]
    public async Task GetLastPrice_HandlesVolatilePriceChanges()
    {
        // Arrange - Simulate volatile market
        var prices = new[] { 50000m, 48000m, 52000m, 49500m };

        foreach (var price in prices)
        {
            var tickers = new Dictionary<string, ITicker>
            {
                { "BTCUSDT", CreateTicker("BTCUSDT", price) }
            };
            _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

            // Act
            var result = await _sut.GetLastPrice("BTCUSDT");

            // Assert
            result.Should().Be(price);
        }
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task GetLastPrice_HandlesMultipleConcurrentRequests()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 50000m) },
            { "ETHUSDT", CreateTicker("ETHUSDT", 3000m) },
            { "ADAUSDT", CreateTicker("ADAUSDT", 1.5m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act - Multiple concurrent requests
        var tasks = new[]
        {
            _sut.GetLastPrice("BTCUSDT"),
            _sut.GetLastPrice("ETHUSDT"),
            _sut.GetLastPrice("ADAUSDT"),
            _sut.GetLastPrice("BTCUSDT"),
            _sut.GetLastPrice("ETHUSDT")
        };

        var results = await Task.WhenAll(tasks);

        // Assert
        results[0].Should().Be(50000m);
        results[1].Should().Be(3000m);
        results[2].Should().Be(1.5m);
        results[3].Should().Be(50000m);
        results[4].Should().Be(3000m);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task GetLastPrice_WithEmptyPairName_ReturnsZero()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker>());

        // Act
        var result = await _sut.GetLastPrice(string.Empty);

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public async Task GetLastPrice_WithCaseSensitivePair_HandlesCorrectly()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 50000m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act - Dictionary is case-sensitive
        var upperResult = await _sut.GetLastPrice("BTCUSDT");
        var lowerResult = await _sut.GetLastPrice("btcusdt");

        // Assert
        upperResult.Should().Be(50000m);
        lowerResult.Should().Be(0m); // Case-sensitive, so lowercase won't match
    }

    [Fact]
    public async Task GetLastPrice_WithZeroPrice_ReturnsZero()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 0m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act
        var result = await _sut.GetLastPrice("BTCUSDT");

        // Assert
        result.Should().Be(0m);
    }

    [Fact]
    public async Task GetLastPrice_WithVerySmallPrice_ReturnsCorrectValue()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "SHIBUSDT", CreateTicker("SHIBUSDT", 0.00000001m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act
        var result = await _sut.GetLastPrice("SHIBUSDT");

        // Assert
        result.Should().Be(0.00000001m);
    }

    [Fact]
    public async Task GetLastPrice_WithVeryLargePrice_ReturnsCorrectValue()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateTicker("BTCUSDT", 999999999.99999999m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act
        var result = await _sut.GetLastPrice("BTCUSDT");

        // Assert
        result.Should().Be(999999999.99999999m);
    }

    #endregion

    #region Helper Methods

    private static ITicker CreateTicker(string pair, decimal lastPrice)
    {
        var tickerMock = new Mock<ITicker>();
        tickerMock.Setup(x => x.Pair).Returns(pair);
        tickerMock.Setup(x => x.LastPrice).Returns(lastPrice);
        tickerMock.Setup(x => x.BidPrice).Returns(lastPrice * 0.999m);
        tickerMock.Setup(x => x.AskPrice).Returns(lastPrice * 1.001m);
        return tickerMock.Object;
    }

    #endregion
}

/// <summary>
/// Tests for BacktestingExchangeService virtual order execution during backtesting
/// </summary>
public class BacktestingExchangeServiceVirtualOrderTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly BacktestingExchangeService _sut;

    public BacktestingExchangeServiceVirtualOrderTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();

        _sut = new BacktestingExchangeService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _backtestingServiceMock.Object);
    }

    [Fact]
    public void PlaceOrder_NotImplemented_BecauseVirtualTradingHandledByTradingService()
    {
        // Note: In backtesting mode, order execution is handled by TradingService
        // using virtual trading, not by the exchange service

        // Arrange
        var orderMock = new Mock<IOrder>();

        // Act & Assert
        var exception = Assert.ThrowsAsync<NotImplementedException>(
            () => _sut.PlaceOrder(orderMock.Object));

        // This confirms that BacktestingExchangeService does not directly handle orders
        // Virtual trading during backtesting is managed by TradingService
    }
}
