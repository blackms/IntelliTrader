using IntelliTrader.Domain.Rules;
using IntelliTrader.Domain.Rules.Services;

namespace IntelliTrader.Domain.Tests.Rules;

public class RuleEvaluatorTests
{
    private readonly RuleEvaluator _sut = new();

    #region Empty Conditions

    [Fact]
    public void Evaluate_WithNoConditions_ReturnsTrue()
    {
        // Arrange
        var conditions = Array.Empty<RuleCondition>();
        var context = new RuleEvaluationContext();

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithEmptyCondition_ReturnsTrue()
    {
        // Arrange
        var conditions = new[] { new RuleCondition() };
        var context = new RuleEvaluationContext();

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Volume Conditions

    [Theory]
    [InlineData(100_000, 150_000, true)]  // Volume >= MinVolume
    [InlineData(100_000, 100_000, true)]  // Volume == MinVolume
    [InlineData(100_000, 50_000, false)]  // Volume < MinVolume
    public void Evaluate_MinVolumeCondition_EvaluatesCorrectly(long minVolume, long actualVolume, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Signal = "TV-15mins", MinVolume = minVolume } };
        var context = CreateContextWithSignal("TV-15mins", volume: actualVolume);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(200_000, 150_000, true)]  // Volume <= MaxVolume
    [InlineData(200_000, 200_000, true)]  // Volume == MaxVolume
    [InlineData(200_000, 250_000, false)] // Volume > MaxVolume
    public void Evaluate_MaxVolumeCondition_EvaluatesCorrectly(long maxVolume, long actualVolume, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Signal = "TV-15mins", MaxVolume = maxVolume } };
        var context = CreateContextWithSignal("TV-15mins", volume: actualVolume);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_MinVolumeCondition_WhenSignalMissing_ReturnsFalse()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Signal = "TV-15mins", MinVolume = 100_000 } };
        var context = new RuleEvaluationContext { Signals = new Dictionary<string, SignalSnapshot>() };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MinVolumeCondition_WhenVolumeNull_ReturnsFalse()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Signal = "TV-15mins", MinVolume = 100_000 } };
        var context = CreateContextWithSignal("TV-15mins", volume: null);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Rating Conditions

    [Theory]
    [InlineData(0.3, 0.5, true)]   // Rating >= MinRating
    [InlineData(0.3, 0.3, true)]   // Rating == MinRating
    [InlineData(0.3, 0.2, false)]  // Rating < MinRating
    public void Evaluate_MinRatingCondition_EvaluatesCorrectly(double minRating, double actualRating, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Signal = "TV-15mins", MinRating = minRating } };
        var context = CreateContextWithSignal("TV-15mins", rating: actualRating);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.8, 0.5, true)]   // Rating <= MaxRating
    [InlineData(0.8, 0.8, true)]   // Rating == MaxRating
    [InlineData(0.8, 0.9, false)]  // Rating > MaxRating
    public void Evaluate_MaxRatingCondition_EvaluatesCorrectly(double maxRating, double actualRating, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Signal = "TV-15mins", MaxRating = maxRating } };
        var context = CreateContextWithSignal("TV-15mins", rating: actualRating);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Global Rating Conditions

    [Theory]
    [InlineData(0.2, 0.3, true)]   // GlobalRating >= MinGlobalRating
    [InlineData(0.2, 0.2, true)]   // GlobalRating == MinGlobalRating
    [InlineData(0.2, 0.1, false)]  // GlobalRating < MinGlobalRating
    public void Evaluate_MinGlobalRatingCondition_EvaluatesCorrectly(double minGlobalRating, double actualGlobalRating, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MinGlobalRating = minGlobalRating } };
        var context = new RuleEvaluationContext { GlobalRating = actualGlobalRating };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_MinGlobalRatingCondition_WhenGlobalRatingNull_ReturnsFalse()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MinGlobalRating = 0.2 } };
        var context = new RuleEvaluationContext { GlobalRating = null };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Pair Whitelist Conditions

    [Fact]
    public void Evaluate_PairWhitelistCondition_WhenPairInList_ReturnsTrue()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Pairs = new List<string> { "BTCUSDT", "ETHUSDT" } } };
        var context = new RuleEvaluationContext { Pair = "BTCUSDT" };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_PairWhitelistCondition_WhenPairNotInList_ReturnsFalse()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Pairs = new List<string> { "BTCUSDT", "ETHUSDT" } } };
        var context = new RuleEvaluationContext { Pair = "ADAUSDT" };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_PairWhitelistCondition_WhenPairNull_ReturnsFalse()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { Pairs = new List<string> { "BTCUSDT" } } };
        var context = new RuleEvaluationContext { Pair = null };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Position Margin Conditions

    [Theory]
    [InlineData(5.0, 10.0, true)]   // Margin >= MinMargin
    [InlineData(5.0, 5.0, true)]    // Margin == MinMargin
    [InlineData(5.0, 3.0, false)]   // Margin < MinMargin
    public void Evaluate_MinMarginCondition_EvaluatesCorrectly(decimal minMargin, decimal actualMargin, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MinMargin = minMargin } };
        var context = CreateContextWithPosition(currentMargin: actualMargin);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(-5.0, -10.0, true)]   // Margin <= MaxMargin
    [InlineData(-5.0, -5.0, true)]    // Margin == MaxMargin
    [InlineData(-5.0, -3.0, false)]   // Margin > MaxMargin
    public void Evaluate_MaxMarginCondition_EvaluatesCorrectly(decimal maxMargin, decimal actualMargin, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MaxMargin = maxMargin } };
        var context = CreateContextWithPosition(currentMargin: actualMargin);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_MinMarginCondition_WhenNoPosition_ReturnsFalse()
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MinMargin = 5.0m } };
        var context = new RuleEvaluationContext { Position = null };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DCA Level Conditions

    [Theory]
    [InlineData(2, 3, true)]   // DCALevel >= MinDCALevel
    [InlineData(2, 2, true)]   // DCALevel == MinDCALevel
    [InlineData(2, 1, false)]  // DCALevel < MinDCALevel
    public void Evaluate_MinDCALevelCondition_EvaluatesCorrectly(int minDCALevel, int actualDCALevel, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MinDCALevel = minDCALevel } };
        var context = CreateContextWithPosition(dcaLevel: actualDCALevel);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(3, 2, true)]   // DCALevel <= MaxDCALevel
    [InlineData(3, 3, true)]   // DCALevel == MaxDCALevel
    [InlineData(3, 4, false)]  // DCALevel > MaxDCALevel
    public void Evaluate_MaxDCALevelCondition_EvaluatesCorrectly(int maxDCALevel, int actualDCALevel, bool expected)
    {
        // Arrange
        var conditions = new[] { new RuleCondition { MaxDCALevel = maxDCALevel } };
        var context = CreateContextWithPosition(dcaLevel: actualDCALevel);

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Multiple Conditions (AND Logic)

    [Fact]
    public void Evaluate_MultipleConditions_AllMustPass()
    {
        // Arrange - Two conditions, both should pass
        var conditions = new[]
        {
            new RuleCondition { Signal = "TV-15mins", MinRating = 0.3 },
            new RuleCondition { MinGlobalRating = 0.2 }
        };
        var context = new RuleEvaluationContext
        {
            GlobalRating = 0.3,
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", Rating = 0.5 }
            }
        };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MultipleConditions_FailsIfAnyFails()
    {
        // Arrange - Two conditions, one fails
        var conditions = new[]
        {
            new RuleCondition { Signal = "TV-15mins", MinRating = 0.3 },
            new RuleCondition { MinGlobalRating = 0.5 }  // This will fail
        };
        var context = new RuleEvaluationContext
        {
            GlobalRating = 0.3,  // Below MinGlobalRating
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", Rating = 0.5 }
            }
        };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_SingleConditionWithMultipleConstraints_AllMustPass()
    {
        // Arrange - Single condition with multiple constraints
        var conditions = new[]
        {
            new RuleCondition
            {
                Signal = "TV-15mins",
                MinRating = 0.3,
                MinVolume = 100_000,
                MinGlobalRating = 0.2
            }
        };
        var context = new RuleEvaluationContext
        {
            GlobalRating = 0.3,
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot
                {
                    Name = "TV-15mins",
                    Rating = 0.5,
                    Volume = 150_000
                }
            }
        };

        // Act
        var result = _sut.Evaluate(conditions, context);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static RuleEvaluationContext CreateContextWithSignal(
        string signalName,
        long? volume = null,
        double? rating = null,
        double? volatility = null)
    {
        return new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                [signalName] = new SignalSnapshot
                {
                    Name = signalName,
                    Volume = volume,
                    Rating = rating,
                    Volatility = volatility
                }
            }
        };
    }

    private static RuleEvaluationContext CreateContextWithPosition(
        decimal currentMargin = 0,
        int dcaLevel = 0,
        TimeSpan? currentAge = null,
        TimeSpan? lastBuyAge = null)
    {
        return new RuleEvaluationContext
        {
            Position = new PositionSnapshot
            {
                CurrentMargin = currentMargin,
                DCALevel = dcaLevel,
                CurrentAge = currentAge ?? TimeSpan.Zero,
                LastBuyAge = lastBuyAge ?? TimeSpan.Zero
            }
        };
    }

    #endregion
}
