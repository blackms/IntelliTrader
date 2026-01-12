using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Rules;
using IntelliTrader.Domain.Rules.Services;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading.Rules;

public class TradingRuleProcessorTests
{
    private readonly Mock<IPositionRepository> _positionRepositoryMock;
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<ISignalProviderPort> _signalProviderMock;
    private readonly RuleEvaluator _ruleEvaluator;
    private readonly TradingRuleProcessor _processor;

    public TradingRuleProcessorTests()
    {
        _positionRepositoryMock = new Mock<IPositionRepository>();
        _portfolioRepositoryMock = new Mock<IPortfolioRepository>();
        _exchangePortMock = new Mock<IExchangePort>();
        _signalProviderMock = new Mock<ISignalProviderPort>();
        _ruleEvaluator = new RuleEvaluator();

        _processor = new TradingRuleProcessor(
            _positionRepositoryMock.Object,
            _portfolioRepositoryMock.Object,
            _exchangePortMock.Object,
            _ruleEvaluator,
            _signalProviderMock.Object);
    }

    #region Helper Methods

    private static Portfolio CreatePortfolio()
    {
        return Portfolio.Create("Test", "USDT", 10000m, 5, 10m);
    }

    private static Position CreatePosition(
        TradingPair pair,
        decimal entryPrice = 50000m,
        decimal quantity = 0.1m,
        DateTimeOffset? openedAt = null)
    {
        return Position.Open(
            pair,
            OrderId.From(Guid.NewGuid().ToString()),
            Price.Create(entryPrice),
            Quantity.Create(quantity),
            Money.Create(5m, "USDT"),
            "TestRule",
            openedAt);
    }

    private static TradingRuleConfig CreateDefaultConfig()
    {
        return new TradingRuleConfig
        {
            Enabled = true,
            DefaultSellMargin = 2.0m,
            DefaultStopLossMargin = -10m,
            StopLossEnabled = true,
            StopLossMinAge = 60, // 60 seconds
            DCAEnabled = true,
            MaxDCALevels = 3,
            Rules = Array.Empty<TradingRule>()
        };
    }

    #endregion

    #region ProcessAllPositionsAsync Tests

