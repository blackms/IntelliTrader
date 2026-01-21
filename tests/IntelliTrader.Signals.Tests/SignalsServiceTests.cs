using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Signals.Base;

namespace IntelliTrader.Signals.Tests;

public class SignalsServiceTests
{
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<IRulesService> _rulesServiceMock;
    private readonly Mock<IModuleRules> _moduleRulesMock;
    private readonly SignalsService _sut;
    private readonly Dictionary<string, Mock<ISignalReceiver>> _receiverMocks;
    private readonly List<SignalDefinition> _definitions;

    public SignalsServiceTests()
    {
        _coreServiceMock = new Mock<ICoreService>();
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _rulesServiceMock = new Mock<IRulesService>();
        _moduleRulesMock = new Mock<IModuleRules>();
        _receiverMocks = new Dictionary<string, Mock<ISignalReceiver>>();
        _definitions = new List<SignalDefinition>();

        // Setup rules service
        _rulesServiceMock.Setup(x => x.GetRules(It.IsAny<string>())).Returns(_moduleRulesMock.Object);
        _moduleRulesMock.Setup(x => x.GetConfiguration<SignalRulesConfig>())
            .Returns(new SignalRulesConfig { CheckInterval = 3, ProcessingMode = RuleProcessingMode.FirstMatch });

        // Signal receiver factory
        Func<string, string, IConfigurationSection?, ISignalReceiver> signalReceiverFactory =
            (receiver, name, config) =>
            {
                if (_receiverMocks.TryGetValue(name, out var mock))
                {
                    return mock.Object;
                }
                return null!;
            };

        _sut = new SignalsService(
            _coreServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            signalReceiverFactory);
    }

    private Mock<ISignalReceiver> CreateMockReceiver(string name, int period, IEnumerable<ISignal>? signals = null, double? averageRating = null)
    {
        var mock = new Mock<ISignalReceiver>();
        mock.Setup(x => x.SignalName).Returns(name);
        mock.Setup(x => x.GetPeriod()).Returns(period);
        mock.Setup(x => x.GetSignals()).Returns(signals ?? new List<ISignal>());
        mock.Setup(x => x.GetAverageRating()).Returns(averageRating);
        _receiverMocks[name] = mock;
        return mock;
    }

    private void AddDefinition(string name, string receiver = "TradingView")
    {
        _definitions.Add(new SignalDefinition
        {
            Name = name,
            Receiver = receiver,
            Configuration = null
        });
    }

    private void SetupConfig(IEnumerable<string>? globalRatingSignals = null)
    {
        var config = new SignalsConfig
        {
            Enabled = true,
            Definitions = _definitions,
            GlobalRatingSignals = globalRatingSignals ?? new List<string>()
        };

        // Use reflection to set the config since ConfigrableServiceBase manages it
        var configField = typeof(SignalsService)
            .BaseType!
            .GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        configField?.SetValue(_sut, config);
    }

    private static ISignal CreateSignal(string name, string pair, double? rating = null)
    {
        return new Signal
        {
            Name = name,
            Pair = pair,
            Rating = rating,
            Price = 100m,
            Volume = 1000,
            Volatility = 0.05
        };
    }

    #region Start Tests

