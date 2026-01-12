using IntelliTrader.Domain.Signals.ValueObjects;

namespace IntelliTrader.Domain.Tests.Signals.ValueObjects;

public class SignalRatingTests
{
    #region Create

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(-0.5)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void Create_WithValidValue_CreatesRating(double value)
    {
        // Act
        var rating = SignalRating.Create(value);

        // Assert
        rating.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-1.5)]
    [InlineData(2.0)]
    public void Create_WithOutOfRangeValue_ThrowsArgumentOutOfRangeException(double value)
    {
        // Act
        var act = () => SignalRating.Create(value);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_WithInvalidValue_ThrowsArgumentException(double value)
    {
        // Act
        var act = () => SignalRating.Create(value);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-1.5, -1.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(-2.0, -1.0)]
    public void Create_WithClampTrue_ClampsValue(double value, double expectedClampedValue)
    {
        // Act
        var rating = SignalRating.Create(value, clamp: true);

        // Assert
        rating.Value.Should().Be(expectedClampedValue);
    }

    #endregion

    #region Static Factory Methods

    [Fact]
    public void Neutral_ReturnsZeroRating()
    {
        // Act
        var rating = SignalRating.Neutral;

        // Assert
        rating.Value.Should().Be(0);
    }

    [Fact]
    public void StrongBuy_ReturnsMaxValue()
    {
        // Act
        var rating = SignalRating.StrongBuy;

        // Assert
        rating.Value.Should().Be(SignalRating.MaxValue);
    }

    [Fact]
    public void StrongSell_ReturnsMinValue()
    {
        // Act
        var rating = SignalRating.StrongSell;

        // Assert
        rating.Value.Should().Be(SignalRating.MinValue);
    }

    #endregion

    #region Properties

    [Theory]
    [InlineData(0.5, true, false)]
    [InlineData(-0.5, false, true)]
    [InlineData(0, false, false)]
    public void IsBuySignal_IsSellSignal_ReturnCorrectValues(double value, bool expectedBuy, bool expectedSell)
    {
        // Arrange
        var rating = SignalRating.Create(value);

        // Assert
        rating.IsBuySignal.Should().Be(expectedBuy);
        rating.IsSellSignal.Should().Be(expectedSell);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.005, true)]
    [InlineData(-0.005, true)]
    [InlineData(0.02, false)]
    [InlineData(-0.02, false)]
    public void IsNeutral_WithDefaultTolerance_ReturnsCorrectResult(double value, bool expected)
    {
        // Arrange
        var rating = SignalRating.Create(value);

        // Act
        var result = rating.IsNeutral();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.5, 0.5, true)]
    [InlineData(0.6, 0.5, true)]
    [InlineData(-0.6, 0.5, true)]
    [InlineData(0.4, 0.5, false)]
    [InlineData(-0.4, 0.5, false)]
    public void IsStrong_ReturnsCorrectResult(double value, double threshold, bool expected)
    {
        // Arrange
        var rating = SignalRating.Create(value);

        // Act
        var result = rating.IsStrong(threshold);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.75, 0.75)]
    [InlineData(-0.5, 0.5)]
    [InlineData(0, 0)]
    public void Strength_ReturnsAbsoluteValue(double value, double expectedStrength)
    {
        // Arrange
        var rating = SignalRating.Create(value);

        // Act
        var strength = rating.Strength;

        // Assert
        strength.Should().Be(expectedStrength);
    }

    #endregion

    #region Operations

    [Theory]
    [InlineData(0.7, 0.3, 0.4)]
    [InlineData(0.3, 0.7, -0.4)]
    [InlineData(-0.5, 0.5, -1.0)]
    public void ChangeFrom_CalculatesCorrectly(double current, double previous, double expectedChange)
    {
        // Arrange
        var currentRating = SignalRating.Create(current);
        var previousRating = SignalRating.Create(previous);

        // Act
        var change = currentRating.ChangeFrom(previousRating);

        // Assert
        change.Should().BeApproximately(expectedChange, 0.0001);
    }

    [Fact]
    public void Round_RoundsToSpecifiedDecimals()
    {
        // Arrange
        var rating = SignalRating.Create(0.5678);

        // Act
        var result = rating.Round(2);

        // Assert
        result.Value.Should().Be(0.57);
    }

    #endregion

    #region Comparison Operators

    [Theory]
    [InlineData(0.5, 0.3, true, false)]
    [InlineData(0.3, 0.5, false, true)]
    [InlineData(0.5, 0.5, false, false)]
    [InlineData(-0.3, -0.5, true, false)]
    public void ComparisonOperators_WorkCorrectly(double value1, double value2, bool expectedGreater, bool expectedLess)
    {
        // Arrange
        var r1 = SignalRating.Create(value1);
        var r2 = SignalRating.Create(value2);

        // Assert
        (r1 > r2).Should().Be(expectedGreater);
        (r1 < r2).Should().Be(expectedLess);
        (r1 >= r2).Should().Be(expectedGreater || value1 == value2);
        (r1 <= r2).Should().Be(expectedLess || value1 == value2);
    }

    [Fact]
    public void CompareTo_ReturnsCorrectResult()
    {
        // Arrange
        var r1 = SignalRating.Create(0.7);
        var r2 = SignalRating.Create(0.3);

        // Assert
        r1.CompareTo(r2).Should().BePositive();
        r2.CompareTo(r1).Should().BeNegative();
        r1.CompareTo(r1).Should().Be(0);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        // Arrange
        var r1 = SignalRating.Create(0.5);
        var r2 = SignalRating.Create(0.5);

        // Assert
        r1.Should().Be(r2);
    }

    [Fact]
    public void Equals_WithDifferentValue_ReturnsFalse()
    {
        // Arrange
        var r1 = SignalRating.Create(0.5);
        var r2 = SignalRating.Create(0.3);

        // Assert
        r1.Should().NotBe(r2);
    }

    #endregion

    #region Implicit Conversion

    [Fact]
    public void ImplicitConversion_ToDouble_ReturnsValue()
    {
        // Arrange
        var rating = SignalRating.Create(0.75);

        // Act
        double value = rating;

        // Assert
        value.Should().Be(0.75);
    }

    #endregion
}
