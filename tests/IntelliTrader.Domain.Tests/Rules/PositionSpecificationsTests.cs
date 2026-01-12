using IntelliTrader.Domain.Rules;
using IntelliTrader.Domain.Rules.Specifications;

namespace IntelliTrader.Domain.Tests.Rules;

public class PositionSpecificationsTests
{
    #region Age Specifications

    [Theory]
    [InlineData(60, 120, true)]   // Position age 2 min >= MinAge 1 min
    [InlineData(60, 60, true)]    // Position age == MinAge
    [InlineData(60, 30, false)]   // Position age < MinAge
    public void MinAgeSpecification_EvaluatesCorrectly(double minAgeSeconds, double actualAgeSeconds, bool expected)
    {
        // Arrange
        var spec = new MinAgeSpecification(minAgeSeconds);
        var context = CreateContextWithPosition(currentAge: TimeSpan.FromSeconds(actualAgeSeconds));

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(300, 120, true)]   // Position age 2 min <= MaxAge 5 min
    [InlineData(300, 300, true)]   // Position age == MaxAge
    [InlineData(300, 600, false)]  // Position age > MaxAge
    public void MaxAgeSpecification_EvaluatesCorrectly(double maxAgeSeconds, double actualAgeSeconds, bool expected)
    {
        // Arrange
        var spec = new MaxAgeSpecification(maxAgeSeconds);
        var context = CreateContextWithPosition(currentAge: TimeSpan.FromSeconds(actualAgeSeconds));

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Fact]
    public void MinAgeSpecification_WithSpeedMultiplier_AdjustsThreshold()
    {
        // Arrange - With speed 2x, MinAge of 60s becomes effectively 30s
        var spec = new MinAgeSpecification(60); // 60 seconds
        var context = new RuleEvaluationContext
        {
            SpeedMultiplier = 2.0,
            Position = new PositionSnapshot
            {
                CurrentAge = TimeSpan.FromSeconds(40) // 40 seconds actual age
            }
        };

        // Act - 60s / 2.0 = 30s effective min age, 40s >= 30s should pass
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(30, 60, true)]   // LastBuyAge >= MinLastBuyAge
    [InlineData(30, 30, true)]
    [InlineData(30, 15, false)]
    public void MinLastBuyAgeSpecification_EvaluatesCorrectly(double minLastBuyAgeSeconds, double actualLastBuyAgeSeconds, bool expected)
    {
        // Arrange
        var spec = new MinLastBuyAgeSpecification(minLastBuyAgeSeconds);
        var context = CreateContextWithPosition(lastBuyAge: TimeSpan.FromSeconds(actualLastBuyAgeSeconds));

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Margin Change Specifications

    [Theory]
    [InlineData(-5.0, 10.0, 5.0, true)]    // MarginChange (5-10 = -5) >= MinMarginChange (-5)
    [InlineData(-5.0, 10.0, 3.0, false)]   // MarginChange (3-10 = -7) < MinMarginChange (-5)
    public void MinMarginChangeSpecification_EvaluatesCorrectly(decimal minMarginChange, decimal lastBuyMargin, decimal currentMargin, bool expected)
    {
        // Arrange
        var spec = new MinMarginChangeSpecification(minMarginChange);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot
            {
                CurrentMargin = currentMargin,
                LastBuyMargin = lastBuyMargin
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(5.0, 10.0, 12.0, true)]    // MarginChange (12-10 = 2) <= MaxMarginChange (5)
    [InlineData(5.0, 10.0, 20.0, false)]   // MarginChange (20-10 = 10) > MaxMarginChange (5)
    public void MaxMarginChangeSpecification_EvaluatesCorrectly(decimal maxMarginChange, decimal lastBuyMargin, decimal currentMargin, bool expected)
    {
        // Arrange
        var spec = new MaxMarginChangeSpecification(maxMarginChange);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot
            {
                CurrentMargin = currentMargin,
                LastBuyMargin = lastBuyMargin
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Fact]
    public void MarginChangeSpecification_WhenLastBuyMarginNull_ReturnsFalse()
    {
        // Arrange
        var spec = new MinMarginChangeSpecification(-5.0m);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot
            {
                CurrentMargin = 10.0m,
                LastBuyMargin = null
            }
        };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Amount Specifications

    [Theory]
    [InlineData(10.0, 15.0, true)]
    [InlineData(10.0, 10.0, true)]
    [InlineData(10.0, 5.0, false)]
    public void MinAmountSpecification_EvaluatesCorrectly(decimal minAmount, decimal actualAmount, bool expected)
    {
        // Arrange
        var spec = new MinAmountSpecification(minAmount);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { TotalAmount = actualAmount }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(20.0, 15.0, true)]
    [InlineData(20.0, 20.0, true)]
    [InlineData(20.0, 25.0, false)]
    public void MaxAmountSpecification_EvaluatesCorrectly(decimal maxAmount, decimal actualAmount, bool expected)
    {
        // Arrange
        var spec = new MaxAmountSpecification(maxAmount);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { TotalAmount = actualAmount }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Cost Specifications

    [Theory]
    [InlineData(100.0, 150.0, true)]
    [InlineData(100.0, 100.0, true)]
    [InlineData(100.0, 50.0, false)]
    public void MinCostSpecification_EvaluatesCorrectly(decimal minCost, decimal actualCost, bool expected)
    {
        // Arrange
        var spec = new MinCostSpecification(minCost);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { CurrentCost = actualCost }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(200.0, 150.0, true)]
    [InlineData(200.0, 200.0, true)]
    [InlineData(200.0, 250.0, false)]
    public void MaxCostSpecification_EvaluatesCorrectly(decimal maxCost, decimal actualCost, bool expected)
    {
        // Arrange
        var spec = new MaxCostSpecification(maxCost);
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { CurrentCost = actualCost }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Signal Rules Specifications

    [Fact]
    public void SignalRulesSpecification_WhenPositionSignalRuleMatches_ReturnsTrue()
    {
        // Arrange
        var spec = new SignalRulesSpecification(new[] { "buy_strong", "buy_moderate" });
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { SignalRule = "buy_strong" }
        };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void SignalRulesSpecification_WhenPositionSignalRuleDoesNotMatch_ReturnsFalse()
    {
        // Arrange
        var spec = new SignalRulesSpecification(new[] { "buy_strong", "buy_moderate" });
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { SignalRule = "buy_weak" }
        };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void SignalRulesSpecification_WhenPositionSignalRuleNull_ReturnsFalse()
    {
        // Arrange
        var spec = new SignalRulesSpecification(new[] { "buy_strong" });
        var context = new RuleEvaluationContext
        {
            Position = new PositionSnapshot { SignalRule = null }
        };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void PositionSpecification_WhenNoPosition_ReturnsFalse()
    {
        // Arrange
        var spec = new MinMarginSpecification(5.0m);
        var context = new RuleEvaluationContext { Position = null };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helpers

    private static RuleEvaluationContext CreateContextWithPosition(
        TimeSpan? currentAge = null,
        TimeSpan? lastBuyAge = null)
    {
        return new RuleEvaluationContext
        {
            Position = new PositionSnapshot
            {
                CurrentAge = currentAge ?? TimeSpan.Zero,
                LastBuyAge = lastBuyAge ?? TimeSpan.Zero
            }
        };
    }

    #endregion
}
