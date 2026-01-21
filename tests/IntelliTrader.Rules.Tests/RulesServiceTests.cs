using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using IntelliTrader.Core;
using System.Reflection;
using Xunit;

namespace IntelliTrader.Rules.Tests;

public class RulesServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly TestableRulesService _sut;

    public RulesServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _sut = CreateRulesServiceWithConfig(CreateDefaultConfig());
    }

    #region Helper Methods

    private TestableRulesService CreateRulesServiceWithConfig(IRulesConfig config)
    {
        var service = new TestableRulesService(_loggingServiceMock.Object);
        service.SetConfig(config);
        return service;
    }

    private static IRulesConfig CreateDefaultConfig(params IModuleRules[] modules)
    {
        var configMock = new Mock<IRulesConfig>();
        configMock.Setup(x => x.Modules).Returns(modules);
        return configMock.Object;
    }

    private static IModuleRules CreateModuleRules(string moduleName, IEnumerable<IRule>? entries = null)
    {
        var moduleRulesMock = new Mock<IModuleRules>();
        moduleRulesMock.Setup(x => x.Module).Returns(moduleName);
        moduleRulesMock.Setup(x => x.Entries).Returns(entries ?? new List<IRule>());
        return moduleRulesMock.Object;
    }

    private static IRuleCondition CreateCondition(
        string? signal = null,
        long? minVolume = null,
        long? maxVolume = null,
        double? minVolumeChange = null,
        double? maxVolumeChange = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        decimal? minPriceChange = null,
        decimal? maxPriceChange = null,
        double? minRating = null,
        double? maxRating = null,
        double? minRatingChange = null,
        double? maxRatingChange = null,
        double? minVolatility = null,
        double? maxVolatility = null,
        double? minGlobalRating = null,
        double? maxGlobalRating = null,
        List<string>? pairs = null,
        double? minAge = null,
        double? maxAge = null,
        double? minLastBuyAge = null,
        double? maxLastBuyAge = null,
        decimal? minMargin = null,
        decimal? maxMargin = null,
        decimal? minMarginChange = null,
        decimal? maxMarginChange = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        decimal? minCost = null,
        decimal? maxCost = null,
        int? minDCALevel = null,
        int? maxDCALevel = null,
        List<string>? signalRules = null)
    {
        var conditionMock = new Mock<IRuleCondition>();
        conditionMock.Setup(x => x.Signal).Returns(signal!);
        conditionMock.Setup(x => x.MinVolume).Returns(minVolume);
        conditionMock.Setup(x => x.MaxVolume).Returns(maxVolume);
        conditionMock.Setup(x => x.MinVolumeChange).Returns(minVolumeChange);
        conditionMock.Setup(x => x.MaxVolumeChange).Returns(maxVolumeChange);
        conditionMock.Setup(x => x.MinPrice).Returns(minPrice);
        conditionMock.Setup(x => x.MaxPrice).Returns(maxPrice);
        conditionMock.Setup(x => x.MinPriceChange).Returns(minPriceChange);
        conditionMock.Setup(x => x.MaxPriceChange).Returns(maxPriceChange);
        conditionMock.Setup(x => x.MinRating).Returns(minRating);
        conditionMock.Setup(x => x.MaxRating).Returns(maxRating);
        conditionMock.Setup(x => x.MinRatingChange).Returns(minRatingChange);
        conditionMock.Setup(x => x.MaxRatingChange).Returns(maxRatingChange);
        conditionMock.Setup(x => x.MinVolatility).Returns(minVolatility);
        conditionMock.Setup(x => x.MaxVolatility).Returns(maxVolatility);
        conditionMock.Setup(x => x.MinGlobalRating).Returns(minGlobalRating);
        conditionMock.Setup(x => x.MaxGlobalRating).Returns(maxGlobalRating);
        conditionMock.Setup(x => x.Pairs).Returns(pairs!);
        conditionMock.Setup(x => x.MinAge).Returns(minAge);
        conditionMock.Setup(x => x.MaxAge).Returns(maxAge);
        conditionMock.Setup(x => x.MinLastBuyAge).Returns(minLastBuyAge);
        conditionMock.Setup(x => x.MaxLastBuyAge).Returns(maxLastBuyAge);
        conditionMock.Setup(x => x.MinMargin).Returns(minMargin);
        conditionMock.Setup(x => x.MaxMargin).Returns(maxMargin);
        conditionMock.Setup(x => x.MinMarginChange).Returns(minMarginChange);
        conditionMock.Setup(x => x.MaxMarginChange).Returns(maxMarginChange);
        conditionMock.Setup(x => x.MinAmount).Returns(minAmount);
        conditionMock.Setup(x => x.MaxAmount).Returns(maxAmount);
        conditionMock.Setup(x => x.MinCost).Returns(minCost);
        conditionMock.Setup(x => x.MaxCost).Returns(maxCost);
        conditionMock.Setup(x => x.MinDCALevel).Returns(minDCALevel);
        conditionMock.Setup(x => x.MaxDCALevel).Returns(maxDCALevel);
        conditionMock.Setup(x => x.SignalRules).Returns(signalRules!);
        return conditionMock.Object;
    }

    private static ISignal CreateSignal(
        string name,
        string pair,
        long? volume = null,
        double? volumeChange = null,
        decimal? price = null,
        decimal? priceChange = null,
        double? rating = null,
        double? ratingChange = null,
        double? volatility = null)
    {
        var signalMock = new Mock<ISignal>();
        signalMock.Setup(x => x.Name).Returns(name);
        signalMock.Setup(x => x.Pair).Returns(pair);
        signalMock.Setup(x => x.Volume).Returns(volume);
        signalMock.Setup(x => x.VolumeChange).Returns(volumeChange);
        signalMock.Setup(x => x.Price).Returns(price);
        signalMock.Setup(x => x.PriceChange).Returns(priceChange);
        signalMock.Setup(x => x.Rating).Returns(rating);
        signalMock.Setup(x => x.RatingChange).Returns(ratingChange);
        signalMock.Setup(x => x.Volatility).Returns(volatility);
        return signalMock.Object;
    }

    private static ITradingPair CreateTradingPair(
        string pair = "BTCUSDT",
        decimal totalAmount = 1.0m,
        decimal averagePricePaid = 100m,
        decimal currentPrice = 100m,
        decimal currentMargin = 0m,
        double currentAge = 100,
        double lastBuyAge = 50,
        int dcaLevel = 0,
        decimal currentCost = 100m,
        OrderMetadata? metadata = null)
    {
        var mockPair = new Mock<ITradingPair>();
        mockPair.Setup(x => x.Pair).Returns(pair);
        mockPair.Setup(x => x.TotalAmount).Returns(totalAmount);
        mockPair.Setup(x => x.AveragePricePaid).Returns(averagePricePaid);
        mockPair.Setup(x => x.CurrentPrice).Returns(currentPrice);
        mockPair.Setup(x => x.CurrentMargin).Returns(currentMargin);
        mockPair.Setup(x => x.CurrentAge).Returns(currentAge);
        mockPair.Setup(x => x.LastBuyAge).Returns(lastBuyAge);
        mockPair.Setup(x => x.DCALevel).Returns(dcaLevel);
        mockPair.Setup(x => x.CurrentCost).Returns(currentCost);
        mockPair.Setup(x => x.Metadata).Returns(metadata ?? new OrderMetadata());
        return mockPair.Object;
    }

    #endregion

    #region GetRules Tests

    [Fact]
    public void GetRules_WithValidModule_ReturnsModuleRules()
    {
        // Arrange
        var moduleRules = CreateModuleRules("TestModule");
        var config = CreateDefaultConfig(moduleRules);
        var sut = CreateRulesServiceWithConfig(config) as IRulesService;

        // Act
        var result = sut.GetRules("TestModule");

        // Assert
        result.Should().NotBeNull();
        result.Module.Should().Be("TestModule");
    }

    [Fact]
    public void GetRules_WithInvalidModule_ThrowsException()
    {
        // Arrange
        var moduleRules = CreateModuleRules("ExistingModule");
        var config = CreateDefaultConfig(moduleRules);
        var sut = CreateRulesServiceWithConfig(config) as IRulesService;

        // Act
        Action act = () => sut.GetRules("NonExistentModule");

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("Unable to find rules for NonExistentModule");
    }

    [Fact]
    public void GetRules_WithMultipleModules_ReturnsCorrectModule()
    {
        // Arrange
        var moduleRules1 = CreateModuleRules("Module1");
        var moduleRules2 = CreateModuleRules("Module2");
        var moduleRules3 = CreateModuleRules("Module3");
        var config = CreateDefaultConfig(moduleRules1, moduleRules2, moduleRules3);
        var sut = CreateRulesServiceWithConfig(config) as IRulesService;

        // Act
        var result = sut.GetRules("Module2");

        // Assert
        result.Should().NotBeNull();
        result.Module.Should().Be("Module2");
    }

    [Fact]
    public void GetRules_WithEmptyModulesList_ThrowsException()
    {
        // Arrange
        var config = CreateDefaultConfig();
        var sut = CreateRulesServiceWithConfig(config) as IRulesService;

        // Act
        Action act = () => sut.GetRules("AnyModule");

        // Assert
        act.Should().Throw<Exception>()
            .WithMessage("Unable to find rules for AnyModule");
    }

    #endregion

    #region CheckConditions - Volume Tests

    [Fact]
    public void CheckConditions_WithMinVolumeCondition_ReturnsTrueWhenMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 1000000);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minVolume: 500000);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMinVolumeCondition_ReturnsFalseWhenNotMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 100000);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minVolume: 500000);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMaxVolumeCondition_ReturnsTrueWhenMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 100000);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", maxVolume: 500000);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxVolumeCondition_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 1000000);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", maxVolume: 500000);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithVolumeCondition_ReturnsFalseWhenVolumeIsNull()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: null);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minVolume: 500000);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Rating Tests

    [Fact]
    public void CheckConditions_WithMinRating_ReturnsTrueWhenMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", rating: 0.8);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMinRating_ReturnsFalseWhenNotMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", rating: 0.3);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMaxRating_ReturnsTrueWhenMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", rating: 0.3);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", maxRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxRating_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", rating: 0.8);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", maxRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithRatingCondition_ReturnsFalseWhenRatingIsNull()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", rating: null);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Signal Tests

    [Fact]
    public void CheckConditions_WithNullSignal_ReturnsFalse()
    {
        // Arrange
        var signals = new Dictionary<string, ISignal>(); // No signals
        var condition = CreateCondition(signal: "NonExistentSignal", minVolume: 100);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMismatchedSignalName_ReturnsFalse()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 1000000);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "DifferentSignal", minVolume: 100);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - TradingPair Tests

    [Fact]
    public void CheckConditions_WithTradingPairConditions_EvaluatesCorrectly()
    {
        // Arrange
        var tradingPair = CreateTradingPair(
            pair: "BTCUSDT",
            totalAmount: 1.0m,
            currentMargin: 5.0m,
            dcaLevel: 2,
            currentAge: 200,
            lastBuyAge: 100);
        var condition = CreateCondition(
            minMargin: 3.0m,
            maxMargin: 10.0m,
            minDCALevel: 1,
            maxDCALevel: 5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMinMargin_ReturnsFalseWhenNotMet()
    {
        // Arrange
        var tradingPair = CreateTradingPair(currentMargin: 2.0m);
        var condition = CreateCondition(minMargin: 5.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMaxMargin_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var tradingPair = CreateTradingPair(currentMargin: 10.0m);
        var condition = CreateCondition(maxMargin: 5.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMinDCALevel_ReturnsTrueWhenMet()
    {
        // Arrange
        var tradingPair = CreateTradingPair(dcaLevel: 3);
        var condition = CreateCondition(minDCALevel: 2);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxDCALevel_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var tradingPair = CreateTradingPair(dcaLevel: 5);
        var condition = CreateCondition(maxDCALevel: 3);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithTradingPairCondition_ReturnsFalseWhenTradingPairIsNull()
    {
        // Arrange
        var condition = CreateCondition(minMargin: 5.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMinAmount_ReturnsTrueWhenMet()
    {
        // Arrange
        var tradingPair = CreateTradingPair(totalAmount: 10.0m);
        var condition = CreateCondition(minAmount: 5.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxAmount_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var tradingPair = CreateTradingPair(totalAmount: 10.0m);
        var condition = CreateCondition(maxAmount: 5.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMinCost_ReturnsTrueWhenMet()
    {
        // Arrange
        var tradingPair = CreateTradingPair(currentCost: 500.0m);
        var condition = CreateCondition(minCost: 100.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxCost_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var tradingPair = CreateTradingPair(currentCost: 500.0m);
        var condition = CreateCondition(maxCost: 100.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Global Rating Tests

    [Fact]
    public void CheckConditions_WithMinGlobalRating_ReturnsTrueWhenMet()
    {
        // Arrange
        var condition = CreateCondition(minGlobalRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), 0.8, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMinGlobalRating_ReturnsFalseWhenNotMet()
    {
        // Arrange
        var condition = CreateCondition(minGlobalRating: 0.8);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), 0.5, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMaxGlobalRating_ReturnsTrueWhenMet()
    {
        // Arrange
        var condition = CreateCondition(maxGlobalRating: 0.8);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), 0.5, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxGlobalRating_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var condition = CreateCondition(maxGlobalRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), 0.8, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithGlobalRatingCondition_ReturnsFalseWhenGlobalRatingIsNull()
    {
        // Arrange
        var condition = CreateCondition(minGlobalRating: 0.5);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Pairs Filter Tests

    [Fact]
    public void CheckConditions_WithPairsFilter_ReturnsTrueWhenPairMatches()
    {
        // Arrange
        var condition = CreateCondition(pairs: new List<string> { "BTCUSDT", "ETHUSDT" });
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithPairsFilter_ReturnsFalseWhenPairNotInList()
    {
        // Arrange
        var condition = CreateCondition(pairs: new List<string> { "BTCUSDT", "ETHUSDT" });
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "XRPUSDT", null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithPairsFilter_ReturnsFalseWhenPairIsNull()
    {
        // Arrange
        var condition = CreateCondition(pairs: new List<string> { "BTCUSDT", "ETHUSDT" });
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Price Tests

    [Fact]
    public void CheckConditions_WithMinPrice_ReturnsTrueWhenMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", price: 50000m);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minPrice: 40000m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxPrice_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", price: 50000m);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", maxPrice: 40000m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Volatility Tests

    [Fact]
    public void CheckConditions_WithMinVolatility_ReturnsTrueWhenMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volatility: 5.0);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", minVolatility: 3.0);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxVolatility_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volatility: 10.0);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition = CreateCondition(signal: "TestSignal", maxVolatility: 5.0);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - SignalRules Tests

    [Fact]
    public void CheckConditions_WithSignalRules_ReturnsTrueWhenSignalRuleMatches()
    {
        // Arrange
        var metadata = new OrderMetadata { SignalRule = "BuyRule1" };
        var tradingPair = CreateTradingPair(metadata: metadata);
        var condition = CreateCondition(signalRules: new List<string> { "BuyRule1", "BuyRule2" });
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithSignalRules_ReturnsFalseWhenSignalRuleNotInList()
    {
        // Arrange
        var metadata = new OrderMetadata { SignalRule = "BuyRule3" };
        var tradingPair = CreateTradingPair(metadata: metadata);
        var condition = CreateCondition(signalRules: new List<string> { "BuyRule1", "BuyRule2" });
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithSignalRules_ReturnsFalseWhenSignalRuleIsNull()
    {
        // Arrange
        var metadata = new OrderMetadata { SignalRule = null };
        var tradingPair = CreateTradingPair(metadata: metadata);
        var condition = CreateCondition(signalRules: new List<string> { "BuyRule1", "BuyRule2" });
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region CheckConditions - Multiple Conditions Tests

    [Fact]
    public void CheckConditions_WithMultipleConditions_ReturnsTrueWhenAllMet()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 1000000, rating: 0.8);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition1 = CreateCondition(signal: "TestSignal", minVolume: 500000);
        var condition2 = CreateCondition(signal: "TestSignal", minRating: 0.5);
        var conditions = new List<IRuleCondition> { condition1, condition2 };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMultipleConditions_ReturnsFalseWhenOneFails()
    {
        // Arrange
        var signal = CreateSignal("TestSignal", "BTCUSDT", volume: 100000, rating: 0.8);
        var signals = new Dictionary<string, ISignal> { { "TestSignal", signal } };
        var condition1 = CreateCondition(signal: "TestSignal", minVolume: 500000); // This will fail
        var condition2 = CreateCondition(signal: "TestSignal", minRating: 0.5);
        var conditions = new List<IRuleCondition> { condition1, condition2 };

        // Act
        var result = _sut.CheckConditions(conditions, signals, null, null, null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithEmptyConditions_ReturnsTrue()
    {
        // Arrange
        var conditions = new List<IRuleCondition>();

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, null, null);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region CheckConditions - MarginChange Tests

    [Fact]
    public void CheckConditions_WithMinMarginChange_ReturnsTrueWhenMet()
    {
        // Arrange
        var metadata = new OrderMetadata { LastBuyMargin = 2.0m };
        var tradingPair = CreateTradingPair(currentMargin: 7.0m, metadata: metadata);
        var condition = CreateCondition(minMarginChange: 3.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CheckConditions_WithMaxMarginChange_ReturnsFalseWhenExceeded()
    {
        // Arrange
        var metadata = new OrderMetadata { LastBuyMargin = 2.0m };
        var tradingPair = CreateTradingPair(currentMargin: 10.0m, metadata: metadata);
        var condition = CreateCondition(maxMarginChange: 5.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CheckConditions_WithMarginChangeCondition_ReturnsFalseWhenLastBuyMarginIsNull()
    {
        // Arrange
        var metadata = new OrderMetadata { LastBuyMargin = null };
        var tradingPair = CreateTradingPair(currentMargin: 5.0m, metadata: metadata);
        var condition = CreateCondition(minMarginChange: 3.0m);
        var conditions = new List<IRuleCondition> { condition };

        // Act
        var result = _sut.CheckConditions(conditions, new Dictionary<string, ISignal>(), null, "BTCUSDT", tradingPair);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Callback Registration Tests

    [Fact]
    public void RegisterRulesChangeCallback_AddsCallback()
    {
        // Arrange
        var callbackInvoked = false;
        Action callback = () => callbackInvoked = true;

        // Act
        _sut.RegisterRulesChangeCallback(callback);

        // Trigger config reload to invoke callbacks
        InvokeOnConfigReloaded(_sut);

        // Assert
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void UnregisterRulesChangeCallback_RemovesCallback()
    {
        // Arrange
        var callbackInvoked = false;
        Action callback = () => callbackInvoked = true;
        _sut.RegisterRulesChangeCallback(callback);
        _sut.UnregisterRulesChangeCallback(callback);

        // Act
        InvokeOnConfigReloaded(_sut);

        // Assert
        callbackInvoked.Should().BeFalse();
    }

    [Fact]
    public void RegisterRulesChangeCallback_AllowsMultipleCallbacks()
    {
        // Arrange
        var callback1Invoked = false;
        var callback2Invoked = false;
        Action callback1 = () => callback1Invoked = true;
        Action callback2 = () => callback2Invoked = true;

        // Act
        _sut.RegisterRulesChangeCallback(callback1);
        _sut.RegisterRulesChangeCallback(callback2);
        InvokeOnConfigReloaded(_sut);

        // Assert
        callback1Invoked.Should().BeTrue();
        callback2Invoked.Should().BeTrue();
    }

    [Fact]
    public void OnConfigReloaded_InvokesAllCallbacks()
    {
        // Arrange
        var invokeCount = 0;
        Action callback1 = () => invokeCount++;
        Action callback2 = () => invokeCount++;
        Action callback3 = () => invokeCount++;
        _sut.RegisterRulesChangeCallback(callback1);
        _sut.RegisterRulesChangeCallback(callback2);
        _sut.RegisterRulesChangeCallback(callback3);

        // Act
        InvokeOnConfigReloaded(_sut);

        // Assert
        invokeCount.Should().Be(3);
    }

    [Fact]
    public void UnregisterRulesChangeCallback_OnlyRemovesSpecifiedCallback()
    {
        // Arrange
        var callback1Invoked = false;
        var callback2Invoked = false;
        Action callback1 = () => callback1Invoked = true;
        Action callback2 = () => callback2Invoked = true;
        _sut.RegisterRulesChangeCallback(callback1);
        _sut.RegisterRulesChangeCallback(callback2);
        _sut.UnregisterRulesChangeCallback(callback1);

        // Act
        InvokeOnConfigReloaded(_sut);

        // Assert
        callback1Invoked.Should().BeFalse();
        callback2Invoked.Should().BeTrue();
    }

    private void InvokeOnConfigReloaded(TestableRulesService service)
    {
        service.TriggerOnConfigReloaded();
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsRulesServiceConstant()
    {
        // Act
        var serviceName = _sut.ServiceName;

        // Assert
        serviceName.Should().Be(Constants.ServiceNames.RulesService);
    }

    #endregion
}

/// <summary>
/// Testable implementation of RulesService for unit testing purposes.
/// Mirrors the behavior of the internal RulesService class from IntelliTrader.Rules.
/// </summary>
internal class TestableRulesService : IRulesService
{
    public string ServiceName => Constants.ServiceNames.RulesService;

    public IRulesConfig Config { get; private set; } = null!;

    private readonly ILoggingService _loggingService;
    private readonly List<Action> _rulesChangeCallbacks = new List<Action>();

    public TestableRulesService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public IConfigurationSection RawConfig => throw new NotImplementedException();

    public IModuleRules GetRules(string module)
    {
        IModuleRules? moduleRules = Config.Modules.FirstOrDefault(m => m.Module == module);
        if (moduleRules != null)
        {
            return moduleRules;
        }
        else
        {
            throw new Exception($"Unable to find rules for {module}");
        }
    }

    public bool CheckConditions(IEnumerable<IRuleCondition> conditions, Dictionary<string, ISignal> signals, double? globalRating, string? pair, ITradingPair? tradingPair)
    {
        foreach (var condition in conditions)
        {
            ISignal? signal = null;
            if (condition.Signal != null && signals.TryGetValue(condition.Signal, out ISignal? s))
            {
                signal = s;
            }

            if (condition.MinVolume != null && (signal == null || signal.Volume == null || signal.Volume < condition.MinVolume) ||
                condition.MaxVolume != null && (signal == null || signal.Volume == null || signal.Volume > condition.MaxVolume) ||
                condition.MinVolumeChange != null && (signal == null || signal.VolumeChange == null || signal.VolumeChange < condition.MinVolumeChange) ||
                condition.MaxVolumeChange != null && (signal == null || signal.VolumeChange == null || signal.VolumeChange > condition.MaxVolumeChange) ||
                condition.MinPrice != null && (signal == null || signal.Price == null || signal.Price < condition.MinPrice) ||
                condition.MaxPrice != null && (signal == null || signal.Price == null || signal.Price > condition.MaxPrice) ||
                condition.MinPriceChange != null && (signal == null || signal.PriceChange == null || signal.PriceChange < condition.MinPriceChange) ||
                condition.MaxPriceChange != null && (signal == null || signal.PriceChange == null || signal.PriceChange > condition.MaxPriceChange) ||
                condition.MinRating != null && (signal == null || signal.Rating == null || signal.Rating < condition.MinRating) ||
                condition.MaxRating != null && (signal == null || signal.Rating == null || signal.Rating > condition.MaxRating) ||
                condition.MinRatingChange != null && (signal == null || signal.RatingChange == null || signal.RatingChange < condition.MinRatingChange) ||
                condition.MaxRatingChange != null && (signal == null || signal.RatingChange == null || signal.RatingChange > condition.MaxRatingChange) ||
                condition.MinVolatility != null && (signal == null || signal.Volatility == null || signal.Volatility < condition.MinVolatility) ||
                condition.MaxVolatility != null && (signal == null || signal.Volatility == null || signal.Volatility > condition.MaxVolatility) ||
                condition.MinGlobalRating != null && (globalRating == null || globalRating < condition.MinGlobalRating) ||
                condition.MaxGlobalRating != null && (globalRating == null || globalRating > condition.MaxGlobalRating) ||
                condition.Pairs != null && (pair == null || !condition.Pairs.Contains(pair)) ||

                condition.MinAge != null && (tradingPair == null || tradingPair.CurrentAge < condition.MinAge / Application.Speed) ||
                condition.MaxAge != null && (tradingPair == null || tradingPair.CurrentAge > condition.MaxAge / Application.Speed) ||
                condition.MinLastBuyAge != null && (tradingPair == null || tradingPair.LastBuyAge < condition.MinLastBuyAge / Application.Speed) ||
                condition.MaxLastBuyAge != null && (tradingPair == null || tradingPair.LastBuyAge > condition.MaxLastBuyAge / Application.Speed) ||
                condition.MinMargin != null && (tradingPair == null || tradingPair.CurrentMargin < condition.MinMargin) ||
                condition.MaxMargin != null && (tradingPair == null || tradingPair.CurrentMargin > condition.MaxMargin) ||
                condition.MinMarginChange != null && (tradingPair == null || tradingPair.Metadata.LastBuyMargin == null || (tradingPair.CurrentMargin - tradingPair.Metadata.LastBuyMargin) < condition.MinMarginChange) ||
                condition.MaxMarginChange != null && (tradingPair == null || tradingPair.Metadata.LastBuyMargin == null || (tradingPair.CurrentMargin - tradingPair.Metadata.LastBuyMargin) > condition.MaxMarginChange) ||
                condition.MinAmount != null && (tradingPair == null || tradingPair.TotalAmount < condition.MinAmount) ||
                condition.MaxAmount != null && (tradingPair == null || tradingPair.TotalAmount > condition.MaxAmount) ||
                condition.MinCost != null && (tradingPair == null || tradingPair.CurrentCost < condition.MinCost) ||
                condition.MaxCost != null && (tradingPair == null || tradingPair.CurrentCost > condition.MaxCost) ||
                condition.MinDCALevel != null && (tradingPair == null || tradingPair.DCALevel < condition.MinDCALevel) ||
                condition.MaxDCALevel != null && (tradingPair == null || tradingPair.DCALevel > condition.MaxDCALevel) ||
                condition.SignalRules != null && (tradingPair == null || tradingPair.Metadata.SignalRule == null || !condition.SignalRules.Contains(tradingPair.Metadata.SignalRule)))
            {
                return false;
            }
        }
        return true;
    }

    public void RegisterRulesChangeCallback(Action callback)
    {
        _rulesChangeCallbacks.Add(callback);
    }

    public void UnregisterRulesChangeCallback(Action callback)
    {
        _rulesChangeCallbacks.Remove(callback);
    }

    /// <summary>
    /// Triggers the OnConfigReloaded behavior for testing callback invocations.
    /// </summary>
    public void TriggerOnConfigReloaded()
    {
        foreach (var callback in _rulesChangeCallbacks)
        {
            callback();
        }
    }

    /// <summary>
    /// Injects a configuration for testing purposes.
    /// </summary>
    public void SetConfig(IRulesConfig config)
    {
        Config = config;
    }
}
