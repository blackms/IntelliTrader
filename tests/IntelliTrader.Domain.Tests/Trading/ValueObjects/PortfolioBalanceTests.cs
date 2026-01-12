using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.ValueObjects;

public class PortfolioBalanceTests
{
    #region Create

    [Fact]
    public void Create_WithValidParameters_CreatesBalance()
    {
        // Act
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Assert
        balance.Currency.Should().Be("USDT");
        balance.Total.Amount.Should().Be(10000m);
        balance.Available.Amount.Should().Be(10000m);
        balance.Reserved.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_WithZeroBalance_CreatesZeroBalance()
    {
        // Act
        var balance = PortfolioBalance.Create(0m, "USDT");

        // Assert
        balance.Total.Amount.Should().Be(0m);
        balance.Available.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_WithNegativeAmount_ThrowsArgumentException()
    {
        // Act
        var act = () => PortfolioBalance.Create(-100m, "USDT");

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("totalAmount");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_WithInvalidCurrency_ThrowsArgumentException(string? currency)
    {
        // Act
        var act = () => PortfolioBalance.Create(10000m, currency!);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("currency");
    }

    [Fact]
    public void Zero_CreatesZeroBalance()
    {
        // Act
        var balance = PortfolioBalance.Zero("USDT");

        // Assert
        balance.Total.Amount.Should().Be(0m);
        balance.Available.Amount.Should().Be(0m);
        balance.Reserved.Amount.Should().Be(0m);
    }

    #endregion

    #region CanAfford

    [Theory]
    [InlineData(10000, 5000, true)]
    [InlineData(10000, 10000, true)]
    [InlineData(10000, 10001, false)]
    public void CanAfford_ReturnsCorrectResult(decimal total, decimal amount, bool expected)
    {
        // Arrange
        var balance = PortfolioBalance.Create(total, "USDT");

        // Act
        var result = balance.CanAfford(Money.Create(amount, "USDT"));

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void CanAfford_WithDifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var act = () => balance.CanAfford(Money.Create(100m, "BTC"));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Currency mismatch*");
    }

    #endregion

    #region Reserve

    [Fact]
    public void Reserve_WithSufficientFunds_ReservesCorrectly()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var newBalance = balance.Reserve(Money.Create(3000m, "USDT"));

        // Assert
        newBalance.Total.Amount.Should().Be(10000m);
        newBalance.Available.Amount.Should().Be(7000m);
        newBalance.Reserved.Amount.Should().Be(3000m);
    }

    [Fact]
    public void Reserve_WithInsufficientFunds_ThrowsInvalidOperationException()
    {
        // Arrange
        var balance = PortfolioBalance.Create(1000m, "USDT");

        // Act
        var act = () => balance.Reserve(Money.Create(2000m, "USDT"));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Insufficient funds*");
    }

    [Fact]
    public void Reserve_WithDifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var act = () => balance.Reserve(Money.Create(100m, "BTC"));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Currency mismatch*");
    }

    [Fact]
    public void Reserve_MultipleReservations_AccumulatesReserved()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var balance1 = balance.Reserve(Money.Create(2000m, "USDT"));
        var balance2 = balance1.Reserve(Money.Create(3000m, "USDT"));

        // Assert
        balance2.Available.Amount.Should().Be(5000m);
        balance2.Reserved.Amount.Should().Be(5000m);
    }

    #endregion

    #region Release

    [Fact]
    public void Release_WithSufficientReserved_ReleasesCorrectly()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT")
            .Reserve(Money.Create(3000m, "USDT"));

        // Act
        var newBalance = balance.Release(Money.Create(3000m, "USDT"));

