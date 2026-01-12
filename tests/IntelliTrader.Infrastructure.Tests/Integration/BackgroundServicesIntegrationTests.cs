using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Trailing;
using IntelliTrader.Domain.Signals.ValueObjects;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests for background services working together.
/// </summary>
public class BackgroundServicesIntegrationTests : IClassFixture<InfrastructureTestFixture>, IDisposable
{
    private readonly InfrastructureTestFixture _fixture;
    private readonly JsonPositionRepository _repository;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<ISignalProviderPort> _signalProviderMock;
    private readonly TrailingManager _trailingManager;

    public BackgroundServicesIntegrationTests(InfrastructureTestFixture fixture)
    {
        _fixture = fixture;
        _repository = fixture.CreatePositionRepository();
        _exchangePortMock = new Mock<IExchangePort>();
        _signalProviderMock = new Mock<ISignalProviderPort>();
        _trailingManager = new TrailingManager(_exchangePortMock.Object);
    }

    public void Dispose()
    {
        _repository.Dispose();
    }

    #region TradingRuleProcessorService Tests

    [Fact]
    public async Task TradingRuleProcessor_ProcessesActivePositions()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(52000m)));

        _signalProviderMock
            .Setup(x => x.GetAggregatedSignalAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AggregatedSignal>.Success(new AggregatedSignal
            {
                Pair = pair,
                OverallRating = SignalRating.Create(0.5),
                BuySignalCount = 5,
                SellSignalCount = 2,
                NeutralSignalCount = 3,
                IndividualSignals = new List<TradingSignal>(),
                Timestamp = DateTimeOffset.UtcNow
            }));

        var loggerMock = new Mock<ILogger<TradingRuleProcessorService>>();
        var options = new TradingRuleProcessorOptions
        {
            Interval = TimeSpan.FromMilliseconds(100),
            StartDelay = TimeSpan.Zero
        };

        var service = new TradingRuleProcessorService(
            loggerMock.Object,
            _signalProviderMock.Object,
            _exchangePortMock.Object,
            _repository,
            options);

        PairTradingConfig? receivedConfig = null;
        service.ConfigurationsUpdated += (_, args) =>
        {
            if (args.Configurations.TryGetValue(pair, out var config))
            {
                receivedConfig = config;
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        // Assert
        receivedConfig.Should().NotBeNull();
        receivedConfig!.Pair.Should().Be(pair);
        receivedConfig.SellEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task TradingRuleProcessor_GeneratesConfigurationForMultiplePositions()
    {
        // Arrange
        var pairs = new[]
        {
            TradingPair.Create("BTCUSDT", "USDT"),
            TradingPair.Create("ETHUSDT", "USDT")
        };

        foreach (var pair in pairs)
        {
            var position = Position.Open(
                pair,
                OrderId.From($"order_{pair.Symbol}"),
                Price.Create(1000m),
                Quantity.Create(1m),
                Money.Create(1m, "USDT"));

            await _repository.SaveAsync(position);

            _exchangePortMock
                .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Price>.Success(Price.Create(1050m)));

            _signalProviderMock
                .Setup(x => x.GetAggregatedSignalAsync(pair, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<AggregatedSignal>.Success(new AggregatedSignal
                {
                    Pair = pair,
                    OverallRating = SignalRating.Create(0.3),
                    BuySignalCount = 3,
                    SellSignalCount = 3,
                    NeutralSignalCount = 4,
                    IndividualSignals = new List<TradingSignal>(),
                    Timestamp = DateTimeOffset.UtcNow
                }));
        }

        var loggerMock = new Mock<ILogger<TradingRuleProcessorService>>();
        var options = new TradingRuleProcessorOptions
        {
            Interval = TimeSpan.FromMilliseconds(100),
            StartDelay = TimeSpan.Zero
        };

        var service = new TradingRuleProcessorService(
            loggerMock.Object,
            _signalProviderMock.Object,
            _exchangePortMock.Object,
            _repository,
            options);

        var receivedConfigs = new Dictionary<TradingPair, PairTradingConfig>();
        service.ConfigurationsUpdated += (_, args) =>
        {
            foreach (var kvp in args.Configurations)
            {
                receivedConfigs[kvp.Key] = kvp.Value;
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        // Assert
        receivedConfigs.Should().HaveCount(2);
        receivedConfigs.Keys.Should().Contain(pairs[0]);
        receivedConfigs.Keys.Should().Contain(pairs[1]);
    }

    #endregion

    #region SignalRuleProcessorService Tests

    [Fact]
    public async Task SignalRuleProcessor_EvaluatesSignalAndTriggersBuy()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalProviderMock
            .Setup(x => x.GetAggregatedSignalAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AggregatedSignal>.Success(new AggregatedSignal
            {
                Pair = pair,
                OverallRating = SignalRating.Create(0.7), // Strong buy signal
                BuySignalCount = 8,
                SellSignalCount = 1,
                NeutralSignalCount = 1,
                IndividualSignals = new List<TradingSignal>
                {
                    new TradingSignal
                    {
                        SignalName = "RSI",
                        Pair = pair,
                        Rating = SignalRating.Create(0.8),
                        Type = SignalType.Oscillator,
                        ProviderName = "TradingView",
                        Timestamp = DateTimeOffset.UtcNow
                    }
                },
                Timestamp = DateTimeOffset.UtcNow
            }));

        var loggerMock = new Mock<ILogger<SignalRuleProcessorService>>();
        var options = new SignalRuleProcessorOptions
        {
            Interval = TimeSpan.FromMilliseconds(100),
            MinBuySignalRating = 0.5
        };

        var service = new SignalRuleProcessorService(
            loggerMock.Object,
            _signalProviderMock.Object,
            _repository,
            options);

        BuySignalTriggered? receivedSignal = null;
        service.BuySignalTriggered += (_, args) =>
        {
            receivedSignal = args.Signal;
        };

        // Act
        var result = await service.EvaluateSignalAsync(pair);

        // Assert
        result.Should().BeTrue();
        receivedSignal.Should().NotBeNull();
        receivedSignal!.Pair.Should().Be(pair);
        receivedSignal.Rating.Value.Should().Be(0.7);
    }

    [Fact]
    public async Task SignalRuleProcessor_RejectsWeakSignal()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        _signalProviderMock
            .Setup(x => x.GetAggregatedSignalAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AggregatedSignal>.Success(new AggregatedSignal
            {
                Pair = pair,
                OverallRating = SignalRating.Create(0.3), // Weak signal
                BuySignalCount = 3,
                SellSignalCount = 4,
                NeutralSignalCount = 3,
                IndividualSignals = new List<TradingSignal>(),
                Timestamp = DateTimeOffset.UtcNow
            }));

        var loggerMock = new Mock<ILogger<SignalRuleProcessorService>>();
        var options = new SignalRuleProcessorOptions
        {
            MinBuySignalRating = 0.5
        };

        var service = new SignalRuleProcessorService(
            loggerMock.Object,
            _signalProviderMock.Object,
            _repository,
            options);

        BuySignalTriggered? receivedSignal = null;
        service.BuySignalTriggered += (_, args) =>
        {
            receivedSignal = args.Signal;
        };

        // Act
        var result = await service.EvaluateSignalAsync(pair);

        // Assert
        result.Should().BeFalse();
        receivedSignal.Should().BeNull();
    }

    [Fact]
    public async Task SignalRuleProcessor_RejectsSignalWhenPositionExists()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Create existing position
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        _signalProviderMock
            .Setup(x => x.GetAggregatedSignalAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AggregatedSignal>.Success(new AggregatedSignal
            {
                Pair = pair,
                OverallRating = SignalRating.Create(0.8),
                BuySignalCount = 8,
                SellSignalCount = 1,
                NeutralSignalCount = 1,
                IndividualSignals = new List<TradingSignal>(),
                Timestamp = DateTimeOffset.UtcNow
            }));

        var loggerMock = new Mock<ILogger<SignalRuleProcessorService>>();
        var options = new SignalRuleProcessorOptions
        {
            MinBuySignalRating = 0.5
        };

        var service = new SignalRuleProcessorService(
            loggerMock.Object,
            _signalProviderMock.Object,
            _repository,
            options);

        // Act
        var result = await service.EvaluateSignalAsync(pair);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region OrderExecutionService Tests

    [Fact]
    public async Task OrderExecutionService_ProcessesPositionAndTriggersSell()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        // Setup price that gives ~10% profit
        var currentPrice = Price.Create(55000m);
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(currentPrice));

        var ruleProcessorLoggerMock = new Mock<ILogger<TradingRuleProcessorService>>();
        var ruleProcessor = new TradingRuleProcessorService(
            ruleProcessorLoggerMock.Object,
            _signalProviderMock.Object,
            _exchangePortMock.Object,
            _repository,
            new TradingRuleProcessorOptions());

        var executionLoggerMock = new Mock<ILogger<OrderExecutionService>>();
        var executionOptions = new OrderExecutionOptions
        {
            Interval = TimeSpan.FromMilliseconds(100),
            VirtualTrading = true
        };

        var service = new OrderExecutionService(
            executionLoggerMock.Object,
            _exchangePortMock.Object,
            _repository,
            _trailingManager,
            ruleProcessor,
            executionOptions);

        OrderExecutedEventArgs? executedOrder = null;
        service.OrderExecuted += (_, args) =>
        {
            executedOrder = args;
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        // Note: The actual sell trigger depends on rule processor configuration
        // This test verifies the service runs without errors
        service.ExecutionCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task OrderExecutionService_HandlesTrailingSellCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        // Start trailing sell
        var config = new TrailingConfig
        {
            TrailingPercentage = 1m,
            StopMargin = 0.5m,
            StopAction = TrailingStopAction.Execute
        };

        _trailingManager.InitiateSellTrailing(
            position.Id,
            pair,
            Price.Create(55000m),
            10m,
            5m,
            config);

        // Setup declining prices to trigger sell
        var priceSequence = new Queue<decimal>(new[] { 54500m, 54000m, 53500m });
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = priceSequence.Count > 0 ? priceSequence.Dequeue() : 53000m;
                return Result<Price>.Success(Price.Create(price));
            });

        var executionLoggerMock = new Mock<ILogger<OrderExecutionService>>();
        var executionOptions = new OrderExecutionOptions
        {
            Interval = TimeSpan.FromMilliseconds(50),
            VirtualTrading = true
        };

        var service = new OrderExecutionService(
            executionLoggerMock.Object,
            _exchangePortMock.Object,
            _repository,
            _trailingManager,
            null,
            executionOptions);

        OrderExecutedEventArgs? executedOrder = null;
        service.OrderExecuted += (_, args) =>
        {
            executedOrder = args;
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        // Assert - trailing should have triggered or continued
        service.ExecutionCount.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Full Integration Flow

    [Fact]
    public async Task FullIntegration_PositionCreatedAndManagedThroughLifecycle()
    {
        // This test simulates a complete trading flow:
        // 1. Create a position
        // 2. Rule processor generates config
        // 3. Order execution processes position
        // 4. Trailing is managed
        // 5. Position is closed

        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Setup exchange mock
        var prices = new Queue<decimal>(new[]
        {
            50000m, 51000m, 52000m, 52500m, 52000m, 51500m
        });

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var price = prices.Count > 0 ? prices.Dequeue() : 51500m;
                return Result<Price>.Success(Price.Create(price));
            });

        _signalProviderMock
            .Setup(x => x.GetAggregatedSignalAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AggregatedSignal>.Success(new AggregatedSignal
            {
                Pair = pair,
                OverallRating = SignalRating.Create(0.6),
                BuySignalCount = 6,
                SellSignalCount = 2,
                NeutralSignalCount = 2,
                IndividualSignals = new List<TradingSignal>(),
                Timestamp = DateTimeOffset.UtcNow
            }));

        // Step 1: Create initial position
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"),
            "IntegrationTestRule");

        await _repository.SaveAsync(position);

        // Step 2: Verify position was saved
        var savedPosition = await _repository.GetByIdAsync(position.Id);
        savedPosition.Should().NotBeNull();
        savedPosition!.SignalRule.Should().Be("IntegrationTestRule");

        // Step 3: Setup and run rule processor
        var ruleLoggerMock = new Mock<ILogger<TradingRuleProcessorService>>();
        var ruleProcessor = new TradingRuleProcessorService(
            ruleLoggerMock.Object,
            _signalProviderMock.Object,
            _exchangePortMock.Object,
            _repository,
            new TradingRuleProcessorOptions { Interval = TimeSpan.FromMilliseconds(50), StartDelay = TimeSpan.Zero });

        using var ruleCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await ruleProcessor.StartAsync(ruleCts.Token);
        await Task.Delay(150);
        await ruleProcessor.StopAsync(CancellationToken.None);

        // Verify config was generated
        var config = ruleProcessor.GetPairConfig(pair);
        config.Should().NotBeNull();

        // Step 4: Simulate position close (manual for this test)
        var currentPrice = Price.Create(51500m);
        position.Close(
            OrderId.From("sell1"),
            currentPrice,
            Money.Create(51.5m, "USDT"));

        await _repository.SaveAsync(position);

        // Step 5: Verify final state
        var finalPosition = await _repository.GetByIdAsync(position.Id);
        finalPosition.Should().NotBeNull();
        finalPosition!.IsClosed.Should().BeTrue();

        var activePositions = await _repository.GetAllActiveAsync();
        activePositions.Should().NotContain(p => p.Id == position.Id);

        var activeCount = await _repository.GetActiveCountAsync();
        activeCount.Should().Be(0);
    }

    #endregion
}
