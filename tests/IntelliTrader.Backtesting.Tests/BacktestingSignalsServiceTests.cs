using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Backtesting;
using IntelliTrader.Signals.Base;

namespace IntelliTrader.Backtesting.Tests;

public class BacktestingSignalsServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<IRulesService> _rulesServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
    private readonly Mock<IModuleRules> _moduleRulesMock;
    private readonly Mock<ISignalsConfig> _signalsConfigMock;
    private readonly BacktestingSignalsService _sut;

    public BacktestingSignalsServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _rulesServiceMock = new Mock<IRulesService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();
        _configProviderMock = new Mock<IConfigProvider>();
        _moduleRulesMock = new Mock<IModuleRules>();
        _signalsConfigMock = new Mock<ISignalsConfig>();

        // Setup default rules
        _moduleRulesMock.Setup(x => x.GetConfiguration<SignalRulesConfig>())
            .Returns(new SignalRulesConfig());
        _rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>()))
            .Returns(_moduleRulesMock.Object);

        // Setup default empty signals
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Setup signals config
        _signalsConfigMock.Setup(x => x.GlobalRatingSignals)
            .Returns(new List<string> { "TradingView" });

        _sut = new BacktestingSignalsService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            _configProviderMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLoggingService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new BacktestingSignalsService(
            null!,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            _configProviderMock.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("loggingService");
    }

    [Fact]
    public void Constructor_WithNullHealthCheckService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new BacktestingSignalsService(
            _loggingServiceMock.Object,
            null!,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            _configProviderMock.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("healthCheckService");
    }

    [Fact]
    public void Constructor_WithNullTradingService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new BacktestingSignalsService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            null!,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            _configProviderMock.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("tradingService");
    }

    [Fact]
    public void Constructor_WithNullRulesService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new BacktestingSignalsService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            null!,
            _backtestingServiceMock.Object,
            _configProviderMock.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("rulesService");
    }

    [Fact]
    public void Constructor_WithNullBacktestingService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new BacktestingSignalsService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            null!,
            _configProviderMock.Object);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("backtestingService");
    }

    [Fact]
    public void Constructor_WithNullConfigProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new BacktestingSignalsService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("configProvider");
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Assert
        _sut.Should().NotBeNull();
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsSignalsService()
    {
        // Act
        var serviceName = _sut.ServiceName;

        // Assert
        serviceName.Should().Be(Constants.ServiceNames.SignalsService);
    }

    #endregion

    #region Start Tests

    [Fact]
    public void Start_LogsStartMessage()
    {
        // Act
        _sut.Start();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Start Backtesting Signals service")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Start_RegistersRulesChangeCallback()
    {
        // Act
        _sut.Start();

        // Assert
        _rulesServiceMock.Verify(
            x => x.RegisterRulesChangeCallback(It.IsAny<Action>()),
            Times.Once);
    }

    [Fact]
    public void Start_LogsStartedMessage()
    {
        // Act
        _sut.Start();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Backtesting Signals service started")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Start_InitializesRulesFromRulesService()
    {
        // Act
        _sut.Start();

        // Assert
        _rulesServiceMock.Verify(
            x => x.GetRules(Constants.ServiceNames.SignalsService),
            Times.Once);
    }

    #endregion

    #region Stop Tests

    [Fact]
    public void Stop_LogsStopMessage()
    {
        // Arrange
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Stop Backtesting Signals service")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Stop_UnregistersRulesChangeCallback()
    {
        // Arrange
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        _rulesServiceMock.Verify(
            x => x.UnregisterRulesChangeCallback(It.IsAny<Action>()),
            Times.Once);
    }

    [Fact]
    public void Stop_RemovesSignalRulesHealthCheck()
    {
        // Arrange
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        _healthCheckServiceMock.Verify(
            x => x.RemoveHealthCheck(Constants.HealthChecks.SignalRulesProcessed),
            Times.Once);
    }

    [Fact]
    public void Stop_LogsStoppedMessage()
    {
        // Arrange
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Info(It.Is<string>(s => s.Contains("Backtesting Signals service stopped")), It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region ClearTrailing Tests

    [Fact]
    public void ClearTrailing_ClearsAllTrailingSignals()
    {
        // Act
        _sut.ClearTrailing();
        var trailingSignals = _sut.GetTrailingSignals();

        // Assert
        trailingSignals.Should().BeEmpty();
    }

    #endregion

    #region GetTrailingSignals Tests

    [Fact]
    public void GetTrailingSignals_InitiallyReturnsEmptyList()
    {
        // Act
        var result = _sut.GetTrailingSignals();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetTrailingInfo Tests

    [Fact]
    public void GetTrailingInfo_ReturnsEmptyEnumerable()
    {
        // Act
        var result = _sut.GetTrailingInfo("BTCUSDT");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetTrailingInfo_ForAnyPair_ReturnsEmptyEnumerable()
    {
        // Arrange
        var pairs = new[] { "BTCUSDT", "ETHUSDT", "ADAUSDT", "NONEXISTENT" };

        foreach (var pair in pairs)
        {
            // Act
            var result = _sut.GetTrailingInfo(pair);

            // Assert
            result.Should().BeEmpty();
        }
    }

    #endregion

    #region GetSignalNames Tests

    [Fact]
    public void GetSignalNames_WhenNoSignals_ReturnsEmptyList()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Act
        var result = _sut.GetSignalNames();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetSignalNames_ReturnsDistinctSignalNames()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT"), CreateSignal("CustomSignal", "BTCUSDT") } },
            { "ETHUSDT", new[] { CreateSignal("TradingView", "ETHUSDT"), CreateSignal("CustomSignal", "ETHUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignalNames();

        // Assert
        result.Should().BeEquivalentTo(new[] { "TradingView", "CustomSignal" });
    }

    [Fact]
    public void GetSignalNames_CachesResultOnSubsequentCalls()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("Signal1", "BTCUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result1 = _sut.GetSignalNames();
        var result2 = _sut.GetSignalNames();

        // Assert - Should be the same cached result
        result1.Should().BeSameAs(result2);
    }

    #endregion

    #region GetAllSignals Tests

    [Fact]
    public void GetAllSignals_WhenNoSignals_ReturnsEmptyCollection()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Act
        var result = _sut.GetAllSignals();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetAllSignals_ReturnsAllSignalsFromAllPairs()
    {
        // Arrange
        var signal1 = CreateSignal("TradingView", "BTCUSDT");
        var signal2 = CreateSignal("TradingView", "ETHUSDT");
        var signal3 = CreateSignal("CustomSignal", "BTCUSDT");

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal1, signal3 } },
            { "ETHUSDT", new[] { signal2 } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetAllSignals();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(signal1);
        result.Should().Contain(signal2);
        result.Should().Contain(signal3);
    }

    #endregion

    #region GetSignalsByName Tests

    [Fact]
    public void GetSignalsByName_WithNull_ReturnsAllSignals()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("Signal1", "BTCUSDT"), CreateSignal("Signal2", "BTCUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignalsByName(null!);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetSignalsByName_WithValidName_ReturnsFilteredSignals()
    {
        // Arrange
        var tvSignal1 = CreateSignal("TradingView", "BTCUSDT");
        var tvSignal2 = CreateSignal("TradingView", "ETHUSDT");
        var customSignal = CreateSignal("CustomSignal", "BTCUSDT");

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { tvSignal1, customSignal } },
            { "ETHUSDT", new[] { tvSignal2 } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignalsByName("TradingView");

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(tvSignal1);
        result.Should().Contain(tvSignal2);
        result.Should().NotContain(customSignal);
    }

    [Fact]
    public void GetSignalsByName_WithNonExistentName_ReturnsEmptyCollection()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignalsByName("NonExistent");

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetSignalsByPair Tests

    [Fact]
    public void GetSignalsByPair_WhenPairExists_ReturnsSignals()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT");
        var signal2 = CreateSignal("Signal2", "BTCUSDT");

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal1, signal2 } },
            { "ETHUSDT", new[] { CreateSignal("Signal1", "ETHUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignalsByPair("BTCUSDT");

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(signal1);
        result.Should().Contain(signal2);
    }

    [Fact]
    public void GetSignalsByPair_WhenPairDoesNotExist_ReturnsNull()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("Signal1", "BTCUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignalsByPair("NONEXISTENT");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetSignal Tests

    [Fact]
    public void GetSignal_WhenSignalExists_ReturnsSignal()
    {
        // Arrange
        var targetSignal = CreateSignal("TradingView", "BTCUSDT", rating: 0.8);

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { targetSignal } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignal("BTCUSDT", "TradingView");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(targetSignal);
    }

    [Fact]
    public void GetSignal_WhenSignalDoesNotExist_ReturnsNull()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignal("ETHUSDT", "TradingView");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetSignal_WhenSignalNameDoesNotMatch_ReturnsNull()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT") } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetSignal("BTCUSDT", "NonExistentSignal");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetRating (Single SignalName) Tests

    [Fact]
    public void GetRating_WithExistingSignal_ReturnsRating()
    {
        // Arrange
        var signal = CreateSignal("TradingView", "BTCUSDT", rating: 0.75);
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetRating("BTCUSDT", "TradingView");

        // Assert
        result.Should().Be(0.75);
    }

    [Fact]
    public void GetRating_WhenSignalDoesNotExist_ReturnsNull()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Act
        var result = _sut.GetRating("BTCUSDT", "TradingView");

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void GetRating_ReturnsCorrectRatingValues(double rating)
    {
        // Arrange
        var signal = CreateSignal("TradingView", "BTCUSDT", rating: rating);
        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetRating("BTCUSDT", "TradingView");

        // Assert
        result.Should().Be(rating);
    }

    #endregion

    #region GetRating (Multiple SignalNames) Tests

    [Fact]
    public void GetRating_WithMultipleSignals_ReturnsAverageRating()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", rating: 0.6);
        var signal2 = CreateSignal("Signal2", "BTCUSDT", rating: 0.8);

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal1, signal2 } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1", "Signal2" });

        // Assert
        result.Should().Be(0.7); // (0.6 + 0.8) / 2
    }

    [Fact]
    public void GetRating_WithEmptySignalNames_ReturnsNull()
    {
        // Act
        var result = _sut.GetRating("BTCUSDT", Enumerable.Empty<string>());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithNullSignalNames_ReturnsNull()
    {
        // Act
        var result = _sut.GetRating("BTCUSDT", (IEnumerable<string>)null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WhenOneSignalMissing_ReturnsNull()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", rating: 0.6);

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal1 } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1", "NonExistentSignal" });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithMultipleSignals_RoundsToEightDecimalPlaces()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", rating: 0.333333333);
        var signal2 = CreateSignal("Signal2", "BTCUSDT", rating: 0.666666666);

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal1, signal2 } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1", "Signal2" });

        // Assert
        result.Should().NotBeNull();
        var decimalPlaces = BitConverter.GetBytes(decimal.GetBits((decimal)result.Value)[3])[2];
        decimalPlaces.Should().BeLessThanOrEqualTo(8);
    }

    #endregion

    #region GetGlobalRating Tests

    [Fact]
    public void GetGlobalRating_WhenNoSignals_ReturnsNull()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetGlobalRating_WhenExceptionOccurs_ReturnsNull()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Throws(new Exception("Test exception"));

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetGlobalRating_WhenExceptionOccurs_LogsError()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Throws(new Exception("Test exception"));

        // Act
        _sut.GetGlobalRating();

        // Assert
        _loggingServiceMock.Verify(
            x => x.Error(It.Is<string>(s => s.Contains("Unable to get global rating")), It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region Rules Property Tests

    [Fact]
    public void Rules_AfterStart_ReturnsModuleRules()
    {
        // Arrange
        _sut.Start();

        // Act
        var rules = _sut.Rules;

        // Assert
        rules.Should().NotBeNull();
        rules.Should().Be(_moduleRulesMock.Object);
    }

    #endregion

    #region Snapshot Replay Tests

    [Fact]
    public void GetAllSignals_ReturnsSignalsFromCurrentSnapshot()
    {
        // Arrange - Simulate snapshot 1
        var snapshot1Signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT", rating: 0.5) } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(snapshot1Signals);

        // Act
        var result1 = _sut.GetAllSignals().ToList();

        // Arrange - Simulate snapshot 2 (new snapshot loaded)
        var snapshot2Signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT", rating: 0.7) } },
            { "ETHUSDT", new[] { CreateSignal("TradingView", "ETHUSDT", rating: 0.6) } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(snapshot2Signals);

        var result2 = _sut.GetAllSignals().ToList();

        // Assert
        result1.Should().HaveCount(1);
        result2.Should().HaveCount(2);
        result2.First(s => s.Pair == "BTCUSDT").Rating.Should().Be(0.7);
    }

    [Fact]
    public void GetRating_ReflectsCurrentSnapshotData()
    {
        // Arrange - Initial snapshot
        var initialSignals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT", rating: 0.3) } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(initialSignals);

        var initialRating = _sut.GetRating("BTCUSDT", "TradingView");

        // Update snapshot
        var updatedSignals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateSignal("TradingView", "BTCUSDT", rating: 0.9) } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(updatedSignals);

        var updatedRating = _sut.GetRating("BTCUSDT", "TradingView");

        // Assert
        initialRating.Should().Be(0.3);
        updatedRating.Should().Be(0.9);
    }

    #endregion

    #region Helper Methods

    private static ISignal CreateSignal(string name, string pair, double? rating = 0.5)
    {
        var signalMock = new Mock<ISignal>();
        signalMock.Setup(x => x.Name).Returns(name);
        signalMock.Setup(x => x.Pair).Returns(pair);
        signalMock.Setup(x => x.Rating).Returns(rating);
        signalMock.Setup(x => x.Volume).Returns(1000000);
        signalMock.Setup(x => x.VolumeChange).Returns(5.0);
        signalMock.Setup(x => x.Price).Returns(50000m);
        signalMock.Setup(x => x.PriceChange).Returns(2.5m);
        signalMock.Setup(x => x.RatingChange).Returns(0.1);
        signalMock.Setup(x => x.Volatility).Returns(15.0);
        return signalMock.Object;
    }

    #endregion
}

/// <summary>
/// Tests for BacktestingSignalsService edge cases and performance scenarios
/// </summary>
public class BacktestingSignalsServiceEdgeCaseTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<IRulesService> _rulesServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
    private readonly Mock<IModuleRules> _moduleRulesMock;
    private readonly BacktestingSignalsService _sut;

    public BacktestingSignalsServiceEdgeCaseTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _rulesServiceMock = new Mock<IRulesService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();
        _configProviderMock = new Mock<IConfigProvider>();
        _moduleRulesMock = new Mock<IModuleRules>();

        _moduleRulesMock.Setup(x => x.GetConfiguration<SignalRulesConfig>())
            .Returns(new SignalRulesConfig());
        _rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>()))
            .Returns(_moduleRulesMock.Object);

        _sut = new BacktestingSignalsService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            _backtestingServiceMock.Object,
            _configProviderMock.Object);
    }

    [Fact]
    public void GetSignalsByName_WithEmptyString_ReturnsEmptyCollection()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>
            {
                { "BTCUSDT", new[] { CreateSignal("Signal1", "BTCUSDT") } }
            });

        // Act
        var result = _sut.GetSignalsByName(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetSignalsByPair_WithEmptyString_ReturnsNull()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Act
        var result = _sut.GetSignalsByPair(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithNullRating_ReturnsNull()
    {
        // Arrange
        var signal = new Mock<ISignal>();
        signal.Setup(x => x.Name).Returns("TradingView");
        signal.Setup(x => x.Pair).Returns("BTCUSDT");
        signal.Setup(x => x.Rating).Returns((double?)null);

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal.Object } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetRating("BTCUSDT", "TradingView");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllSignals_WithLargeNumberOfSignals_HandlesCorrectly()
    {
        // Arrange
        var signals = new Dictionary<string, IEnumerable<ISignal>>();
        for (int i = 0; i < 100; i++)
        {
            var pair = $"PAIR{i}USDT";
            signals[pair] = new[]
            {
                CreateSignal("Signal1", pair),
                CreateSignal("Signal2", pair),
                CreateSignal("Signal3", pair)
            };
        }
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _sut.GetAllSignals().ToList();

        // Assert
        result.Should().HaveCount(300); // 100 pairs * 3 signals each
    }

    private static ISignal CreateSignal(string name, string pair)
    {
        var signalMock = new Mock<ISignal>();
        signalMock.Setup(x => x.Name).Returns(name);
        signalMock.Setup(x => x.Pair).Returns(pair);
        signalMock.Setup(x => x.Rating).Returns(0.5);
        return signalMock.Object;
    }
}
