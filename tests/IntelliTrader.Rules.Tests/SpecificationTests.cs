using FluentAssertions;
using Moq;
using IntelliTrader.Core;
using IntelliTrader.Rules.Specifications;
using Xunit;

namespace IntelliTrader.Rules.Tests;

public class SpecificationTests
{
    #region Helper Methods

    private static ConditionContext CreateContext(
        ISignal? signal = null,
        double? globalRating = null,
        string? pair = null,
        ITradingPair? tradingPair = null,
        double speed = 1.0)
    {
        return new ConditionContext(signal, globalRating, pair, tradingPair, speed);
    }

    private static ISignal CreateSignal(
        string name = "TestSignal",
        string pair = "BTCUSDT",
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
        mockPair.Setup(x => x.CurrentMargin).Returns(currentMargin);
        mockPair.Setup(x => x.CurrentAge).Returns(currentAge);
        mockPair.Setup(x => x.LastBuyAge).Returns(lastBuyAge);
        mockPair.Setup(x => x.DCALevel).Returns(dcaLevel);
        mockPair.Setup(x => x.CurrentCost).Returns(currentCost);
        mockPair.Setup(x => x.Metadata).Returns(metadata ?? new OrderMetadata());
        return mockPair.Object;
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

    #endregion

    #region AgeSpecification Tests

    [Fact]
    public void AgeSpecification_WithMinAge_ReturnsTrueWhenAgeExceedsThreshold()
    {
        // Arrange - speed=1, minAge=50, currentAge=100 => threshold is 50/1=50
        var tradingPair = CreateTradingPair(currentAge: 100);
        var context = CreateContext(tradingPair: tradingPair, speed: 1.0);
        var spec = new AgeSpecification(minAge: 50, maxAge: null, minLastBuyAge: null, maxLastBuyAge: null);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void AgeSpecification_WithMinAge_ReturnsFalseWhenAgeBelowThreshold()
    {
        // Arrange - speed=1, minAge=200, currentAge=100
        var tradingPair = CreateTradingPair(currentAge: 100);
        var context = CreateContext(tradingPair: tradingPair, speed: 1.0);
        var spec = new AgeSpecification(minAge: 200, maxAge: null, minLastBuyAge: null, maxLastBuyAge: null);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void AgeSpecification_WithSpeedMultiplier_AdjustsAgeThreshold()
    {
        // Arrange - speed=2, minAge=200, currentAge=80
        // threshold = 200/2 = 100, currentAge(80) < 100 => false
        var tradingPair = CreateTradingPair(currentAge: 80);
        var context = CreateContext(tradingPair: tradingPair, speed: 2.0);
        var spec = new AgeSpecification(minAge: 200, maxAge: null, minLastBuyAge: null, maxLastBuyAge: null);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void AgeSpecification_WithSpeedMultiplier_PassesWhenAgeExceedsAdjustedThreshold()
    {
        // Arrange - speed=2, minAge=200, currentAge=150
        // threshold = 200/2 = 100, currentAge(150) >= 100 => true
        var tradingPair = CreateTradingPair(currentAge: 150);
        var context = CreateContext(tradingPair: tradingPair, speed: 2.0);
        var spec = new AgeSpecification(minAge: 200, maxAge: null, minLastBuyAge: null, maxLastBuyAge: null);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void AgeSpecification_WithMaxLastBuyAge_ReturnsFalseWhenExceeded()
    {
        // Arrange - speed=1, maxLastBuyAge=30, lastBuyAge=50
        var tradingPair = CreateTradingPair(lastBuyAge: 50);
        var context = CreateContext(tradingPair: tradingPair, speed: 1.0);
        var spec = new AgeSpecification(minAge: null, maxAge: null, minLastBuyAge: null, maxLastBuyAge: 30);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void AgeSpecification_WithNullTradingPair_ReturnsFalse()
    {
        var context = CreateContext(tradingPair: null, speed: 1.0);
        var spec = new AgeSpecification(minAge: 10, maxAge: null, minLastBuyAge: null, maxLastBuyAge: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region VolumeChange Tests

    [Fact]
    public void VolumeSpecification_WithMinVolumeChange_ReturnsTrueWhenMet()
    {
        var signal = CreateSignal(volumeChange: 15.0);
        var context = CreateContext(signal: signal);
        var spec = new VolumeSpecification(null, null, minVolumeChange: 10.0, maxVolumeChange: null);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void VolumeSpecification_WithMaxVolumeChange_ReturnsFalseWhenExceeded()
    {
        var signal = CreateSignal(volumeChange: 25.0);
        var context = CreateContext(signal: signal);
        var spec = new VolumeSpecification(null, null, minVolumeChange: null, maxVolumeChange: 20.0);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void VolumeSpecification_WithNullVolumeChange_ReturnsFalseWhenConstraintSet()
    {
        var signal = CreateSignal(volumeChange: null);
        var context = CreateContext(signal: signal);
        var spec = new VolumeSpecification(null, null, minVolumeChange: 5.0, maxVolumeChange: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region PriceChange Tests

    [Fact]
    public void PriceSpecification_WithMinPriceChange_ReturnsTrueWhenMet()
    {
        var signal = CreateSignal(priceChange: 5.0m);
        var context = CreateContext(signal: signal);
        var spec = new PriceSpecification(null, null, minPriceChange: 3.0m, maxPriceChange: null);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void PriceSpecification_WithMaxPriceChange_ReturnsFalseWhenExceeded()
    {
        var signal = CreateSignal(priceChange: 10.0m);
        var context = CreateContext(signal: signal);
        var spec = new PriceSpecification(null, null, minPriceChange: null, maxPriceChange: 5.0m);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region RatingChange Tests

    [Fact]
    public void RatingSpecification_WithMinRatingChange_ReturnsTrueWhenMet()
    {
        var signal = CreateSignal(ratingChange: 0.3);
        var context = CreateContext(signal: signal);
        var spec = new RatingSpecification(null, null, minRatingChange: 0.2, maxRatingChange: null);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void RatingSpecification_WithMaxRatingChange_ReturnsFalseWhenExceeded()
    {
        var signal = CreateSignal(ratingChange: 0.8);
        var context = CreateContext(signal: signal);
        var spec = new RatingSpecification(null, null, minRatingChange: null, maxRatingChange: 0.5);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void RatingSpecification_WithNullSignal_ReturnsFalseWhenConstraintSet()
    {
        var context = CreateContext(signal: null);
        var spec = new RatingSpecification(minRating: 0.5, maxRating: null, minRatingChange: null, maxRatingChange: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region HasConstraints Tests

    [Fact]
    public void VolumeSpecification_HasConstraints_ReturnsFalseWhenAllNull()
    {
        var spec = new VolumeSpecification(null, null, null, null);
        spec.HasConstraints.Should().BeFalse();
    }

    [Fact]
    public void VolumeSpecification_HasConstraints_ReturnsTrueWhenAnySet()
    {
        var spec = new VolumeSpecification(null, null, minVolumeChange: 5.0, null);
        spec.HasConstraints.Should().BeTrue();
    }

    [Fact]
    public void AgeSpecification_HasConstraints_ReturnsFalseWhenAllNull()
    {
        var spec = new AgeSpecification(null, null, null, null);
        spec.HasConstraints.Should().BeFalse();
    }

    [Fact]
    public void DCALevelSpecification_HasConstraints_ReturnsTrueWhenMaxSet()
    {
        var spec = new DCALevelSpecification(null, maxDCALevel: 5);
        spec.HasConstraints.Should().BeTrue();
    }

    [Fact]
    public void GlobalRatingSpecification_HasConstraints_ReturnsFalseWhenAllNull()
    {
        var spec = new GlobalRatingSpecification(null, null);
        spec.HasConstraints.Should().BeFalse();
    }

    [Fact]
    public void PairsSpecification_HasConstraints_ReturnsFalseWhenNull()
    {
        var spec = new PairsSpecification(null);
        spec.HasConstraints.Should().BeFalse();
    }

    [Fact]
    public void SignalRulesSpecification_HasConstraints_ReturnsTrueWhenSet()
    {
        var spec = new SignalRulesSpecification(new List<string> { "Rule1" });
        spec.HasConstraints.Should().BeTrue();
    }

    #endregion

    #region Specification Combinator Tests

    [Fact]
    public void TrueSpecification_AlwaysReturnsTrue()
    {
        var spec = new TrueSpecification<ConditionContext>();
        var context = CreateContext();

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void AndSpecification_ReturnsFalseWhenLeftFails()
    {
        var left = new GlobalRatingSpecification(minGlobalRating: 0.9, maxGlobalRating: null);
        var right = new TrueSpecification<ConditionContext>();
        var and = new AndSpecification<ConditionContext>(left, right);
        var context = CreateContext(globalRating: 0.5);

        and.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void OrSpecification_ReturnsTrueWhenEitherPasses()
    {
        var left = new GlobalRatingSpecification(minGlobalRating: 0.9, maxGlobalRating: null); // fails
        var right = new TrueSpecification<ConditionContext>(); // passes
        var or = new OrSpecification<ConditionContext>(left, right);
        var context = CreateContext(globalRating: 0.5);

        or.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void NotSpecification_InvertsResult()
    {
        var inner = new GlobalRatingSpecification(minGlobalRating: 0.9, maxGlobalRating: null);
        var not = new NotSpecification<ConditionContext>(inner);
        var context = CreateContext(globalRating: 0.5);

        // Inner would return false (0.5 < 0.9), Not inverts to true
        not.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void CompositeAndSpecification_ReturnsFalseWhenAnyFails()
    {
        var specs = new List<ISpecification<ConditionContext>>
        {
            new TrueSpecification<ConditionContext>(),
            new GlobalRatingSpecification(minGlobalRating: 0.9, maxGlobalRating: null), // fails
            new TrueSpecification<ConditionContext>()
        };
        var composite = new CompositeAndSpecification<ConditionContext>(specs);
        var context = CreateContext(globalRating: 0.5);

        composite.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void CompositeAndSpecification_ReturnsTrueWhenAllPass()
    {
        var specs = new List<ISpecification<ConditionContext>>
        {
            new TrueSpecification<ConditionContext>(),
            new GlobalRatingSpecification(minGlobalRating: 0.3, maxGlobalRating: null),
            new TrueSpecification<ConditionContext>()
        };
        var composite = new CompositeAndSpecification<ConditionContext>(specs);
        var context = CreateContext(globalRating: 0.5);

        composite.IsSatisfiedBy(context).Should().BeTrue();
    }

    #endregion

    #region ConditionContext.Create Tests

    [Fact]
    public void ConditionContext_Create_ResolvesSignalByName()
    {
        var signal = CreateSignal("Sig1", "BTCUSDT", rating: 0.9);
        var signals = new Dictionary<string, ISignal> { { "Sig1", signal } };

        var context = ConditionContext.Create(signals, "Sig1", null, null, null, 1.0);

        context.Signal.Should().BeSameAs(signal);
    }

    [Fact]
    public void ConditionContext_Create_ReturnsNullSignalWhenNameNotFound()
    {
        var signals = new Dictionary<string, ISignal>();

        var context = ConditionContext.Create(signals, "Missing", null, null, null, 1.0);

        context.Signal.Should().BeNull();
    }

    [Fact]
    public void ConditionContext_Create_ReturnsNullSignalWhenNameIsNull()
    {
        var signals = new Dictionary<string, ISignal>();

        var context = ConditionContext.Create(signals, null, null, null, null, 1.0);

        context.Signal.Should().BeNull();
    }

    #endregion

    #region ConditionSpecificationBuilder Tests

    [Fact]
    public void ConditionSpecificationBuilder_WithNoConstraints_ReturnsTrueSpecification()
    {
        // A condition with all null values should produce a TrueSpecification
        var condition = CreateCondition();
        var spec = ConditionSpecificationBuilder.Build(condition);

        var context = CreateContext();
        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void ConditionSpecificationBuilder_WithMultipleConstraints_CombinesWithAnd()
    {
        // Condition requiring both min volume and min rating
        var condition = CreateCondition(signal: "Sig1", minVolume: 1000, minRating: 0.5);
        var spec = ConditionSpecificationBuilder.Build(condition);

        // Signal with sufficient volume but insufficient rating
        var signal = CreateSignal(volume: 2000, rating: 0.3);
        var signals = new Dictionary<string, ISignal> { { "Sig1", signal } };
        var context = ConditionContext.Create(signals, "Sig1", null, null, null, 1.0);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void ConditionSpecificationBuilder_WithMultipleConstraints_PassesWhenAllMet()
    {
        var condition = CreateCondition(signal: "Sig1", minVolume: 1000, minRating: 0.5);
        var spec = ConditionSpecificationBuilder.Build(condition);

        var signal = CreateSignal(volume: 2000, rating: 0.8);
        var signals = new Dictionary<string, ISignal> { { "Sig1", signal } };
        var context = ConditionContext.Create(signals, "Sig1", null, null, null, 1.0);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    #endregion

    #region VolatilitySpecification Edge Cases

    [Fact]
    public void VolatilitySpecification_WithNullSignal_ReturnsFalseWhenConstraintSet()
    {
        var context = CreateContext(signal: null);
        var spec = new VolatilitySpecification(minVolatility: 1.0, maxVolatility: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void VolatilitySpecification_WithNullVolatility_ReturnsFalseWhenConstraintSet()
    {
        var signal = CreateSignal(volatility: null);
        var context = CreateContext(signal: signal);
        var spec = new VolatilitySpecification(minVolatility: 1.0, maxVolatility: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region MarginSpecification Edge Cases

    [Fact]
    public void MarginSpecification_WithNullTradingPair_ReturnsFalseWhenConstraintSet()
    {
        var context = CreateContext(tradingPair: null);
        var spec = new MarginSpecification(minMargin: 1.0m, maxMargin: null, minMarginChange: null, maxMarginChange: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void MarginSpecification_WithMinMarginChange_CalculatesDiffCorrectly()
    {
        // currentMargin=10, lastBuyMargin=3, diff=7, minMarginChange=5 => 7>=5 => true
        var metadata = new OrderMetadata { LastBuyMargin = 3.0m };
        var tradingPair = CreateTradingPair(currentMargin: 10.0m, metadata: metadata);
        var context = CreateContext(tradingPair: tradingPair);
        var spec = new MarginSpecification(null, null, minMarginChange: 5.0m, maxMarginChange: null);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    #endregion

    #region AmountSpecification Edge Cases

    [Fact]
    public void AmountSpecification_WithNullTradingPair_ReturnsFalseWhenConstraintSet()
    {
        var context = CreateContext(tradingPair: null);
        var spec = new AmountSpecification(minAmount: 1.0m, maxAmount: null, minCost: null, maxCost: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void AmountSpecification_WithMinAndMaxCost_PassesWhenInRange()
    {
        var tradingPair = CreateTradingPair(currentCost: 150.0m);
        var context = CreateContext(tradingPair: tradingPair);
        var spec = new AmountSpecification(null, null, minCost: 100.0m, maxCost: 200.0m);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    #endregion

    #region DCALevelSpecification Edge Cases

    [Fact]
    public void DCALevelSpecification_WithNullTradingPair_ReturnsFalseWhenConstraintSet()
    {
        var context = CreateContext(tradingPair: null);
        var spec = new DCALevelSpecification(minDCALevel: 1, maxDCALevel: null);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    [Fact]
    public void DCALevelSpecification_WithMinAndMax_PassesWhenInRange()
    {
        var tradingPair = CreateTradingPair(dcaLevel: 3);
        var context = CreateContext(tradingPair: tradingPair);
        var spec = new DCALevelSpecification(minDCALevel: 1, maxDCALevel: 5);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void DCALevelSpecification_WithMinAndMax_FailsWhenOutOfRange()
    {
        var tradingPair = CreateTradingPair(dcaLevel: 7);
        var context = CreateContext(tradingPair: tradingPair);
        var spec = new DCALevelSpecification(minDCALevel: 1, maxDCALevel: 5);

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region SignalRulesSpecification Edge Cases

    [Fact]
    public void SignalRulesSpecification_WithNullTradingPair_ReturnsFalse()
    {
        var context = CreateContext(tradingPair: null);
        var spec = new SignalRulesSpecification(new List<string> { "Rule1" });

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region PairsSpecification Edge Cases

    [Fact]
    public void PairsSpecification_WithNullPairs_AlwaysReturnsTrue()
    {
        var context = CreateContext(pair: "ANYTHING");
        var spec = new PairsSpecification(null);

        spec.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void PairsSpecification_WithEmptyList_ReturnsFalse()
    {
        var context = CreateContext(pair: "BTCUSDT");
        var spec = new PairsSpecification(new List<string>());

        spec.IsSatisfiedBy(context).Should().BeFalse();
    }

    #endregion

    #region Specification Fluent API Tests

    [Fact]
    public void Specification_And_CombinesCorrectly()
    {
        var spec1 = new GlobalRatingSpecification(minGlobalRating: 0.3, maxGlobalRating: null);
        var spec2 = new GlobalRatingSpecification(minGlobalRating: null, maxGlobalRating: 0.9);
        var combined = spec1.And(spec2);

        var context = CreateContext(globalRating: 0.5);
        combined.IsSatisfiedBy(context).Should().BeTrue();
    }

    [Fact]
    public void Specification_Not_InvertsCorrectly()
    {
        var spec = new GlobalRatingSpecification(minGlobalRating: 0.9, maxGlobalRating: null);
        var negated = spec.Not();

        var context = CreateContext(globalRating: 0.5);
        negated.IsSatisfiedBy(context).Should().BeTrue();
    }

    #endregion
}
