using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Moq;
using IntelliTrader.Core;
using IntelliTrader.Trading;

namespace IntelliTrader.Performance.Tests.Benchmarks;

/// <summary>
/// BenchmarkDotNet micro-benchmarks for trading operations.
/// Run manually via: dotnet run -c Release -- --filter "*TradingBenchmarks*"
/// These are NOT executed as part of CI; use the xUnit tests for CI assertions.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class TradingBenchmarks
{
    private TradingService _sut = null!;
    private List<string> _pairs = null!;

    [Params(10, 50, 100, 500)]
    public int PairCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var healthCheckServiceMock = new Mock<IHealthCheckService>();
        var rulesServiceMock = new Mock<IRulesService>();
        var backtestingServiceMock = new Mock<IBacktestingService>();
        var signalsServiceMock = new Mock<ISignalsService>();
        var exchangeServiceMock = new Mock<IExchangeService>();
        var accountMock = new Mock<ITradingAccount>();
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

        accountMock.Setup(x => x.GetBalance()).Returns(100000m);
        accountMock.Setup(x => x.GetTradingPairs()).Returns(new List<ITradingPair>());
        accountMock.Setup(x => x.HasTradingPair(It.IsAny<string>())).Returns(false);
        exchangeServiceMock.Setup(x => x.GetLastPrice(It.IsAny<string>())).ReturnsAsync(100m);

        Func<string, IExchangeService> exchangeServiceFactory = (_) => exchangeServiceMock.Object;

        _sut = new TradingService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            healthCheckServiceMock.Object,
            rulesServiceMock.Object,
            new Lazy<IBacktestingService>(() => backtestingServiceMock.Object),
            new Lazy<ISignalsService>(() => signalsServiceMock.Object),
            exchangeServiceFactory,
            applicationContextMock.Object,
            configProviderMock.Object);

        // Inject default trading config via reflection
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

        var field = typeof(TradingService).GetField("<Account>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(_sut, accountMock.Object);

        _sut.ResumeTrading(true);

        _pairs = Enumerable.Range(0, PairCount)
            .Select(i => $"PAIR{i}USDT")
            .ToList();
    }

    [Benchmark]
    public int CanBuy_AllPairs()
    {
        int count = 0;
        foreach (var pair in _pairs)
        {
            if (_sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m }, out _))
                count++;
        }
        return count;
    }

    [Benchmark]
    public int CanBuy_ManualOrders()
    {
        int count = 0;
        foreach (var pair in _pairs)
        {
            if (_sut.CanBuy(new BuyOptions(pair) { MaxCost = 100m, ManualOrder = true }, out _))
                count++;
        }
        return count;
    }
}

/// <summary>
/// Entry point for running benchmarks manually.
/// Usage: dotnet run -c Release
/// </summary>
public static class BenchmarkProgram
{
    public static void RunBenchmarks(string[] args)
    {
        BenchmarkRunner.Run<TradingBenchmarks>();
    }
}
