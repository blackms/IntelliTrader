using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.ValueObjects;

public class MoneyTests
{
    #region Create

    [Theory]
    [InlineData(100.50, "USDT")]
    [InlineData(0, "BTC")]
    [InlineData(-50.25, "ETH")]
    public void Create_WithValidAmountAndCurrency_CreatesMoney(decimal amount, string currency)
    {
        // Act
        var money = Money.Create(amount, currency);

        // Assert
        money.Amount.Should().Be(amount);
        money.Currency.Should().Be(currency.ToUpperInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_WithInvalidCurrency_ThrowsArgumentException(string? currency)
    {
        // Act
        var act = () => Money.Create(100m, currency!);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("currency");
    }

    [Fact]
    public void Zero_CreatesZeroMoney()
    {
        // Act
        var money = Money.Zero("USDT");

        // Assert
        money.Amount.Should().Be(0m);
        money.Currency.Should().Be("USDT");
        money.IsZero.Should().BeTrue();
    }

    #endregion

    #region Properties

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(100, false, true, false)]
    [InlineData(-50, false, false, true)]
    public void Properties_ReturnCorrectValues(decimal amount, bool isZero, bool isPositive, bool isNegative)
    {
        // Arrange
        var money = Money.Create(amount, "USDT");

        // Assert
        money.IsZero.Should().Be(isZero);
        money.IsPositive.Should().Be(isPositive);
        money.IsNegative.Should().Be(isNegative);
    }

    #endregion

    #region Operations

    [Fact]
    public void Abs_ReturnsAbsoluteValue()
    {
        // Arrange
        var money = Money.Create(-100m, "USDT");

        // Act
        var result = money.Abs();

        // Assert
        result.Amount.Should().Be(100m);
        result.Currency.Should().Be("USDT");
    }

    [Fact]
    public void Negate_ReturnsNegatedValue()
    {
        // Arrange
        var money = Money.Create(100m, "USDT");

        // Act
        var result = money.Negate();

        // Assert
        result.Amount.Should().Be(-100m);
    }

    [Fact]
    public void Round_RoundsToSpecifiedDecimals()
    {
        // Arrange
        var money = Money.Create(100.5678m, "USDT");

        // Act
        var result = money.Round(2);

        // Assert
        result.Amount.Should().Be(100.57m);
    }

    #endregion

    #region Arithmetic Operators

    [Fact]
    public void Addition_WithSameCurrency_ReturnsSum()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(50m, "USDT");

        // Act
        var result = money1 + money2;

        // Assert
        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("USDT");
    }

    [Fact]
    public void Addition_WithDifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(50m, "BTC");

        // Act
        var act = () => money1 + money2;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Subtraction_WithSameCurrency_ReturnsDifference()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(30m, "USDT");

        // Act
        var result = money1 - money2;

        // Assert
        result.Amount.Should().Be(70m);
    }

    [Fact]
    public void Multiplication_ReturnsProduct()
    {
        // Arrange
        var money = Money.Create(100m, "USDT");

        // Act
        var result1 = money * 2.5m;
        var result2 = 2.5m * money;

        // Assert
        result1.Amount.Should().Be(250m);
        result2.Amount.Should().Be(250m);
    }

    [Fact]
    public void Division_ReturnsQuotient()
    {
        // Arrange
        var money = Money.Create(100m, "USDT");

        // Act
        var result = money / 4m;

        // Assert
        result.Amount.Should().Be(25m);
    }

    [Fact]
    public void Division_ByZero_ThrowsDivideByZeroException()
    {
        // Arrange
        var money = Money.Create(100m, "USDT");

        // Act
        var act = () => money / 0m;

        // Assert
        act.Should().Throw<DivideByZeroException>();
    }

    #endregion

    #region Comparison Operators

    [Theory]
    [InlineData(100, 50, true, false)]
    [InlineData(50, 100, false, true)]
    [InlineData(100, 100, false, false)]
    public void ComparisonOperators_WorkCorrectly(decimal amount1, decimal amount2, bool expectedGreater, bool expectedLess)
    {
        // Arrange
        var money1 = Money.Create(amount1, "USDT");
        var money2 = Money.Create(amount2, "USDT");

        // Assert
        (money1 > money2).Should().Be(expectedGreater);
        (money1 < money2).Should().Be(expectedLess);
        (money1 >= money2).Should().Be(expectedGreater || amount1 == amount2);
        (money1 <= money2).Should().Be(expectedLess || amount1 == amount2);
    }

    [Fact]
    public void CompareTo_WithSameCurrency_ReturnsCorrectResult()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(50m, "USDT");

        // Assert
        money1.CompareTo(money2).Should().BePositive();
        money2.CompareTo(money1).Should().BeNegative();
        money1.CompareTo(money1).Should().Be(0);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSameAmountAndCurrency_ReturnsTrue()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(100m, "USDT");

        // Assert
        money1.Should().Be(money2);
    }

    [Fact]
    public void Equals_WithDifferentAmount_ReturnsFalse()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(50m, "USDT");

        // Assert
        money1.Should().NotBe(money2);
    }

    [Fact]
    public void Equals_WithDifferentCurrency_ReturnsFalse()
    {
        // Arrange
        var money1 = Money.Create(100m, "USDT");
        var money2 = Money.Create(100m, "BTC");

        // Assert
        money1.Should().NotBe(money2);
    }

    #endregion

    #region ToString

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var money = Money.Create(100.50m, "USDT");

        // Act
        var result = money.ToString();

        // Assert - check contains amount and currency (culture-independent)
        result.Should().Contain("100");
        result.Should().Contain("50");
        result.Should().Contain("USDT");
    }

    #endregion
}
