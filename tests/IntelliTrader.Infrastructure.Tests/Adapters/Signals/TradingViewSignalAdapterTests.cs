using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Signals;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Signals;

public class TradingViewSignalAdapterTests : IDisposable
{
    private readonly Mock<ISignalsService> _signalsServiceMock;
    private readonly TradingViewSignalAdapter _sut;

    public TradingViewSignalAdapterTests()
    {
        _signalsServiceMock = new Mock<ISignalsService>();
        _sut = new TradingViewSignalAdapter(_signalsServiceMock.Object);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullSignalsService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TradingViewSignalAdapter(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("legacySignals");
    }

    [Fact]
    public void Constructor_WithValidSignalsService_CreatesInstance()
    {
        // Act
        var adapter = new TradingViewSignalAdapter(_signalsServiceMock.Object);

        // Assert
        adapter.Should().NotBeNull();
        adapter.ProviderName.Should().Be("TradingView");
    }

    [Fact]
    public void Constructor_WithCustomQuoteCurrency_CreatesInstance()
    {
        // Act
        var adapter = new TradingViewSignalAdapter(_signalsServiceMock.Object, "BTC");

        // Assert
        adapter.Should().NotBeNull();
    }

    #endregion

    #region GetSignalAsync Tests

    [Fact]
    public async Task GetSignalAsync_WhenSignalExists_ReturnsSignal()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var signalName = "RSI";

        var legacySignal = CreateMockSignal(signalName, pair.Symbol, 0.5);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, signalName);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SignalName.Should().Be(signalName);
        result.Value.Pair.Should().Be(pair);
        result.Value.Rating.Value.Should().Be(0.5);
        result.Value.ProviderName.Should().Be("TradingView");
    }

    [Fact]
    public async Task GetSignalAsync_WhenSignalNotFound_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(Array.Empty<ISignal>());

        // Act
        var result = await _sut.GetSignalAsync(pair, "NonExistent");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetSignalAsync_WhenExceptionOccurs_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Throws(new Exception("Service error"));

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task GetSignalAsync_IsCaseInsensitive()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignal = CreateMockSignal("RSI", pair.Symbol, 0.5);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, "rsi");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.SignalName.Should().Be("RSI");
    }

    #endregion

    #region GetAllSignalsAsync Tests

    [Fact]
    public async Task GetAllSignalsAsync_ReturnsAllSignalsForPair()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var signals = new[]
        {
            CreateMockSignal("RSI", pair.Symbol, 0.5),
            CreateMockSignal("MACD", pair.Symbol, -0.3),
            CreateMockSignal("EMA", pair.Symbol, 0.7)
        };

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(signals);

        // Act
        var result = await _sut.GetAllSignalsAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Select(s => s.SignalName).Should().Contain(new[] { "RSI", "MACD", "EMA" });
    }

    [Fact]
    public async Task GetAllSignalsAsync_ExcludesSignalsWithNullRating()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var signals = new[]
        {
            CreateMockSignal("RSI", pair.Symbol, 0.5),
            CreateMockSignal("MACD", pair.Symbol, null) // No rating
        };

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(signals);

        // Act
        var result = await _sut.GetAllSignalsAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.Single().SignalName.Should().Be("RSI");
    }

    [Fact]
    public async Task GetAllSignalsAsync_WhenNoPairSignals_ReturnsEmptyList()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(Array.Empty<ISignal>());

        // Act
        var result = await _sut.GetAllSignalsAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region GetSignalsForPairsAsync Tests

    [Fact]
    public async Task GetSignalsForPairsAsync_ReturnsSignalsForMultiplePairs()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var ethPair = TradingPair.Create("ETHUSDT", "USDT");
        var pairs = new[] { btcPair, ethPair };

        var allSignals = new[]
        {
            CreateMockSignal("RSI", "BTCUSDT", 0.5),
            CreateMockSignal("MACD", "BTCUSDT", 0.3),
            CreateMockSignal("RSI", "ETHUSDT", -0.2)
        };

        _signalsServiceMock
            .Setup(x => x.GetAllSignals())
            .Returns(allSignals);

        // Act
        var result = await _sut.GetSignalsForPairsAsync(pairs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[btcPair].Should().HaveCount(2);
        result.Value[ethPair].Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSignalsForPairsAsync_OmitsPairsWithNoSignals()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var unknownPair = TradingPair.Create("XYZUSDT", "USDT");
        var pairs = new[] { btcPair, unknownPair };

        var allSignals = new[]
        {
            CreateMockSignal("RSI", "BTCUSDT", 0.5)
        };

        _signalsServiceMock
            .Setup(x => x.GetAllSignals())
            .Returns(allSignals);

        // Act
        var result = await _sut.GetSignalsForPairsAsync(pairs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.ContainsKey(btcPair).Should().BeTrue();
        result.Value.ContainsKey(unknownPair).Should().BeFalse();
    }

    #endregion

    #region GetAggregatedSignalAsync Tests

    [Fact]
    public async Task GetAggregatedSignalAsync_ReturnsAggregatedSignal()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var signals = new[]
        {
            CreateMockSignal("RSI", pair.Symbol, 0.5),   // Buy
            CreateMockSignal("MACD", pair.Symbol, 0.3),  // Buy
            CreateMockSignal("EMA", pair.Symbol, -0.2),  // Sell
            CreateMockSignal("SMA", pair.Symbol, 0.0)    // Neutral
        };

        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(new[] { "RSI", "MACD", "EMA", "SMA" });

        _signalsServiceMock
            .Setup(x => x.GetRating(pair.Symbol, It.IsAny<IEnumerable<string>>()))
            .Returns(0.15);

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(signals);

        // Act
        var result = await _sut.GetAggregatedSignalAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Pair.Should().Be(pair);
        result.Value.OverallRating.Value.Should().Be(0.15);
        result.Value.BuySignalCount.Should().Be(2);
        result.Value.SellSignalCount.Should().Be(1);
        result.Value.NeutralSignalCount.Should().Be(1);
        result.Value.TotalSignalCount.Should().Be(4);
        result.Value.IndividualSignals.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetAggregatedSignalAsync_WhenNoOverallRating_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(new[] { "RSI" });

        _signalsServiceMock
            .Setup(x => x.GetRating(pair.Symbol, It.IsAny<IEnumerable<string>>()))
            .Returns((double?)null);

        // Act
        var result = await _sut.GetAggregatedSignalAsync(pair);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task GetAggregatedSignalAsync_CalculatesPercentagesCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var signals = new[]
        {
            CreateMockSignal("RSI", pair.Symbol, 0.5),   // Buy
            CreateMockSignal("MACD", pair.Symbol, 0.3),  // Buy
        };

        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(new[] { "RSI", "MACD" });

        _signalsServiceMock
            .Setup(x => x.GetRating(pair.Symbol, It.IsAny<IEnumerable<string>>()))
            .Returns(0.4);

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(signals);

        // Act
        var result = await _sut.GetAggregatedSignalAsync(pair);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.BuyPercentage.Should().Be(100m);
        result.Value.SellPercentage.Should().Be(0m);
    }

    #endregion

    #region Signal Type Determination Tests

    [Theory]
    [InlineData("RSI", SignalType.Oscillator)]
    [InlineData("Stoch_RSI", SignalType.Oscillator)]
    [InlineData("CCI", SignalType.Oscillator)]
    [InlineData("Williams_R", SignalType.Oscillator)]
    [InlineData("SMA", SignalType.MovingAverage)]
    [InlineData("EMA", SignalType.MovingAverage)]
    [InlineData("ICHIMOKU", SignalType.MovingAverage)]
    [InlineData("MACD", SignalType.Trend)]
    [InlineData("ADX", SignalType.Trend)]
    [InlineData("PSAR", SignalType.Trend)]
    [InlineData("Volume", SignalType.Volume)]
    [InlineData("OBV", SignalType.Volume)]
    [InlineData("Recommend_All", SignalType.Summary)]
    [InlineData("Summary", SignalType.Summary)]
    [InlineData("Custom_Indicator", SignalType.Technical)]
    public async Task GetSignalAsync_DeterminesCorrectSignalType(string signalName, SignalType expectedType)
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignal = CreateMockSignal(signalName, pair.Symbol, 0.5);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, signalName);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Type.Should().Be(expectedType);
    }

    #endregion

    #region Rating Conversion Tests

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(-0.5, -0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, -1.0)]
    [InlineData(0.0, 0.0)]
    public async Task GetSignalAsync_ConvertsRatingCorrectly(double legacyRating, double expectedRating)
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignal = CreateMockSignal("RSI", pair.Symbol, legacyRating);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Rating.Value.Should().Be(expectedRating);
    }

    [Theory]
    [InlineData(1.5, 1.0)]   // Clamped to max
    [InlineData(-1.5, -1.0)] // Clamped to min
    [InlineData(2.0, 1.0)]   // Clamped to max
    public async Task GetSignalAsync_ClampsOutOfRangeRatings(double legacyRating, double expectedRating)
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignal = CreateMockSignal("RSI", pair.Symbol, legacyRating);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Rating.Value.Should().Be(expectedRating);
    }

    #endregion

    #region Signal Properties Tests

    [Fact]
    public async Task GetSignalAsync_SetsIsBuySignalCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignal = CreateMockSignal("RSI", pair.Symbol, 0.5);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsBuySignal.Should().BeTrue();
        result.Value.IsSellSignal.Should().BeFalse();
    }

    [Fact]
    public async Task GetSignalAsync_SetsIsSellSignalCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignal = CreateMockSignal("RSI", pair.Symbol, -0.5);
        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignal });

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsBuySignal.Should().BeFalse();
        result.Value.IsSellSignal.Should().BeTrue();
    }

    [Fact]
    public async Task GetSignalAsync_IncludesMetadata()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignalMock = new Mock<ISignal>();
        legacySignalMock.Setup(x => x.Name).Returns("RSI");
        legacySignalMock.Setup(x => x.Pair).Returns(pair.Symbol);
        legacySignalMock.Setup(x => x.Rating).Returns(0.5);
        legacySignalMock.Setup(x => x.Price).Returns(50000m);
        legacySignalMock.Setup(x => x.PriceChange).Returns(2.5m);
        legacySignalMock.Setup(x => x.Volume).Returns(1000000L);
        legacySignalMock.Setup(x => x.Volatility).Returns(0.15);

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignalMock.Object });

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Metadata.Should().NotBeNull();
        result.Value.Metadata!["price"].Should().Be(50000m);
        result.Value.Metadata["priceChange"].Should().Be(2.5m);
        result.Value.Metadata["volume"].Should().Be(1000000L);
        result.Value.Metadata["volatility"].Should().Be(0.15);
    }

    [Fact]
    public async Task GetSignalAsync_IncludesDescription()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        var legacySignalMock = new Mock<ISignal>();
        legacySignalMock.Setup(x => x.Name).Returns("RSI");
        legacySignalMock.Setup(x => x.Pair).Returns(pair.Symbol);
        legacySignalMock.Setup(x => x.Rating).Returns(0.5);
        legacySignalMock.Setup(x => x.Price).Returns(50000m);
        legacySignalMock.Setup(x => x.PriceChange).Returns(2.5m);

        _signalsServiceMock
            .Setup(x => x.GetSignalsByPair(pair.Symbol))
            .Returns(new[] { legacySignalMock.Object });

        // Act
        var result = await _sut.GetSignalAsync(pair, "RSI");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Description.Should().NotBeNullOrEmpty();
        result.Value.Description.Should().Contain("Price");
        result.Value.Description.Should().Contain("Change");
    }

    #endregion

    #region Observable Tests

    [Fact]
    public void SubscribeToSignals_FiltersSignalsByPair()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var ethPair = TradingPair.Create("ETHUSDT", "USDT");

        var receivedSignals = new List<TradingSignal>();
        using var subscription = _sut.SubscribeToSignals(btcPair)
            .Subscribe(s => receivedSignals.Add(s));

        var btcSignal = CreateTradingSignal("RSI", btcPair, 0.5);
        var ethSignal = CreateTradingSignal("RSI", ethPair, -0.3);

        // Act
        _sut.PublishSignalUpdate(btcSignal);
        _sut.PublishSignalUpdate(ethSignal);

        // Assert
        receivedSignals.Should().HaveCount(1);
        receivedSignals.Single().Pair.Should().Be(btcPair);
    }

    [Fact]
    public void SubscribeToAllSignals_ReceivesAllSignals()
    {
        // Arrange
        var btcPair = TradingPair.Create("BTCUSDT", "USDT");
        var ethPair = TradingPair.Create("ETHUSDT", "USDT");

        var receivedSignals = new List<TradingSignal>();
        using var subscription = _sut.SubscribeToAllSignals()
            .Subscribe(s => receivedSignals.Add(s));

        var btcSignal = CreateTradingSignal("RSI", btcPair, 0.5);
        var ethSignal = CreateTradingSignal("RSI", ethPair, -0.3);

        // Act
        _sut.PublishSignalUpdate(btcSignal);
        _sut.PublishSignalUpdate(ethSignal);

        // Assert
        receivedSignals.Should().HaveCount(2);
    }

    #endregion

    #region TestConnectivityAsync Tests

    [Fact]
    public async Task TestConnectivityAsync_WhenConnected_ReturnsTrue()
    {
        // Arrange
        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(new[] { "RSI", "MACD" });

        // Act
        var result = await _sut.TestConnectivityAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectivityAsync_WhenNoSignals_ReturnsFalse()
    {
        // Arrange
        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Returns(Array.Empty<string>());

        // Act
        var result = await _sut.TestConnectivityAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectivityAsync_WhenExceptionOccurs_ReturnsFailure()
    {
        // Arrange
        _signalsServiceMock
            .Setup(x => x.GetSignalNames())
            .Throws(new Exception("Connection failed"));

        // Act
        var result = await _sut.TestConnectivityAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
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

    private static TradingSignal CreateTradingSignal(string name, TradingPair pair, double rating)
    {
        return new TradingSignal
        {
            SignalName = name,
            Pair = pair,
            Rating = Domain.Signals.ValueObjects.SignalRating.Create(rating),
            Type = SignalType.Technical,
            ProviderName = "TradingView",
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