    [Fact]
    public async Task ProcessAllPositionsAsync_WhenDisabled_ReturnsEmptyList()
    {
        // Arrange
        var config = new TradingRuleConfig { Enabled = false };

        // Act
        var result = await _processor.ProcessAllPositionsAsync(config);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAllPositionsAsync_WhenNoPortfolio_ReturnsFailure()
    {
        // Arrange
        var config = CreateDefaultConfig();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        // Act
        var result = await _processor.ProcessAllPositionsAsync(config);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task ProcessAllPositionsAsync_WhenNoActivePositions_ReturnsEmptyList()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var portfolio = CreatePortfolio();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        // Act
        var result = await _processor.ProcessAllPositionsAsync(config);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAllPositionsAsync_WhenPriceFetchFails_ReturnsFailure()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var portfolio = CreatePortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair);

        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPricesAsync(It.IsAny<IEnumerable<TradingPair>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<TradingPair, Price>>.Failure(Error.ExchangeError("Failed")));

        // Act
        var result = await _processor.ProcessAllPositionsAsync(config);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAllPositionsAsync_WithActivePositions_ProcessesEachPosition()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var portfolio = CreatePortfolio();
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var position1 = CreatePosition(pair1, 50000m, 0.1m, DateTimeOffset.UtcNow.AddHours(-1));
        var position2 = CreatePosition(pair2, 3000m, 1m, DateTimeOffset.UtcNow.AddHours(-1));

        portfolio.RecordPositionOpened(position1.Id, pair1, Money.Create(5000m, "USDT"));
        portfolio.RecordPositionOpened(position2.Id, pair2, Money.Create(3000m, "USDT"));

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position1);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position2);

        var prices = new Dictionary<TradingPair, Price>
        {
            { pair1, Price.Create(51000m) }, // +2%
            { pair2, Price.Create(3060m) }   // +2%
        };

        _exchangePortMock
            .Setup(x => x.GetCurrentPricesAsync(It.IsAny<IEnumerable<TradingPair>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<TradingPair, Price>>.Success(prices));

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(It.IsAny<TradingPair>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessAllPositionsAsync(config);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    #endregion

    #region ProcessPositionAsync - Stop Loss Tests

    [Fact]
    public async Task ProcessPositionAsync_WhenStopLossTriggered_ReturnsStopLossAction()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        // Create position that was opened some time ago (to pass min age check)
        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(44000m); // -12% from entry

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().Be(TradingRuleAction.StopLoss);
        result.MatchReason.Should().Contain("Stop-loss");
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenStopLossButTooYoung_DoesNotTriggerStopLoss()
    {
        // Arrange
        var config = CreateDefaultConfig();
        config = config with { StopLossMinAge = 3600 }; // 1 hour minimum age

        var pair = TradingPair.Create("BTCUSDT", "USDT");
        // Create position that was just opened
        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddSeconds(-30));
        var currentPrice = Price.Create(44000m); // -12% from entry

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().BeNull(); // No stop-loss because too young
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenStopLossDisabled_DoesNotTriggerStopLoss()
    {
        // Arrange
        var config = CreateDefaultConfig() with { StopLossEnabled = false };
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(44000m); // -12% from entry

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().BeNull();
    }

    #endregion

    #region ProcessPositionAsync - Take Profit Tests

    [Fact]
    public async Task ProcessPositionAsync_WhenTakeProfitTargetReached_ReturnsTakeProfitAction()
    {
        // Arrange
        var config = CreateDefaultConfig() with { DefaultSellMargin = 2.0m };
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(51500m); // ~3% profit (exceeds 2% target)

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().Be(TradingRuleAction.TakeProfit);
        result.MatchReason.Should().Contain("Take-profit");
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenBelowTakeProfitTarget_DoesNotTriggerTakeProfit()
    {
        // Arrange
        var config = CreateDefaultConfig() with { DefaultSellMargin = 5.0m };
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(51000m); // ~2% profit (below 5% target)

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().BeNull();
    }

    #endregion

    #region ProcessPositionAsync - Custom Rules Tests

    [Fact]
    public async Task ProcessPositionAsync_WhenCustomSellRuleMatches_ReturnsSellAction()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var sellRule = new TradingRule
        {
            Name = "SellAtMinMargin",
            Action = TradingRuleAction.Sell,
            Conditions = new[]
            {
                new RuleCondition { MinMargin = 1.0m }
            }
        };

        var config = CreateDefaultConfig() with
        {
            DefaultSellMargin = 10m, // High target so take-profit doesn't trigger
            Rules = new[] { sellRule }
        };

        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(50750m); // ~1.5% profit

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.MatchedRule.Should().Be(sellRule);
        result.RecommendedAction.Should().Be(TradingRuleAction.Sell);
        result.MatchReason.Should().Contain("SellAtMinMargin");
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenDCARuleMatches_ReturnsDCAAction()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var dcaRule = new TradingRule
        {
            Name = "DCAOnDip",
            Action = TradingRuleAction.DCA,
            Conditions = new[]
            {
                new RuleCondition { MaxMargin = -5m } // DCA when down 5%
            }
        };

        var config = CreateDefaultConfig() with
        {
            StopLossEnabled = false, // Disable stop-loss so DCA can trigger
            Rules = new[] { dcaRule }
        };

        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(47000m); // ~-6% loss

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.MatchedRule.Should().Be(dcaRule);
        result.RecommendedAction.Should().Be(TradingRuleAction.DCA);
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenDCARuleMatchesButMaxLevelReached_DoesNotTriggerDCA()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var dcaRule = new TradingRule
        {
            Name = "DCAOnDip",
            Action = TradingRuleAction.DCA,
            Conditions = new[]
            {
                new RuleCondition { MaxMargin = -2m }
            }
        };

        var config = CreateDefaultConfig() with
        {
            StopLossEnabled = false,
            MaxDCALevels = 2,
            Rules = new[] { dcaRule }
        };

        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        // Add DCA entries to reach max level
        position.AddDCAEntry(OrderId.From("dca1"), Price.Create(48000m), Quantity.Create(0.1m), Money.Create(5m, "USDT"));
        position.AddDCAEntry(OrderId.From("dca2"), Price.Create(46000m), Quantity.Create(0.1m), Money.Create(5m, "USDT"));

        var currentPrice = Price.Create(44000m); // Further dip

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().BeNull();
        result.MatchReason.Should().Contain("DCA not allowed");
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenMultipleRulesMatch_UsesFirstMatchMode()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var rule1 = new TradingRule
        {
            Name = "Rule1",
            Priority = 1,
            Action = TradingRuleAction.Sell,
            Conditions = new[]
            {
                new RuleCondition { MinMargin = 1m }
            }
        };
        var rule2 = new TradingRule
        {
            Name = "Rule2",
            Priority = 2,
            Action = TradingRuleAction.Alert,
            Conditions = new[]
            {
                new RuleCondition { MinMargin = 0.5m }
            }
        };

        var config = CreateDefaultConfig() with
        {
            DefaultSellMargin = 100m,
            ProcessingMode = RuleProcessingMode.FirstMatch,
            Rules = new[] { rule1, rule2 }
        };

        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(51000m); // ~2% profit

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.MatchedRule.Should().Be(rule1);
        result.RecommendedAction.Should().Be(TradingRuleAction.Sell);
    }

    [Fact]
    public async Task ProcessPositionAsync_WhenNoRulesMatch_ReturnsNoAction()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var rule = new TradingRule
        {
            Name = "HighMarginRule",
            Action = TradingRuleAction.Sell,
            Conditions = new[]
            {
                new RuleCondition { MinMargin = 50m } // Very high requirement
            }
        };

        var config = CreateDefaultConfig() with
        {
            DefaultSellMargin = 100m, // High target
            StopLossEnabled = false,
            Rules = new[] { rule }
        };

        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(50500m); // ~1% profit

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.HasMatch.Should().BeFalse();
        result.RecommendedAction.Should().BeNull();
    }

    #endregion

    #region GetPositionsRequiringAction Tests

    [Fact]
    public void GetPositionsRequiringAction_FiltersPositionsWithActions()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");
        var pair3 = TradingPair.Create("ADAUSDT", "USDT");

        var results = new List<TradingRuleProcessingResult>
        {
            new()
            {
                PositionId = PositionId.Create(),
                Pair = pair1,
                RecommendedAction = TradingRuleAction.Sell,
                CurrentMargin = Margin.FromPercentage(5m),
                CurrentPrice = Price.Create(50000m)
            },
            new()
            {
                PositionId = PositionId.Create(),
                Pair = pair2,
                RecommendedAction = null, // No action
                CurrentMargin = Margin.FromPercentage(1m),
                CurrentPrice = Price.Create(3000m)
            },
            new()
            {
                PositionId = PositionId.Create(),
                Pair = pair3,
                RecommendedAction = TradingRuleAction.StopLoss,
                CurrentMargin = Margin.FromPercentage(-15m),
                CurrentPrice = Price.Create(0.5m)
            }
        };

        // Act
        var positionsRequiringAction = _processor.GetPositionsRequiringAction(results);

        // Assert
        positionsRequiringAction.Should().HaveCount(2);
        positionsRequiringAction.Should().Contain(r => r.Pair == pair1);
        positionsRequiringAction.Should().Contain(r => r.Pair == pair3);
        positionsRequiringAction.Should().NotContain(r => r.Pair == pair2);
    }

    #endregion

    #region CreateCloseCommand Tests

    [Fact]
    public void CreateCloseCommand_ForSellAction_ReturnsCommandWithSignalRuleReason()
    {
        // Arrange
        var positionId = PositionId.Create();
        var result = new TradingRuleProcessingResult
        {
            PositionId = positionId,
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.Sell,
            CurrentMargin = Margin.FromPercentage(5m),
            CurrentPrice = Price.Create(50000m)
        };

        // Act
        var command = _processor.CreateCloseCommand(result);

        // Assert
        command.Should().NotBeNull();
        command!.PositionId.Should().Be(positionId);
        command.Reason.Should().Be(CloseReason.SignalRule);
    }

    [Fact]
    public void CreateCloseCommand_ForStopLossAction_ReturnsCommandWithStopLossReason()
    {
        // Arrange
        var positionId = PositionId.Create();
        var result = new TradingRuleProcessingResult
        {
            PositionId = positionId,
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.StopLoss,
            CurrentMargin = Margin.FromPercentage(-15m),
            CurrentPrice = Price.Create(50000m)
        };

        // Act
        var command = _processor.CreateCloseCommand(result);

        // Assert
        command.Should().NotBeNull();
        command!.Reason.Should().Be(CloseReason.StopLoss);
    }

    [Fact]
    public void CreateCloseCommand_ForTakeProfitAction_ReturnsCommandWithTakeProfitReason()
    {
        // Arrange
        var positionId = PositionId.Create();
        var result = new TradingRuleProcessingResult
        {
            PositionId = positionId,
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.TakeProfit,
            CurrentMargin = Margin.FromPercentage(10m),
            CurrentPrice = Price.Create(50000m)
        };

        // Act
        var command = _processor.CreateCloseCommand(result);

        // Assert
        command.Should().NotBeNull();
        command!.Reason.Should().Be(CloseReason.TakeProfit);
    }

    [Fact]
    public void CreateCloseCommand_ForDCAAction_ReturnsNull()
    {
        // Arrange
        var result = new TradingRuleProcessingResult
        {
            PositionId = PositionId.Create(),
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.DCA,
            CurrentMargin = Margin.FromPercentage(-5m),
            CurrentPrice = Price.Create(50000m)
        };

        // Act
        var command = _processor.CreateCloseCommand(result);

        // Assert
        command.Should().BeNull();
    }

    #endregion

    #region CreateDCACommand Tests

    [Fact]
    public void CreateDCACommand_ForDCAAction_ReturnsCommand()
    {
        // Arrange
        var positionId = PositionId.Create();
        var result = new TradingRuleProcessingResult
        {
            PositionId = positionId,
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.DCA,
            CurrentMargin = Margin.FromPercentage(-5m),
            CurrentPrice = Price.Create(50000m)
        };
        var defaultDCACost = Money.Create(100m, "USDT");

        // Act
        var command = _processor.CreateDCACommand(result, defaultDCACost);

        // Assert
        command.Should().NotBeNull();
        command!.PositionId.Should().Be(positionId);
        command.Cost.Should().Be(defaultDCACost);
    }

    [Fact]
    public void CreateDCACommand_WithRuleDCACost_UsesRuleCost()
    {
        // Arrange
        var ruleCost = Money.Create(500m, "USDT");
        var positionId = PositionId.Create();
        var result = new TradingRuleProcessingResult
        {
            PositionId = positionId,
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.DCA,
            MatchedRule = new TradingRule
            {
                Name = "DCARule",
                Action = TradingRuleAction.DCA,
                Conditions = Array.Empty<RuleCondition>(),
                DCACost = ruleCost
            },
            CurrentMargin = Margin.FromPercentage(-5m),
            CurrentPrice = Price.Create(50000m)
        };
        var defaultDCACost = Money.Create(100m, "USDT");

        // Act
        var command = _processor.CreateDCACommand(result, defaultDCACost);

        // Assert
        command.Should().NotBeNull();
        command!.Cost.Should().Be(ruleCost);
    }

    [Fact]
    public void CreateDCACommand_ForNonDCAAction_ReturnsNull()
    {
        // Arrange
        var result = new TradingRuleProcessingResult
        {
            PositionId = PositionId.Create(),
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            RecommendedAction = TradingRuleAction.Sell,
            CurrentMargin = Margin.FromPercentage(5m),
            CurrentPrice = Price.Create(50000m)
        };
        var defaultDCACost = Money.Create(100m, "USDT");

        // Act
        var command = _processor.CreateDCACommand(result, defaultDCACost);

        // Assert
        command.Should().BeNull();
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public async Task ProcessPositionAsync_StopLossTakesPriorityOverCustomRules()
    {
        // Arrange - stop-loss and sell rule both match, stop-loss should win
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var sellRule = new TradingRule
        {
            Name = "SellOnLoss",
            Action = TradingRuleAction.Sell,
            Conditions = new[]
            {
                new RuleCondition { MaxMargin = 0m } // Any loss
            }
        };

        var config = CreateDefaultConfig() with
        {
            DefaultStopLossMargin = -10m,
            StopLossEnabled = true,
            StopLossMinAge = 0, // No age requirement
            Rules = new[] { sellRule }
        };

        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(44000m); // -12% loss

        _signalProviderMock
            .Setup(x => x.GetAllSignalsAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradingSignal>>.Success(Array.Empty<TradingSignal>()));

        // Act
        var result = await _processor.ProcessPositionAsync(position, currentPrice, config);

        // Assert - Stop-loss should take priority
        result.RecommendedAction.Should().Be(TradingRuleAction.StopLoss);
    }

    [Fact]
    public async Task ProcessPositionAsync_WithoutSignalProvider_StillWorks()
    {
        // Arrange
        var processorWithoutSignals = new TradingRuleProcessor(
            _positionRepositoryMock.Object,
            _portfolioRepositoryMock.Object,
            _exchangePortMock.Object,
            _ruleEvaluator);

        var config = CreateDefaultConfig() with { DefaultSellMargin = 2.0m };
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, 50000m, 0.1m, DateTimeOffset.UtcNow.AddMinutes(-10));
        var currentPrice = Price.Create(52000m); // +4% profit

        // Act
        var result = await processorWithoutSignals.ProcessPositionAsync(position, currentPrice, config);

        // Assert
        result.RecommendedAction.Should().Be(TradingRuleAction.TakeProfit);
    }

    #endregion
}