        // Assert
        newBalance.Total.Amount.Should().Be(10000m);
        newBalance.Available.Amount.Should().Be(10000m);
        newBalance.Reserved.Amount.Should().Be(0m);
    }

    [Fact]
    public void Release_PartialRelease_ReleasesCorrectAmount()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT")
            .Reserve(Money.Create(3000m, "USDT"));

        // Act
        var newBalance = balance.Release(Money.Create(1000m, "USDT"));

        // Assert
        newBalance.Available.Amount.Should().Be(8000m);
        newBalance.Reserved.Amount.Should().Be(2000m);
    }

    [Fact]
    public void Release_MoreThanReserved_ThrowsInvalidOperationException()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT")
            .Reserve(Money.Create(1000m, "USDT"));

        // Act
        var act = () => balance.Release(Money.Create(2000m, "USDT"));

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*release more than reserved*");
    }

    #endregion

    #region UpdateTotal

    [Fact]
    public void UpdateTotal_WhenIncreasing_IncreasesAvailable()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var newBalance = balance.UpdateTotal(12000m);

        // Assert
        newBalance.Total.Amount.Should().Be(12000m);
        newBalance.Available.Amount.Should().Be(12000m);
    }

    [Fact]
    public void UpdateTotal_WhenDecreasing_DecreasesAvailable()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var newBalance = balance.UpdateTotal(8000m);

        // Assert
        newBalance.Total.Amount.Should().Be(8000m);
        newBalance.Available.Amount.Should().Be(8000m);
    }

    [Fact]
    public void UpdateTotal_WithReserved_MaintainsReserved()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT")
            .Reserve(Money.Create(3000m, "USDT"));

        // Act
        var newBalance = balance.UpdateTotal(12000m);

        // Assert
        newBalance.Total.Amount.Should().Be(12000m);
        newBalance.Available.Amount.Should().Be(9000m); // 12000 - 3000 reserved
        newBalance.Reserved.Amount.Should().Be(3000m);
    }

    [Fact]
    public void UpdateTotal_WhenNegative_ThrowsArgumentException()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var act = () => balance.UpdateTotal(-100m);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region RecordPnL

    [Fact]
    public void RecordPnL_WithProfit_IncreasesTotalAndAvailable()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var newBalance = balance.RecordPnL(Money.Create(500m, "USDT"));

        // Assert
        newBalance.Total.Amount.Should().Be(10500m);
        newBalance.Available.Amount.Should().Be(10500m);
    }

    [Fact]
    public void RecordPnL_WithLoss_DecreasesTotalAndAvailable()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT");

        // Act
        var newBalance = balance.RecordPnL(Money.Create(-500m, "USDT"));

        // Assert
        newBalance.Total.Amount.Should().Be(9500m);
        newBalance.Available.Amount.Should().Be(9500m);
    }

    #endregion

    #region Percentages

    [Fact]
    public void ReservedPercentage_CalculatesCorrectly()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT")
            .Reserve(Money.Create(2500m, "USDT"));

        // Act
        var percentage = balance.ReservedPercentage;

        // Assert
        percentage.Should().Be(25m);
    }

    [Fact]
    public void AvailablePercentage_CalculatesCorrectly()
    {
        // Arrange
        var balance = PortfolioBalance.Create(10000m, "USDT")
            .Reserve(Money.Create(2500m, "USDT"));

        // Act
        var percentage = balance.AvailablePercentage;

        // Assert
        percentage.Should().Be(75m);
    }

    [Fact]
    public void Percentages_WithZeroBalance_ReturnsZero()
    {
        // Arrange
        var balance = PortfolioBalance.Zero("USDT");

        // Assert
        balance.ReservedPercentage.Should().Be(0m);
        balance.AvailablePercentage.Should().Be(0m);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var balance1 = PortfolioBalance.Create(10000m, "USDT");
        var balance2 = PortfolioBalance.Create(10000m, "USDT");

        // Assert
        balance1.Should().Be(balance2);
    }

    [Fact]
    public void Equals_WithDifferentValues_ReturnsFalse()
    {
        // Arrange
        var balance1 = PortfolioBalance.Create(10000m, "USDT");
        var balance2 = PortfolioBalance.Create(5000m, "USDT");

        // Assert
        balance1.Should().NotBe(balance2);
    }

    #endregion
}
