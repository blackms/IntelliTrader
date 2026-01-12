using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.ValueObjects;

public class QuantityTests
{
    #region Create

    [Theory]
    [InlineData(0)]
    [InlineData(100.50)]
    [InlineData(0.00000001)]
    public void Create_WithValidValue_CreatesQuantity(decimal value)
    {
        // Act
        var quantity = Quantity.Create(value);

        // Assert
        quantity.Value.Should().Be(value);
    }

    [Fact]
    public void Create_WithNegativeValue_ThrowsArgumentException()
    {
        // Act
        var act = () => Quantity.Create(-100m);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Zero_CreatesZeroQuantity()
    {
        // Act
        var quantity = Quantity.Zero;

        // Assert
        quantity.Value.Should().Be(0m);
        quantity.IsZero.Should().BeTrue();
    }

    #endregion

    #region Round and Truncate

    [Fact]
    public void Round_RoundsToSpecifiedDecimals()
    {
        // Arrange
        var quantity = Quantity.Create(100.5678m);

        // Act
        var result = quantity.Round(2);

        // Assert
        result.Value.Should().Be(100.57m);
    }

    [Theory]
    [InlineData(100.5678, 2, 100.56)]
    [InlineData(100.9999, 2, 100.99)]
    [InlineData(0.123456789, 8, 0.12345678)]
    public void Truncate_TruncatesToSpecifiedDecimals(decimal value, int decimals, decimal expected)
    {
        // Arrange
        var quantity = Quantity.Create(value);

        // Act
        var result = quantity.Truncate(decimals);

        // Assert
        result.Value.Should().Be(expected);
    }

    #endregion

    #region Arithmetic Operators

    [Fact]
    public void Addition_ReturnsSumOfQuantities()
    {
        // Arrange
        var q1 = Quantity.Create(100m);
        var q2 = Quantity.Create(50m);

        // Act
        var result = q1 + q2;

        // Assert
        result.Value.Should().Be(150m);
    }

    [Fact]
    public void Subtraction_ReturnsDifferenceOfQuantities()
    {
        // Arrange
        var q1 = Quantity.Create(100m);
        var q2 = Quantity.Create(30m);

        // Act
        var result = q1 - q2;

        // Assert
        result.Value.Should().Be(70m);
    }

    [Fact]
    public void Subtraction_WouldResultInNegative_ThrowsInvalidOperationException()
    {
        // Arrange
        var q1 = Quantity.Create(30m);
        var q2 = Quantity.Create(100m);

        // Act
        var act = () => q1 - q2;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Multiplication_ReturnsProduct()
    {
        // Arrange
        var quantity = Quantity.Create(100m);

        // Act
        var result = quantity * 2.5m;

        // Assert
        result.Value.Should().Be(250m);
    }

    [Fact]
    public void Multiplication_ByNegative_ThrowsArgumentException()
    {
        // Arrange
        var quantity = Quantity.Create(100m);

        // Act
        var act = () => quantity * -1m;

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Division_ReturnsQuotient()
    {
        // Arrange
        var quantity = Quantity.Create(100m);

        // Act
        var result = quantity / 4m;

        // Assert
        result.Value.Should().Be(25m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Division_ByZeroOrNegative_ThrowsArgumentException(decimal divisor)
    {
        // Arrange
        var quantity = Quantity.Create(100m);

        // Act
        var act = () => quantity / divisor;

        // Assert
        act.Should().Throw<ArgumentException>();
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
        var q1 = Quantity.Create(value1);
        var q2 = Quantity.Create(value2);

        // Assert
        (q1 > q2).Should().Be(expectedGreater);
        (q1 < q2).Should().Be(expectedLess);
        (q1 >= q2).Should().Be(expectedGreater || value1 == value2);
        (q1 <= q2).Should().Be(expectedLess || value1 == value2);
    }

    [Fact]
    public void CompareTo_ReturnsCorrectResult()
    {
        // Arrange
        var q1 = Quantity.Create(100m);
        var q2 = Quantity.Create(50m);

        // Assert
        q1.CompareTo(q2).Should().BePositive();
        q2.CompareTo(q1).Should().BeNegative();
        q1.CompareTo(q1).Should().Be(0);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        // Arrange
        var q1 = Quantity.Create(100m);
        var q2 = Quantity.Create(100m);

        // Assert
        q1.Should().Be(q2);
    }

    [Fact]
    public void Equals_WithDifferentValue_ReturnsFalse()
    {
        // Arrange
        var q1 = Quantity.Create(100m);
        var q2 = Quantity.Create(50m);

        // Assert
        q1.Should().NotBe(q2);
    }

    #endregion

    #region Implicit Conversion

    [Fact]
    public void ImplicitConversion_ToDecimal_ReturnsValue()
    {
        // Arrange
        var quantity = Quantity.Create(100.50m);

        // Act
        decimal value = quantity;

        // Assert
        value.Should().Be(100.50m);
    }

    #endregion
}