    [Fact]
    public void Start_WithValidDefinitions_StartsAllReceivers()
    {
        // Arrange
        var receiver1Mock = CreateMockReceiver("Signal1", 60);
        var receiver2Mock = CreateMockReceiver("Signal2", 120);
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig();

        // Act
        _sut.Start();

        // Assert
        receiver1Mock.Verify(x => x.Start(), Times.Once);
        receiver2Mock.Verify(x => x.Start(), Times.Once);
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s == "Start Signals service..."), It.IsAny<Exception>()), Times.Once);
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s == "Signals service started"), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void Start_WithDuplicateDefinition_ThrowsException()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        AddDefinition("Signal1"); // Duplicate
        SetupConfig();

        // Act
        Action act = () => _sut.Start();

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("Duplicate signal definition: Signal1");
    }

    [Fact]
    public void Start_WithNullReceiver_ThrowsException()
    {
        // Arrange
        // Don't create a mock receiver - factory will return null
        AddDefinition("NonExistentSignal", "NonExistentReceiver");
        SetupConfig();

        // Act
        Action act = () => _sut.Start();

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("Signal receiver not found: NonExistentReceiver");
    }

    [Fact]
    public void Start_RegistersRulesChangeCallback()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();

        // Act
        _sut.Start();

        // Assert
        _rulesServiceMock.Verify(x => x.RegisterRulesChangeCallback(It.IsAny<Action>()), Times.Once);
    }

    #endregion

    #region Stop Tests

    [Fact]
    public void Stop_StopsAllReceivers()
    {
        // Arrange
        var receiver1Mock = CreateMockReceiver("Signal1", 60);
        var receiver2Mock = CreateMockReceiver("Signal2", 120);
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig();
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        receiver1Mock.Verify(x => x.Stop(), Times.Once);
        receiver2Mock.Verify(x => x.Stop(), Times.Once);
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s == "Stop Signals service..."), It.IsAny<Exception>()), Times.Once);
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s == "Signals service stopped"), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void Stop_UnregistersRulesChangeCallback()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        _rulesServiceMock.Verify(x => x.UnregisterRulesChangeCallback(It.IsAny<Action>()), Times.Once);
    }

    [Fact]
    public void Stop_RemovesHealthCheck()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert
        _healthCheckServiceMock.Verify(
            x => x.RemoveHealthCheck(Constants.HealthChecks.SignalRulesProcessed),
            Times.Once);
    }

    [Fact]
    public void Stop_ClearsReceiversCollection()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        _sut.Stop();

        // Assert - GetSignalNames should return empty after stop
        var signalNames = _sut.GetSignalNames();
        signalNames.Should().BeEmpty();
    }

    #endregion

    #region GetSignalNames Tests

    [Fact]
    public void GetSignalNames_ReturnsOrderedSignalNamesByPeriod()
    {
        // Arrange
        CreateMockReceiver("Signal15Min", 900);  // 15 min period
        CreateMockReceiver("Signal1Min", 60);    // 1 min period
        CreateMockReceiver("Signal5Min", 300);   // 5 min period
        AddDefinition("Signal15Min");
        AddDefinition("Signal1Min");
        AddDefinition("Signal5Min");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalNames().ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().Be("Signal1Min");   // Smallest period first
        result[1].Should().Be("Signal5Min");
        result[2].Should().Be("Signal15Min");  // Largest period last
    }

    [Fact]
    public void GetSignalNames_WithNoReceivers_ReturnsEmpty()
    {
        // Arrange
        SetupConfig();

        // Act
        var result = _sut.GetSignalNames();

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region GetAllSignals Tests

    [Fact]
    public void GetAllSignals_ReturnsSignalsFromAllReceivers()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var signal2 = CreateSignal("Signal2", "ETHUSDT", 0.6);
        CreateMockReceiver("Signal1", 60, new[] { signal1 });
        CreateMockReceiver("Signal2", 120, new[] { signal2 });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetAllSignals()?.ToList();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(signal1);
        result.Should().Contain(signal2);
    }

    [Fact]
    public void GetAllSignals_WithNoReceivers_ReturnsNull()
    {
        // Arrange
        SetupConfig();

        // Act
        var result = _sut.GetAllSignals();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetSignalsByName Tests

    [Fact]
    public void GetSignalsByName_WithValidName_ReturnsFilteredSignals()
    {
        // Arrange
        var signal1a = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var signal1b = CreateSignal("Signal1", "ETHUSDT", 0.7);
        var signal2 = CreateSignal("Signal2", "BTCUSDT", 0.6);
        CreateMockReceiver("Signal1", 60, new[] { signal1a, signal1b });
        CreateMockReceiver("Signal2", 120, new[] { signal2 });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByName("Signal1")?.ToList();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(signal1a);
        result.Should().Contain(signal1b);
        result.Should().NotContain(signal2);
    }

    [Fact]
    public void GetSignalsByName_WithNullName_ReturnsAllSignals()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var signal2 = CreateSignal("Signal2", "ETHUSDT", 0.6);
        CreateMockReceiver("Signal1", 60, new[] { signal1 });
        CreateMockReceiver("Signal2", 120, new[] { signal2 });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByName(null)?.ToList();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(signal1);
        result.Should().Contain(signal2);
    }

    [Fact]
    public void GetSignalsByName_WithNonExistentName_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByName("NonExistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetSignalsByPair Tests

    [Fact]
    public void GetSignalsByPair_ReturnsSignalsFromAllReceivers()
    {
        // Arrange
        var btcSignal1 = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var ethSignal1 = CreateSignal("Signal1", "ETHUSDT", 0.7);
        var btcSignal2 = CreateSignal("Signal2", "BTCUSDT", 0.6);
        CreateMockReceiver("Signal1", 60, new[] { btcSignal1, ethSignal1 });
        CreateMockReceiver("Signal2", 120, new[] { btcSignal2 });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByPair("BTCUSDT").ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(btcSignal1);
        result.Should().Contain(btcSignal2);
        result.Should().NotContain(ethSignal1);
    }

    [Fact]
    public void GetSignalsByPair_WithNonExistentPair_ReturnsEmpty()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByPair("NONEXISTENT").ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetSignalsByPair_OrdersByPeriod()
    {
        // Arrange
        var btcSignal15 = CreateSignal("Signal15Min", "BTCUSDT", 0.8);
        var btcSignal1 = CreateSignal("Signal1Min", "BTCUSDT", 0.7);
        var btcSignal5 = CreateSignal("Signal5Min", "BTCUSDT", 0.6);
        CreateMockReceiver("Signal15Min", 900, new[] { btcSignal15 });
        CreateMockReceiver("Signal1Min", 60, new[] { btcSignal1 });
        CreateMockReceiver("Signal5Min", 300, new[] { btcSignal5 });
        AddDefinition("Signal15Min");
        AddDefinition("Signal1Min");
        AddDefinition("Signal5Min");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByPair("BTCUSDT").ToList();

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().Be(btcSignal1);    // Period 60
        result[1].Should().Be(btcSignal5);    // Period 300
        result[2].Should().Be(btcSignal15);   // Period 900
    }

    #endregion

    #region GetSignal Tests

    [Fact]
    public void GetSignal_WithValidPairAndSignal_ReturnsSignal()
    {
        // Arrange
        var btcSignal = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var ethSignal = CreateSignal("Signal1", "ETHUSDT", 0.7);
        CreateMockReceiver("Signal1", 60, new[] { btcSignal, ethSignal });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignal("BTCUSDT", "Signal1");

        // Assert
        result.Should().Be(btcSignal);
    }

    [Fact]
    public void GetSignal_WithNonExistentPair_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignal("NONEXISTENT", "Signal1");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetSignal_WithNonExistentSignalName_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignal("BTCUSDT", "NonExistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetRating (Single Signal) Tests

    [Fact]
    public void GetRating_WithValidPairAndSignal_ReturnsRating()
    {
        // Arrange
        var signal = CreateSignal("Signal1", "BTCUSDT", 0.85);
        CreateMockReceiver("Signal1", 60, new[] { signal });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", "Signal1");

        // Assert
        result.Should().Be(0.85);
    }

    [Fact]
    public void GetRating_WithMissingSignal_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("ETHUSDT", "Signal1"); // Pair doesn't exist

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithNonExistentSignalName_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", "NonExistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithSignalWithNullRating_ReturnsNull()
    {
        // Arrange
        var signalWithNullRating = CreateSignal("Signal1", "BTCUSDT", null);
        CreateMockReceiver("Signal1", 60, new[] { signalWithNullRating });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", "Signal1");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetRating (Multiple Signals) Tests

    [Fact]
    public void GetRating_WithMultipleSignals_ReturnsAverage()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var signal2 = CreateSignal("Signal2", "BTCUSDT", 0.6);
        var signal3 = CreateSignal("Signal3", "BTCUSDT", 0.4);
        CreateMockReceiver("Signal1", 60, new[] { signal1 });
        CreateMockReceiver("Signal2", 120, new[] { signal2 });
        CreateMockReceiver("Signal3", 180, new[] { signal3 });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        AddDefinition("Signal3");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1", "Signal2", "Signal3" });

        // Assert
        // Average of 0.8, 0.6, 0.4 = 1.8 / 3 = 0.6
        result.Should().Be(0.6);
    }

    [Fact]
    public void GetRating_WithMultipleSignals_RoundsTo8DecimalPlaces()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", 0.333333333);
        var signal2 = CreateSignal("Signal2", "BTCUSDT", 0.333333333);
        var signal3 = CreateSignal("Signal3", "BTCUSDT", 0.333333334);
        CreateMockReceiver("Signal1", 60, new[] { signal1 });
        CreateMockReceiver("Signal2", 120, new[] { signal2 });
        CreateMockReceiver("Signal3", 180, new[] { signal3 });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        AddDefinition("Signal3");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1", "Signal2", "Signal3" });

        // Assert
        result.Should().NotBeNull();
        result.ToString()!.Length.Should().BeLessThanOrEqualTo(10); // "0." + 8 digits max
    }

    [Fact]
    public void GetRating_WithMultipleSignalsAndOneMissing_ReturnsNull()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var signal2 = CreateSignal("Signal2", "BTCUSDT", 0.6);
        // Signal3 doesn't have BTCUSDT
        CreateMockReceiver("Signal1", 60, new[] { signal1 });
        CreateMockReceiver("Signal2", 120, new[] { signal2 });
        CreateMockReceiver("Signal3", 180, new[] { CreateSignal("Signal3", "ETHUSDT", 0.4) });
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        AddDefinition("Signal3");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1", "Signal2", "Signal3" });

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithEmptySignalNames_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", new List<string>());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithNullSignalNames_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, new[] { CreateSignal("Signal1", "BTCUSDT", 0.8) });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", (IEnumerable<string>)null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetRating_WithSingleSignalInList_ReturnsThatRating()
    {
        // Arrange
        var signal1 = CreateSignal("Signal1", "BTCUSDT", 0.75);
        CreateMockReceiver("Signal1", 60, new[] { signal1 });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", new[] { "Signal1" });

        // Assert
        result.Should().Be(0.75);
    }

    #endregion

    #region GetGlobalRating Tests

    [Fact]
    public void GetGlobalRating_CalculatesCorrectAverage()
    {
        // Arrange
        var receiver1Mock = CreateMockReceiver("Signal1", 60, averageRating: 0.8);
        var receiver2Mock = CreateMockReceiver("Signal2", 120, averageRating: 0.6);
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig(new[] { "Signal1", "Signal2" }); // Both signals in global rating
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        // Average of 0.8 and 0.6 = 0.7
        result.Should().Be(0.7);
    }

    [Fact]
    public void GetGlobalRating_OnlyIncludesConfiguredSignals()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, averageRating: 0.8);
        CreateMockReceiver("Signal2", 120, averageRating: 0.6);
        CreateMockReceiver("Signal3", 180, averageRating: 0.4); // Not in global rating
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        AddDefinition("Signal3");
        SetupConfig(new[] { "Signal1", "Signal2" }); // Only Signal1 and Signal2
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        // Average of 0.8 and 0.6 = 0.7 (Signal3 excluded)
        result.Should().Be(0.7);
    }

    [Fact]
    public void GetGlobalRating_WithNoSignals_ReturnsNull()
    {
        // Arrange
        SetupConfig(new List<string>()); // Empty global rating signals
        // Don't start any receivers

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetGlobalRating_WithNoConfiguredGlobalSignals_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, averageRating: 0.8);
        AddDefinition("Signal1");
        SetupConfig(new List<string>()); // No signals configured for global rating
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetGlobalRating_WhenReceiverReturnsNullRating_ExcludesFromCalculation()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, averageRating: 0.8);
        CreateMockReceiver("Signal2", 120, averageRating: null); // Null rating
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig(new[] { "Signal1", "Signal2" });
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        // Only Signal1 contributes, so result is 0.8
        result.Should().Be(0.8);
    }

    [Fact]
    public void GetGlobalRating_WhenAllReceiversReturnNull_ReturnsNull()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, averageRating: null);
        CreateMockReceiver("Signal2", 120, averageRating: null);
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        SetupConfig(new[] { "Signal1", "Signal2" });
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetGlobalRating_RoundsTo8DecimalPlaces()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, averageRating: 0.333333333);
        CreateMockReceiver("Signal2", 120, averageRating: 0.333333333);
        CreateMockReceiver("Signal3", 180, averageRating: 0.333333334);
        AddDefinition("Signal1");
        AddDefinition("Signal2");
        AddDefinition("Signal3");
        SetupConfig(new[] { "Signal1", "Signal2", "Signal3" });
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().NotBeNull();
        result.ToString()!.Length.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public void GetGlobalRating_WhenExceptionOccurs_ReturnsNullAndLogsError()
    {
        // Arrange
        var receiver1Mock = CreateMockReceiver("Signal1", 60);
        receiver1Mock.Setup(x => x.GetAverageRating()).Throws(new InvalidOperationException("Test exception"));
        AddDefinition("Signal1");
        SetupConfig(new[] { "Signal1" });
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().BeNull();
        _loggingServiceMock.Verify(
            x => x.Error("Unable to get global rating", It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region Trailing Signals Tests

    [Fact]
    public void GetTrailingSignals_InitiallyReturnsEmptyList()
    {
        // Arrange
        SetupConfig();

        // Act
        var result = _sut.GetTrailingSignals();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ClearTrailing_ClearsAllTrailingSignals()
    {
        // Arrange
        SetupConfig();
        // Note: trailingSignals is internal, but we can test the clear operation

        // Act
        _sut.ClearTrailing();

        // Assert
        var result = _sut.GetTrailingSignals();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetTrailingInfo_ReturnsEmptyEnumerable()
    {
        // Arrange
        SetupConfig();

        // Act
        var result = _sut.GetTrailingInfo("BTCUSDT");

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Constructor Validation Tests

    [Fact]
    public void Constructor_WithNullCoreService_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalsService(
            null!,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            (r, n, c) => null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("coreService");
    }

    [Fact]
    public void Constructor_WithNullLoggingService_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalsService(
            _coreServiceMock.Object,
            null!,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            (r, n, c) => null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("loggingService");
    }

    [Fact]
    public void Constructor_WithNullHealthCheckService_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalsService(
            _coreServiceMock.Object,
            _loggingServiceMock.Object,
            null!,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            (r, n, c) => null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("healthCheckService");
    }

    [Fact]
    public void Constructor_WithNullTradingService_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalsService(
            _coreServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            null!,
            _rulesServiceMock.Object,
            (r, n, c) => null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("tradingService");
    }

    [Fact]
    public void Constructor_WithNullRulesService_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalsService(
            _coreServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            null!,
            (r, n, c) => null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("rulesService");
    }

    [Fact]
    public void Constructor_WithNullSignalReceiverFactory_ThrowsArgumentNullException()
    {
        // Arrange & Act
        Action act = () => new SignalsService(
            _coreServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _rulesServiceMock.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("signalReceiverFactory");
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsSignalsServiceConstant()
    {
        // Arrange & Act
        var result = _sut.ServiceName;

        // Assert
        result.Should().Be(Constants.ServiceNames.SignalsService);
    }

    #endregion

    #region Rules and RulesConfig Tests

    [Fact]
    public void Start_SetsRulesFromRulesService()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();

        // Act
        _sut.Start();

        // Assert
        _sut.Rules.Should().Be(_moduleRulesMock.Object);
    }

    [Fact]
    public void Start_SetsRulesConfigFromRules()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();

        // Act
        _sut.Start();

        // Assert
        _sut.RulesConfig.Should().NotBeNull();
        _sut.RulesConfig.CheckInterval.Should().Be(3);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void GetSignalsByPair_WithMultiplePairsPerReceiver_ReturnsFirstMatchOnly()
    {
        // Arrange
        var btcSignal = CreateSignal("Signal1", "BTCUSDT", 0.8);
        var btcSignal2 = CreateSignal("Signal1", "BTCUSDT", 0.9); // Second BTC signal in same receiver
        CreateMockReceiver("Signal1", 60, new[] { btcSignal, btcSignal2 });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetSignalsByPair("BTCUSDT").ToList();

        // Assert - GetSignalsByPair uses FirstOrDefault, so only first match per receiver
        result.Should().HaveCount(1);
        result[0].Should().Be(btcSignal);
    }

    [Fact]
    public void Start_CalledTwice_ClearsAndRestartsReceivers()
    {
        // Arrange
        var receiverMock = CreateMockReceiver("Signal1", 60);
        AddDefinition("Signal1");
        SetupConfig();

        // Act
        _sut.Start();
        _sut.Start();

        // Assert - Receiver should be started twice
        receiverMock.Verify(x => x.Start(), Times.Exactly(2));
    }

    [Fact]
    public void GetRating_WithZeroRatings_ReturnsZero()
    {
        // Arrange
        var signal = CreateSignal("Signal1", "BTCUSDT", 0.0);
        CreateMockReceiver("Signal1", 60, new[] { signal });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", "Signal1");

        // Assert
        result.Should().Be(0.0);
    }

    [Fact]
    public void GetRating_WithNegativeRatings_ReturnsNegative()
    {
        // Arrange
        var signal = CreateSignal("Signal1", "BTCUSDT", -0.5);
        CreateMockReceiver("Signal1", 60, new[] { signal });
        AddDefinition("Signal1");
        SetupConfig();
        _sut.Start();

        // Act
        var result = _sut.GetRating("BTCUSDT", "Signal1");

        // Assert
        result.Should().Be(-0.5);
    }

    [Fact]
    public void GetGlobalRating_WithSingleSignal_ReturnsThatSignalRating()
    {
        // Arrange
        CreateMockReceiver("Signal1", 60, averageRating: 0.75);
        AddDefinition("Signal1");
        SetupConfig(new[] { "Signal1" });
        _sut.Start();

        // Act
        var result = _sut.GetGlobalRating();

        // Assert
        result.Should().Be(0.75);
    }

    #endregion
}
