using System.Diagnostics;
using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Trading;

namespace IntelliTrader.Performance.Tests;

/// <summary>
/// Performance tests for critical trading operations.
/// Thresholds are set generously (10x expected) to avoid flaky failures on CI runners.
/// </summary>
public class TradingPerformanceTests
{
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

    public TradingPerformanceTests()
    {
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

        // Setup backtesting config
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
        Func<string, IExchangeService> exchangeServiceFactory = (_) => _exchangeServiceMock.Object;

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

        // Inject default trading config via reflection to avoid
        // "Value cannot be null (Parameter 'configuration')" errors
        var defaultConfig = new TradingConfig
        {
            Market = "USDT",
            Exchange = "Binance",
            ExcludedPairs = new List<string>(),
            DCALevels = new List<DCALevel>(),
            BuyType = OrderType.Market,
            SellType = OrderType.Market,
            BuyMaxCost = 100m,
            MaxPairs = 0,
            VirtualTrading = true,
            VirtualAccountFilePath = "test-virtual-account.json",
            VirtualAccountInitialBalance = 10000m
        };
        var configField = typeof(ConfigurableServiceBase<TradingConfig>)
            .GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        configField?.SetValue(_sut, defaultConfig);

        // Inject account via reflection
        var field = typeof(TradingService).GetField("<Account>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(_sut, _accountMock.Object);
    }

    private void SetupDefaultMocks(decimal balance = 10000m)
    {
        _accountMock.Setup(x => x.GetBalance()).Returns(balance);
        _accountMock.Setup(x => x.GetTradingPairs()).Returns(new List<ITradingPair>());
        _accountMock.Setup(x => x.HasTradingPair(It.IsAny<string>())).Returns(false);
        _exchangeServiceMock.Setup(x => x.GetLastPrice(It.IsAny<string>())).ReturnsAsync(100m);
        _sut.ResumeTrading(true);
    }

    [Fact]
    public void CanBuy_100Pairs_CompletesWithin100ms()
    {
        // Arrange
        SetupDefaultMocks();
        var pairs = Enumerable.Range(0, 100)
            .Select(i => $"PAIR{i}USDT")
            .ToList();

        // Warmup
        foreach (var pair in pairs.Take(5))
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m }, out _);
        }

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var pair in pairs)
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m }, out _);
        }
        sw.Stop();

        // Assert - generous threshold: 100ms for 100 evaluations (10x margin)
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "CanBuy for 100 pairs should complete well within 100ms");
    }

    [Fact]
    public void CanBuy_WithManualOrder_500Pairs_CompletesWithin200ms()
    {
        // Arrange
        SetupDefaultMocks();
        var pairs = Enumerable.Range(0, 500)
            .Select(i => $"PAIR{i}USDT")
            .ToList();

        // Warmup
        foreach (var pair in pairs.Take(5))
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m, ManualOrder = true }, out _);
        }

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var pair in pairs)
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m, ManualOrder = true }, out _);
        }
        sw.Stop();

        // Assert - 200ms for 500 pairs
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "CanBuy (manual) for 500 pairs should complete well within 200ms");
    }

    [Fact]
    public void CanBuy_WithExistingPairs_CompletesWithin100ms()
    {
        // Arrange
        SetupDefaultMocks();
        _accountMock.Setup(x => x.HasTradingPair(It.IsAny<string>())).Returns(true);

        var pairs = Enumerable.Range(0, 100)
            .Select(i => $"PAIR{i}USDT")
            .ToList();

        // Warmup
        foreach (var pair in pairs.Take(5))
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m }, out _);
        }

        // Act
        var sw = Stopwatch.StartNew();
        foreach (var pair in pairs)
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m }, out _);
        }
        sw.Stop();

        // Assert
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "CanBuy rejection (pair exists) for 100 pairs should be fast");
    }

    [Fact]
    public void CanBuy_ConcurrentEvaluations_CompletesWithin500ms()
    {
        // Arrange
        SetupDefaultMocks();
        var pairs = Enumerable.Range(0, 200)
            .Select(i => $"PAIR{i}USDT")
            .ToList();

        // Warmup
        _sut.CanBuy(new BuyOptions("WARMUPUSDT") { MaxCost = 100m }, out _);

        // Act - simulate concurrent evaluation
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(pairs, pair =>
        {
            _sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m }, out _);
        });
        sw.Stop();

        // Assert - 500ms for 200 concurrent evaluations
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "concurrent CanBuy for 200 pairs should complete within 500ms");
    }

    [Fact]
    public void SuspendResumeTrading_HighFrequency_CompletesWithin50ms()
    {
        // Arrange & Warmup
        _sut.SuspendTrading(false);
        _sut.ResumeTrading(true);

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            if (i % 2 == 0)
                _sut.SuspendTrading(false);
            else
                _sut.ResumeTrading(true);
        }
        sw.Stop();

        // Assert - 50ms for 1000 suspend/resume toggles
        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            "1000 suspend/resume toggles should complete within 50ms");
    }
}
