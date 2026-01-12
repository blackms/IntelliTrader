using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Exchange;
using IntelliTrader.Infrastructure.Adapters.Signals;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests for infrastructure adapters working together.
/// </summary>
public class AdapterIntegrationTests : IDisposable
{
    private readonly Mock<IExchangeService> _exchangeServiceMock;
    private readonly Mock<ISignalsService> _signalsServiceMock;
    private readonly BinanceExchangeAdapter _exchangeAdapter;
    private readonly TradingViewSignalAdapter _signalAdapter;

    public AdapterIntegrationTests()
    {
        _exchangeServiceMock = new Mock<IExchangeService>();
        _signalsServiceMock = new Mock<ISignalsService>();
        _exchangeAdapter = new BinanceExchangeAdapter(_exchangeServiceMock.Object);
        _signalAdapter = new TradingViewSignalAdapter(_signalsServiceMock.Object);
    }

    public void Dispose()
    {
        _signalAdapter.Dispose();
    }

    #region Exchange and Signal Adapter Coordination

    [Fact]
    public async Task ExchangeAndSignal_GetPriceAndSignalForPair_ReturnsConsistentData()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var expectedPrice = 50000m;

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ReturnsAsync(expectedPrice);

        var signalMock = CreateMockSignal("RSI", pair.Symbol, 0.6);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { signalMock });

        // Act
        var priceResult = await _exchangeAdapter.GetCurrentPriceAsync(pair);
        var signalResult = await _signalAdapter.GetAllSignalsAsync(pair);

        // Assert
        priceResult.IsSuccess.Should().BeTrue();
        priceResult.Value.Value.Should().Be(expectedPrice);

        signalResult.IsSuccess.Should().BeTrue();
        signalResult.Value.Should().HaveCount(1);
        signalResult.Value[0].Pair.Should().Be(pair);
    }

    [Fact]
    public async Task ExchangeAndSignal_GetDataForMultiplePairs_ReturnsAllData()
    {
        // Arrange
        var pairs = new[]
        {
            TradingPair.Create("BTCUSDT", "USDT"),
            TradingPair.Create("ETHUSDT", "USDT"),
            TradingPair.Create("ADAUSDT", "USDT")
        };

        foreach (var pair in pairs)
        {
            _exchangeServiceMock
                .Setup(x => x.GetLastPrice(pair.Symbol))
                .ReturnsAsync(100m);
        }

        var allSignals = pairs.Select(p => CreateMockSignal("RSI", p.Symbol, 0.5)).ToArray();
        _signalsServiceMock
            .Setup(x => x.GetAllSignals())
            .Returns(allSignals);

        // Act
        var signalsResult = await _signalAdapter.GetSignalsForPairsAsync(pairs);

        // Assert
        signalsResult.IsSuccess.Should().BeTrue();
        signalsResult.Value.Should().HaveCount(3);
    }

    #endregion

    #region Signal Rating Aggregation

    [Fact]
    public async Task SignalAdapter_AggregatesSignalsCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var signals = new[]
        {
            CreateMockSignal("RSI", pair.Symbol, 0.7),
            CreateMockSignal("MACD", pair.Symbol, 0.5),
            CreateMockSignal("EMA", pair.Symbol, 0.3),
            CreateMockSignal("SMA", pair.Symbol, -0.2),
            CreateMockSignal("Recommend_All", pair.Symbol, 0.4)
        };

        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(signals.Select(s => s.Name).ToArray());

        _signalsServiceMock
            .Setup(x => x.GetRating(pair.Symbol, It.IsAny<IEnumerable<string>>()))
            .Returns(0.34); // Average-ish

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(signals);

        // Act
        var result = await _signalAdapter.GetAggregatedSignalAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.BuySignalCount.Should().Be(4); // RSI, MACD, EMA, Recommend_All > 0
        result.Value.SellSignalCount.Should().Be(1); // SMA < 0
        result.Value.TotalSignalCount.Should().Be(5);
    }

    [Fact]
    public async Task SignalAdapter_CategorizesSignalTypesCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var signals = new[]
        {
            CreateMockSignal("RSI", pair.Symbol, 0.5),
            CreateMockSignal("MACD", pair.Symbol, 0.5),
            CreateMockSignal("EMA", pair.Symbol, 0.5),
            CreateMockSignal("Volume", pair.Symbol, 0.5),
            CreateMockSignal("Recommend_All", pair.Symbol, 0.5)
        };

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(signals);

        // Act
        var result = await _signalAdapter.GetAllSignalsAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var signalTypes = result.Value.ToDictionary(s => s.SignalName, s => s.Type);
        signalTypes["RSI"].Should().Be(SignalType.Oscillator);
        signalTypes["MACD"].Should().Be(SignalType.Trend);
        signalTypes["EMA"].Should().Be(SignalType.MovingAverage);
        signalTypes["Volume"].Should().Be(SignalType.Volume);
        signalTypes["Recommend_All"].Should().Be(SignalType.Summary);
    }

    #endregion

    #region Observable Integration

    [Fact]
    public async Task SignalAdapter_ObservableEmitsUpdates()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var receivedSignals = new List<TradingSignal>();
        var signalReceived = new TaskCompletionSource<bool>();

        using var subscription = _signalAdapter.SubscribeToSignals(pair)
            .Subscribe(signal =>
            {
                receivedSignals.Add(signal);
                signalReceived.TrySetResult(true);
            });

        var testSignal = new TradingSignal
        {
            SignalName = "RSI",
            Pair = pair,
            Rating = Domain.Signals.ValueObjects.SignalRating.Create(0.7),
            Type = SignalType.Oscillator,
            ProviderName = "TradingView",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        _signalAdapter.PublishSignalUpdate(testSignal);

        // Wait for signal with timeout
        var completed = await Task.WhenAny(
            signalReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(1)));

        // Assert
        completed.Should().Be(signalReceived.Task);
        receivedSignals.Should().HaveCount(1);
        receivedSignals[0].SignalName.Should().Be("RSI");
    }

    [Fact]
    public async Task SignalAdapter_ObservableFiltersbyPair()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var ethPair = TradingPair.Create("ETHUSDT", "USDT");

        var btcSignals = new List<TradingSignal>();
        var allSignals = new List<TradingSignal>();

        using var btcSubscription = _signalAdapter.SubscribeToSignals(btcPair)
            .Subscribe(signal => btcSignals.Add(signal));

        using var allSubscription = _signalAdapter.SubscribeToAllSignals()
            .Subscribe(signal => allSignals.Add(signal));

        var btcSignal = new TradingSignal
        {
            SignalName = "RSI",
            Pair = btcPair,
            Rating = Domain.Signals.ValueObjects.SignalRating.Create(0.7),
            Type = SignalType.Oscillator,
            ProviderName = "TradingView",
            Timestamp = DateTimeOffset.UtcNow
        };

        var ethSignal = new TradingSignal
        {
            SignalName = "RSI",
            Pair = ethPair,
            Rating = Domain.Signals.ValueObjects.SignalRating.Create(0.5),
            Type = SignalType.Oscillator,
            ProviderName = "TradingView",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        _signalAdapter.PublishSignalUpdate(btcSignal);
        _signalAdapter.PublishSignalUpdate(ethSignal);
        await Task.Delay(50); // Allow time for observable processing

        // Assert
        btcSignals.Should().HaveCount(1);
        btcSignals[0].Pair.Should().Be(btcPair);

        allSignals.Should().HaveCount(2);
    }

    #endregion

    #region Error Handling Integration

    [Fact]
    public async Task ExchangeAdapter_HandlesServiceErrors_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ThrowsAsync(new Exception("Exchange service unavailable"));

        // Act
        var result = await _exchangeAdapter.GetCurrentPriceAsync(pair);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task SignalAdapter_HandlesServiceErrors_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Throws(new Exception("Signal service unavailable"));

        // Act
        var result = await _signalAdapter.GetAllSignalsAsync(pair);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task BothAdapters_HandleSimultaneousFailures_ReturnIndependentResults()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _exchangeServiceMock
            .Setup(x => x.GetLastPrice(pair.Symbol))
            .ThrowsAsync(new Exception("Exchange error"));

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { CreateMockSignal("RSI", pair.Symbol, 0.5) });

        // Act
        var priceTask = _exchangeAdapter.GetCurrentPriceAsync(pair);
        var signalTask = _signalAdapter.GetAllSignalsAsync(pair);

        await Task.WhenAll(priceTask, signalTask);

        // Assert
        priceTask.Result.IsFailure.Should().BeTrue();
        signalTask.Result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Connectivity Tests

    [Fact]
    public async Task ExchangeAdapter_TestConnectivity_ReturnsSuccess()
    {
        // Arrange
        var tickerMock = new Mock<ITicker>();
        _exchangeServiceMock
            .Setup(x => x.GetTickers("USDT"))
            .ReturnsAsync(new[] { tickerMock.Object });

        // Act
        var result = await _exchangeAdapter.TestConnectivityAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SignalAdapter_TestConnectivity_ReturnsSuccess()
    {
        // Arrange
        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(new[] { "RSI", "MACD" });

        // Act
        var result = await _signalAdapter.TestConnectivityAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task BothAdapters_ConnectivityCheckInParallel_BothSucceed()
    {
        // Arrange
        var tickerMock = new Mock<ITicker>();
        _exchangeServiceMock
            .Setup(x => x.GetTickers("USDT"))
            .ReturnsAsync(new[] { tickerMock.Object });

        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(new[] { "RSI" });

        // Act
        var exchangeTask = _exchangeAdapter.TestConnectivityAsync();
        var signalTask = _signalAdapter.TestConnectivityAsync();

        await Task.WhenAll(exchangeTask, signalTask);

        // Assert
        exchangeTask.Result.IsSuccess.Should().BeTrue();
        exchangeTask.Result.Value.Should().BeTrue();

        signalTask.Result.IsSuccess.Should().BeTrue();
        signalTask.Result.Value.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static ISignal CreateMockSignal(string name, string pair, double? rating)
    {
        var mock = new Mock<ISignal>();
        mock.Setup(x => x.Name).Returns(name);
        mock.Setup(x => x.Pair).Returns(pair);
        mock.Setup(x => x.Rating).Returns(rating);
        mock.Setup(x => x.Price).Returns((decimal?)null);
        mock.Setup(x => x.PriceChange).Returns((decimal?)null);
        mock.Setup(x => x.Volume).Returns((long?)null);
        mock.Setup(x => x.Volatility).Returns((double?)null);
        return mock.Object;
    }

    #endregion
}
