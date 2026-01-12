using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.ValueObjects;

public class PriceTests
{
    #region Create

    [Theory]
    [InlineData(0)]
    [InlineData(100.50)]
    [InlineData(0.00000001)]
    public void Create_WithValidValue_CreatesPrice(decimal value)
    {
        // Act
        var price = Price.Create(value);

        // Assert
        price.Value.Should().Be(value);
    }

    [Fact]
    public void Create_WithNegativeValue_ThrowsArgumentException()
    {
        // Act
        var act = () => Price.Create(-100m);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Zero_CreatesZeroPrice()
    {
        // Act
        var price = Price.Zero;

        // Assert
        price.Value.Should().Be(0m);
        price.IsZero.Should().BeTrue();
    }

    #endregion

    #region PercentageChangeFrom

    [Theory]
    [InlineData(110, 100, 10)]
    [InlineData(90, 100, -10)]
    [InlineData(200, 100, 100)]
    [InlineData(50, 100, -50)]
    [InlineData(100, 100, 0)]
    public void PercentageChangeFrom_CalculatesCorrectly(decimal newValue, decimal oldValue, decimal expectedChange)
    {
        // Arrange
        var newPrice = Price.Create(newValue);
        var oldPrice = Price.Create(oldValue);

        // Act
        var change = newPrice.PercentageChangeFrom(oldPrice);

        // Assert
        change.Should().Be(expectedChange);
    }

    [Fact]
    public void PercentageChangeFrom_ZeroPrice_ThrowsInvalidOperationException()
    {
        // Arrange
        var price = Price.Create(100m);
        var zeroPrice = Price.Zero;

        // Act
        var act = () => price.PercentageChangeFrom(zeroPrice);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region ApplyPercentageChange

    [Theory]
    [InlineData(100, 10, 110)]
    [InlineData(100, -10, 90)]
    [InlineData(100, 100, 200)]
    [InlineData(100, -50, 50)]
    [InlineData(100, 0, 100)]
    public void ApplyPercentageChange_AppliesCorrectly(decimal originalValue, decimal change, decimal expectedValue)
    {
        // Arrange
        var price = Price.Create(originalValue);

        // Act
        var result = price.ApplyPercentageChange(change);

        // Assert
        result.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void ApplyPercentageChange_NegativeResult_ClampsToZero()
    {
        // Arrange
        var price = Price.Create(100m);

        // Act
        var result = price.ApplyPercentageChange(-150m);

        // Assert
        result.Value.Should().Be(0m);
    }

    #endregion

    #region Round

    [Fact]
    public void Round_RoundsToSpecifiedDecimals()
    {
        // Arrange
        var price = Price.Create(100.5678m);

        // Act
        var result = price.Round(2);

        // Assert
        result.Value.Should().Be(100.57m);
    }

    #endregion

    #region Comparison Operators

    [Theory]
    [InlineData(100, 50, true, false)]
    [InlineData(50, 100, false, true)]
    [InlineData(100, 100, false, false)]
    public void ComparisonOperators_WorkCorrectly(decimal value1, decimal value2, bool expectedGreater, bool expectedLess)
    {
        // Arrange
        var price1 = Price.Create(value1);
        var price2 = Price.Create(value2);

        // Assert
        (price1 > price2).Should().Be(expectedGreater);
        (price1 < price2).Should().Be(expectedLess);
        (price1 >= price2).Should().Be(expectedGreater || value1 == value2);
        (price1 <= price2).Should().Be(expectedLess || value1 == value2);
    }

    [Fact]
    public void CompareTo_ReturnsCorrectResult()
    {
        // Arrange
        var price1 = Price.Create(100m);
        var price2 = Price.Create(50m);

        // Assert
        price1.CompareTo(price2).Should().BePositive();
        price2.CompareTo(price1).Should().BeNegative();
        price1.CompareTo(price1).Should().Be(0);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        // Arrange
        var price1 = Price.Create(100m);
        var price2 = Price.Create(100m);

        // Assert
        price1.Should().Be(price2);
    }

    [Fact]
    public void Equals_WithDifferentValue_ReturnsFalse()
    {
        // Arrange
        var price1 = Price.Create(100m);
        var price2 = Price.Create(50m);

        // Assert
        price1.Should().NotBe(price2);
    }

    #endregion

    #region Implicit Conversion

    [Fact]
    public void ImplicitConversion_ToDecimal_ReturnsValue()
    {
        // Arrange
        var price = Price.Create(100.50m);

        // Act
        decimal value = price;

        // Assert
        value.Should().Be(100.50m);
    }

    #endregion
}
