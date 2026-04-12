using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Signals.Base;

namespace IntelliTrader.Performance.Tests;

/// <summary>
/// Performance tests for signal processing operations.
/// Thresholds are generous (10x expected) for CI stability.
/// </summary>
public class SignalPerformanceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<IRulesService> _rulesServiceMock;
    private readonly Mock<IModuleRules> _moduleRulesMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
    private readonly Dictionary<string, Mock<ISignalReceiver>> _receiverMocks;
    private readonly SignalsService _sut;

    public SignalPerformanceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _rulesServiceMock = new Mock<IRulesService>();
        _moduleRulesMock = new Mock<IModuleRules>();
        _configProviderMock = new Mock<IConfigProvider>();
        _receiverMocks = new Dictionary<string, Mock<ISignalReceiver>>();

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
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _rulesServiceMock.Object,
            signalReceiverFactory,
            _configProviderMock.Object);
    }

    private Mock<ISignalReceiver> CreateMockReceiver(string name, IEnumerable<ISignal>? signals = null, double? averageRating = null)
    {
        var mock = new Mock<ISignalReceiver>();
        mock.Setup(x => x.SignalName).Returns(name);
        mock.Setup(x => x.GetPeriod()).Returns(60);
        mock.Setup(x => x.GetSignals()).Returns(signals ?? new List<ISignal>());
        mock.Setup(x => x.GetAverageRating()).Returns(averageRating);
        _receiverMocks[name] = mock;
        return mock;
    }

    private void SetupConfig(IEnumerable<string>? globalRatingSignals = null, List<SignalDefinition>? definitions = null)
    {
        var config = new SignalsConfig
        {
            Enabled = true,
            Definitions = definitions ?? new List<SignalDefinition>(),
            GlobalRatingSignals = globalRatingSignals ?? new List<string>()
        };

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
            VolumeChange = 5.0,
            PriceChange = 1.5m,
            RatingChange = 0.1,
            Volatility = 0.5
        };
    }

    [Fact]
    public void GetGlobalRating_With50Signals_CompletesWithin50ms()
    {
        // Arrange - create 50 signal receivers with ratings
        var signalNames = Enumerable.Range(0, 50)
            .Select(i => $"Signal_{i}")
            .ToList();

        foreach (var name in signalNames)
        {
            CreateMockReceiver(name, averageRating: 0.75);
        }

        // Initialize receivers via reflection
        var receiversField = typeof(SignalsService)
            .GetField("signalReceivers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var receivers = new ConcurrentDictionary<string, ISignalReceiver>();
        foreach (var kvp in _receiverMocks)
        {
            receivers[kvp.Key] = kvp.Value.Object;
        }
        receiversField?.SetValue(_sut, receivers);

        SetupConfig(globalRatingSignals: signalNames);

        // Warmup
        _sut.GetGlobalRating();

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _sut.GetGlobalRating();
        }
        sw.Stop();

        // Assert - 50ms for 1000 iterations with 50 signals each (500ms total budget)
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "1000 global rating calculations across 50 signals should complete within 500ms");
    }

    [Fact]
    public void GetGlobalRating_With100Signals_CompletesWithin100ms()
    {
        // Arrange - create 100 signal receivers
        var signalNames = Enumerable.Range(0, 100)
            .Select(i => $"Signal_{i}")
            .ToList();

        foreach (var name in signalNames)
        {
            CreateMockReceiver(name, averageRating: 0.5 + (0.01 * signalNames.IndexOf(name)));
        }

        var receiversField = typeof(SignalsService)
            .GetField("signalReceivers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var receivers = new ConcurrentDictionary<string, ISignalReceiver>();
        foreach (var kvp in _receiverMocks)
        {
            receivers[kvp.Key] = kvp.Value.Object;
        }
        receiversField?.SetValue(_sut, receivers);

        SetupConfig(globalRatingSignals: signalNames);

        // Warmup
        _sut.GetGlobalRating();

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
        {
            _sut.GetGlobalRating();
        }
        sw.Stop();

        // Assert - 500ms for 500 iterations with 100 signals
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "500 global rating calculations across 100 signals should complete within 500ms");
    }

    [Fact]
    public void GetSignalsByPair_1000Signals_CompletesWithin500ms()
    {
        // Arrange - create signals for many pairs
        var signals = Enumerable.Range(0, 1000)
            .Select(i => CreateSignal("TestSignal", $"PAIR{i}USDT", rating: 0.5 + (i * 0.0001)))
            .ToList();

        var receiverMock = CreateMockReceiver("TestSignal", signals: signals, averageRating: 0.75);

        var receiversField = typeof(SignalsService)
            .GetField("signalReceivers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var receivers = new ConcurrentDictionary<string, ISignalReceiver>();
        receivers["TestSignal"] = receiverMock.Object;
        receiversField?.SetValue(_sut, receivers);

        SetupConfig(globalRatingSignals: new List<string> { "TestSignal" });

        // Warmup
        _sut.GetSignal("PAIR0USDT", "TestSignal");

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _sut.GetSignal($"PAIR{i % 1000}USDT", "TestSignal");
        }
        sw.Stop();

        // Assert - 500ms for 1000 lookups across 1000 signals
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "1000 signal lookups should complete within 500ms");
    }

    [Fact]
    public void GetGlobalRating_ConcurrentAccess_CompletesWithin500ms()
    {
        // Arrange
        var signalNames = Enumerable.Range(0, 20)
            .Select(i => $"Signal_{i}")
            .ToList();

        foreach (var name in signalNames)
        {
            CreateMockReceiver(name, averageRating: 0.8);
        }

        var receiversField = typeof(SignalsService)
            .GetField("signalReceivers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var receivers = new ConcurrentDictionary<string, ISignalReceiver>();
        foreach (var kvp in _receiverMocks)
        {
            receivers[kvp.Key] = kvp.Value.Object;
        }
        receiversField?.SetValue(_sut, receivers);

        SetupConfig(globalRatingSignals: signalNames);

        // Warmup
        _sut.GetGlobalRating();

        // Act - concurrent reads
        var sw = Stopwatch.StartNew();
        Parallel.For(0, 500, _ =>
        {
            _sut.GetGlobalRating();
        });
        sw.Stop();

        // Assert - 500ms for 500 concurrent calls
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "500 concurrent GetGlobalRating calls should complete within 500ms");
    }
}
