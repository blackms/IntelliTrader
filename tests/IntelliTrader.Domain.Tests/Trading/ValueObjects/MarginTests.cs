using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.ValueObjects;

public class MarginTests
{
    #region FromPercentage

    [Theory]
    [InlineData(5.5)]
    [InlineData(0)]
    [InlineData(-10.25)]
    [InlineData(100)]
    public void FromPercentage_CreatesMargin(decimal percentage)
    {
        // Act
        var margin = Margin.FromPercentage(percentage);

        // Assert
        margin.Percentage.Should().Be(percentage);
    }

    [Fact]
    public void Zero_CreatesZeroMargin()
    {
        // Act
        var margin = Margin.Zero;

        // Assert
        margin.Percentage.Should().Be(0m);
        margin.IsBreakEven.Should().BeTrue();
    }

    #endregion

    #region Calculate

    [Theory]
    [InlineData(100, 110, 10)]
    [InlineData(100, 90, -10)]
    [InlineData(100, 200, 100)]
    [InlineData(100, 50, -50)]
    [InlineData(100, 100, 0)]
    public void Calculate_FromCostAndCurrentValue_CalculatesCorrectly(decimal cost, decimal currentValue, decimal expectedPercentage)
    {
        // Act
        var margin = Margin.Calculate(cost, currentValue);

        // Assert
        margin.Percentage.Should().Be(expectedPercentage);
    }

    [Fact]
    public void Calculate_WithZeroCost_ThrowsArgumentException()
    {
        // Act
        var act = () => Margin.Calculate(0, 100);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("cost");
    }

    #endregion

    #region Properties

    [Theory]
    [InlineData(5.5, true, false, false)]
    [InlineData(-3.2, false, true, false)]
    [InlineData(0, false, false, true)]
    public void Properties_ReturnCorrectValues(decimal percentage, bool isProfit, bool isLoss, bool isBreakEven)
    {
        // Arrange
        var margin = Margin.FromPercentage(percentage);

        // Assert
        margin.IsProfit.Should().Be(isProfit);
        margin.IsLoss.Should().Be(isLoss);
        margin.IsBreakEven.Should().Be(isBreakEven);
    }

    #endregion

    #region Operations

    [Fact]
    public void Abs_ReturnsAbsoluteValue()
    {
        // Arrange
        var margin = Margin.FromPercentage(-5.5m);

        // Act
        var result = margin.Abs();

        // Assert
        result.Percentage.Should().Be(5.5m);
    }

    [Theory]
    [InlineData(10, 5, 5)]
    [InlineData(5, 10, -5)]
    [InlineData(-5, -10, 5)]
    public void ChangeFrom_CalculatesCorrectly(decimal current, decimal previous, decimal expectedChange)
    {
        // Arrange
        var currentMargin = Margin.FromPercentage(current);
        var previousMargin = Margin.FromPercentage(previous);

        // Act
        var change = currentMargin.ChangeFrom(previousMargin);

        // Assert
        change.Percentage.Should().Be(expectedChange);
    }

    [Fact]
    public void Round_RoundsToSpecifiedDecimals()
    {
        // Arrange
        var margin = Margin.FromPercentage(5.5678m);

        // Act
        var result = margin.Round(2);

        // Assert
        result.Percentage.Should().Be(5.57m);
    }

    [Theory]
    [InlineData(100, 10, 110)]
    [InlineData(100, -10, 90)]
    [InlineData(100, 50, 150)]
    public void ApplyTo_AppliesMarginToCost(decimal cost, decimal percentage, decimal expectedResult)
    {
        // Arrange
        var margin = Margin.FromPercentage(percentage);

        // Act
        var result = margin.ApplyTo(cost);

        // Assert
        result.Should().Be(expectedResult);
    }

    #endregion

    #region Arithmetic Operators

    [Fact]
    public void Addition_ReturnsSum()
    {
        // Arrange
        var m1 = Margin.FromPercentage(5m);
        var m2 = Margin.FromPercentage(3m);

        // Act
        var result = m1 + m2;

        // Assert
        result.Percentage.Should().Be(8m);
    }

    [Fact]
    public void Subtraction_ReturnsDifference()
    {
        // Arrange
        var m1 = Margin.FromPercentage(10m);
        var m2 = Margin.FromPercentage(3m);

        // Act
        var result = m1 - m2;

        // Assert
        result.Percentage.Should().Be(7m);
    }

    [Fact]
    public void UnaryNegation_ReturnsNegated()
    {
        // Arrange
        var margin = Margin.FromPercentage(5m);

        // Act
        var result = -margin;

        // Assert
        result.Percentage.Should().Be(-5m);
    }

    #endregion

    #region Comparison Operators

    [Theory]
    [InlineData(10, 5, true, false)]
    [InlineData(5, 10, false, true)]
    [InlineData(10, 10, false, false)]
    [InlineData(-5, -10, true, false)]
    public void ComparisonOperators_WorkCorrectly(decimal value1, decimal value2, bool expectedGreater, bool expectedLess)
    {
        // Arrange
        var m1 = Margin.FromPercentage(value1);
        var m2 = Margin.FromPercentage(value2);

        // Assert
        (m1 > m2).Should().Be(expectedGreater);
        (m1 < m2).Should().Be(expectedLess);
        (m1 >= m2).Should().Be(expectedGreater || value1 == value2);
        (m1 <= m2).Should().Be(expectedLess || value1 == value2);
    }

    [Fact]
    public void CompareTo_ReturnsCorrectResult()
    {
        // Arrange
        var m1 = Margin.FromPercentage(10m);
        var m2 = Margin.FromPercentage(5m);

        // Assert
        m1.CompareTo(m2).Should().BePositive();
        m2.CompareTo(m1).Should().BeNegative();
        m1.CompareTo(m1).Should().Be(0);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSamePercentage_ReturnsTrue()
    {
        // Arrange
        var m1 = Margin.FromPercentage(5.5m);
        var m2 = Margin.FromPercentage(5.5m);

        // Assert
        m1.Should().Be(m2);
    }

    [Fact]
    public void Equals_WithDifferentPercentage_ReturnsFalse()
    {
        // Arrange
        var m1 = Margin.FromPercentage(5.5m);
        var m2 = Margin.FromPercentage(3.2m);

        // Assert
        m1.Should().NotBe(m2);
    }

    #endregion

    #region ToString and Implicit Conversion

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var margin = Margin.FromPercentage(5.5m);

        // Act
        var result = margin.ToString();

        // Assert - check contains percentage value and symbol (culture-independent)
        result.Should().Contain("5");
        result.Should().Contain("50");
        result.Should().EndWith("%");
    }

    [Fact]
    public void ImplicitConversion_ToDecimal_ReturnsPercentage()
    {
        // Arrange
        var margin = Margin.FromPercentage(5.5m);

        // Act
        decimal value = margin;

        // Assert
        value.Should().Be(5.5m);
    }

    #endregion
}
