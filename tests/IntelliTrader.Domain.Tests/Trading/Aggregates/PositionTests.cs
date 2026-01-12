using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.Aggregates;

public class PositionTests
{
    private static TradingPair CreatePair() => TradingPair.Create("BTCUSDT", "USDT");
    private static OrderId CreateOrderId(string id = "order123") => OrderId.From(id);
    private static Price CreatePrice(decimal value) => Price.Create(value);
    private static Quantity CreateQuantity(decimal value) => Quantity.Create(value);
    private static Money CreateFees(decimal value) => Money.Create(value, "USDT");

    #region Open Position

    [Fact]
    public void Open_WithValidParameters_CreatesPosition()
    {
        // Arrange
        var pair = CreatePair();
        var orderId = CreateOrderId();
        var price = CreatePrice(50000m);
        var quantity = CreateQuantity(0.1m);
        var fees = CreateFees(5m);

        // Act
        var position = Position.Open(pair, orderId, price, quantity, fees, "buy_signal");

        // Assert
        position.Id.Should().NotBeNull();
        position.Pair.Should().Be(pair);
        position.Currency.Should().Be("USDT");
        position.SignalRule.Should().Be("buy_signal");
        position.DCALevel.Should().Be(0);
        position.IsClosed.Should().BeFalse();
        position.Entries.Should().HaveCount(1);
        position.TotalQuantity.Value.Should().Be(0.1m);
        position.TotalCost.Amount.Should().Be(5000m); // 50000 * 0.1
        position.TotalFees.Amount.Should().Be(5m);
        position.AveragePrice.Value.Should().Be(50000m);
    }

    [Fact]
    public void Open_RaisesPositionOpenedEvent()
    {
        // Act
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m),
            "buy_signal");

        // Assert
        position.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PositionOpened>();

        var evt = (PositionOpened)position.DomainEvents.Single();
        evt.PositionId.Should().Be(position.Id);
        evt.Pair.Should().Be(position.Pair);
        evt.Price.Value.Should().Be(50000m);
        evt.Quantity.Value.Should().Be(0.1m);
        evt.SignalRule.Should().Be("buy_signal");
    }

    [Fact]
    public void Open_WithZeroPrice_ThrowsArgumentException()
    {
        // Act
        var act = () => Position.Open(
            CreatePair(),
            CreateOrderId(),
            Price.Zero,
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("price");
    }

    [Fact]
    public void Open_WithZeroQuantity_ThrowsArgumentException()
    {
        // Act
        var act = () => Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            Quantity.Zero,
            CreateFees(5m));

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("quantity");
    }

    [Fact]
    public void Open_WithNullPair_ThrowsArgumentNullException()
    {
        // Act
        var act = () => Position.Open(
            null!,
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("pair");
    }

    #endregion

    #region DCA Entry

    [Fact]
    public void AddDCAEntry_WithValidParameters_AddsEntry()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Act
        position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(45000m),
            CreateQuantity(0.15m),
            CreateFees(6m));

        // Assert
        position.DCALevel.Should().Be(1);
        position.Entries.Should().HaveCount(2);
        position.TotalQuantity.Value.Should().Be(0.25m);
        position.TotalCost.Amount.Should().Be(11750m); // 5000 + 6750
        position.TotalFees.Amount.Should().Be(11m);
    }

    [Fact]
    public void AddDCAEntry_CalculatesCorrectAveragePrice()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m), // Cost: 5000
            CreateFees(5m));

        // Act
        position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(40000m),
            CreateQuantity(0.1m), // Cost: 4000
            CreateFees(4m));

        // Assert
        // Total cost: 9000, Total qty: 0.2, Average: 45000
        position.AveragePrice.Value.Should().Be(45000m);
    }

    [Fact]
    public void AddDCAEntry_RaisesDCAExecutedEvent()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));
        position.ClearDomainEvents();

        // Act
        position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(45000m),
            CreateQuantity(0.15m),
            CreateFees(6m));

        // Assert
        position.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<DCAExecuted>();

        var evt = (DCAExecuted)position.DomainEvents.Single();
        evt.NewDCALevel.Should().Be(1);
        evt.Price.Value.Should().Be(45000m);
        evt.Quantity.Value.Should().Be(0.15m);
    }

    [Fact]
    public void AddDCAEntry_OnClosedPosition_ThrowsInvalidOperationException()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        position.Close(CreateOrderId("sell1"), CreatePrice(55000m), CreateFees(5m));

        // Act
        var act = () => position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(45000m),
            CreateQuantity(0.15m),
            CreateFees(6m));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*closed*");
    }

    [Fact]
    public void AddDCAEntry_WithDifferentCurrency_ThrowsInvalidOperationException()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Act
        var act = () => position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(45000m),
            CreateQuantity(0.15m),
            Money.Create(6m, "BTC"));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*currency*");
    }

    [Fact]
    public void MultipleDCAEntries_CalculatesCorrectDCALevel()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Act - Add 3 DCA entries
        for (int i = 2; i <= 4; i++)
        {
            position.AddDCAEntry(
                CreateOrderId($"order{i}"),
                CreatePrice(45000m - (i * 1000)),
                CreateQuantity(0.1m),
                CreateFees(5m));
        }

        // Assert
        position.DCALevel.Should().Be(3);
        position.Entries.Should().HaveCount(4);
    }

    #endregion

    #region Close Position

    [Fact]
    public void Close_WithProfit_ClosesPositionCorrectly()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("buy1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m), // Cost: 5000
            CreateFees(5m));

        // Act
        position.Close(
            CreateOrderId("sell1"),
            CreatePrice(55000m), // Value: 5500
            CreateFees(5m));

        // Assert
        position.IsClosed.Should().BeTrue();
        position.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public void Close_RaisesPositionClosedEvent()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("buy1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));
        position.ClearDomainEvents();

        // Act
        position.Close(
            CreateOrderId("sell1"),
            CreatePrice(55000m),
            CreateFees(5m));

        // Assert
        position.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PositionClosed>();

        var evt = (PositionClosed)position.DomainEvents.Single();
        evt.SellPrice.Value.Should().Be(55000m);
        evt.SoldQuantity.Value.Should().Be(0.1m);
        evt.Proceeds.Amount.Should().Be(5500m);
        evt.DCALevel.Should().Be(0);
    }

    [Fact]
    public void Close_OnAlreadyClosedPosition_ThrowsInvalidOperationException()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("buy1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        position.Close(CreateOrderId("sell1"), CreatePrice(55000m), CreateFees(5m));

        // Act
        var act = () => position.Close(
            CreateOrderId("sell2"),
            CreatePrice(56000m),
            CreateFees(5m));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*closed*");
    }

    #endregion

    #region Calculate Margin

    [Theory]
    [InlineData(50000, 55000, 10.0)] // 10% profit (ignoring fees for simplicity)
    [InlineData(50000, 45000, -10.0)] // 10% loss
    [InlineData(50000, 50000, 0.0)] // Break-even
    public void CalculateMargin_ReturnsCorrectPercentage(
        decimal buyPrice,
        decimal currentPrice,
        decimal expectedMarginApprox)
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(buyPrice),
            CreateQuantity(0.1m),
            Money.Zero("USDT")); // Zero fees for this test

        // Act
        var margin = position.CalculateMargin(CreatePrice(currentPrice));

        // Assert
        margin.Percentage.Should().BeApproximately(expectedMarginApprox, 0.01m);
    }

    [Fact]
    public void CalculateMargin_WithFees_AccountsForFees()
    {
        // Arrange: Buy 0.1 BTC at 50000, cost = 5000, fees = 50
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(50m));

        // Act: Current price 55000, value = 5500
        // Cost basis with fees = 5050
        // Margin = (5500 - 5050) / 5050 * 100 = 8.91%
        var margin = position.CalculateMargin(CreatePrice(55000m));

        // Assert
        margin.Percentage.Should().BeApproximately(8.91m, 0.1m);
    }

    [Fact]
    public void CalculateMargin_WithEstimatedSellFees_AccountsForBothFees()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(50m));

        // Act: Include estimated sell fees
        var margin = position.CalculateMargin(
            CreatePrice(55000m),
            CreateFees(50m));

        // Cost basis = 5000 + 50 + 50 = 5100
        // Value = 5500
        // Margin = (5500 - 5100) / 5100 * 100 = 7.84%

        // Assert
        margin.Percentage.Should().BeApproximately(7.84m, 0.1m);
    }

    #endregion

    #region Calculate PnL

    [Fact]
    public void CalculateCurrentValue_ReturnsCorrectValue()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Act
        var currentValue = position.CalculateCurrentValue(CreatePrice(55000m));

        // Assert
        currentValue.Amount.Should().Be(5500m);
        currentValue.Currency.Should().Be("USDT");
    }

    [Fact]
    public void CalculateUnrealizedPnL_WithProfit_ReturnsPositiveValue()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m), // Cost: 5000
            CreateFees(50m));

        // Act: Current value = 5500, Cost basis = 5050
        var pnl = position.CalculateUnrealizedPnL(CreatePrice(55000m));

        // Assert
        pnl.Amount.Should().Be(450m); // 5500 - 5050
        pnl.IsPositive.Should().BeTrue();
    }

    [Fact]
    public void CalculateUnrealizedPnL_WithLoss_ReturnsNegativeValue()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m), // Cost: 5000
            CreateFees(50m));

        // Act: Current value = 4500, Cost basis = 5050
        var pnl = position.CalculateUnrealizedPnL(CreatePrice(45000m));

        // Assert
        pnl.Amount.Should().Be(-550m); // 4500 - 5050
        pnl.IsNegative.Should().BeTrue();
    }

    #endregion

    #region DCA Conditions

    [Theory]
    [InlineData(50000, 45000, 10, true)]  // 10% drop, requires 10% -> true
    [InlineData(50000, 47500, 10, false)] // 5% drop, requires 10% -> false
    [InlineData(50000, 45000, 15, false)] // 10% drop, requires 15% -> false
    [InlineData(50000, 40000, 15, true)]  // 20% drop, requires 15% -> true
    public void CanDCAByPriceDrop_ReturnsCorrectResult(
        decimal avgPrice,
        decimal currentPrice,
        decimal minDropPercent,
        bool expected)
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(avgPrice),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Act
        var canDCA = position.CanDCAByPriceDrop(CreatePrice(currentPrice), minDropPercent);

        // Assert
        canDCA.Should().Be(expected);
    }

    [Fact]
    public void CanDCAByPriceDrop_OnClosedPosition_ReturnsFalse()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));
        position.Close(CreateOrderId("sell"), CreatePrice(55000m), CreateFees(5m));

        // Act
        var canDCA = position.CanDCAByPriceDrop(CreatePrice(40000m), 10);

        // Assert
        canDCA.Should().BeFalse();
    }

    [Fact]
    public void MeetsMinimumAgeForDCA_WithSufficientAge_ReturnsTrue()
    {
        // Arrange
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m),
            timestamp: oldTimestamp);

        // Act
        var meetsAge = position.MeetsMinimumAgeForDCA(TimeSpan.FromMinutes(3));

        // Assert
        meetsAge.Should().BeTrue();
    }

    [Fact]
    public void MeetsMinimumAgeForDCA_WithInsufficientAge_ReturnsFalse()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        // Act
        var meetsAge = position.MeetsMinimumAgeForDCA(TimeSpan.FromMinutes(5));

        // Assert
        meetsAge.Should().BeFalse();
    }

    #endregion

    #region Entry Access

    [Fact]
    public void GetEntryAtLevel_ReturnsCorrectEntry()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(45000m),
            CreateQuantity(0.15m),
            CreateFees(6m));

        // Act
        var entry0 = position.GetEntryAtLevel(0);
        var entry1 = position.GetEntryAtLevel(1);
        var entry2 = position.GetEntryAtLevel(2);

        // Assert
        entry0.Should().NotBeNull();
        entry0!.OrderId.Value.Should().Be("order1");

        entry1.Should().NotBeNull();
        entry1!.OrderId.Value.Should().Be("order2");

        entry2.Should().BeNull();
    }

    [Fact]
    public void GetLastBuyMargin_ReturnsMarginForLastEntry()
    {
        // Arrange
        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m));

        position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(40000m),
            CreateQuantity(0.1m),
            CreateFees(4m));

        // Act: Current price 44000
        // Last entry bought at 40000
        // Margin = (44000 - 40000) / (40000 + fees) * 100
        var lastBuyMargin = position.GetLastBuyMargin(CreatePrice(44000m));

        // Assert
        lastBuyMargin.Should().NotBeNull();
        lastBuyMargin!.IsProfit.Should().BeTrue();
    }

    #endregion

    #region Age Tracking

    [Fact]
    public void Age_ReturnsCorrectDuration()
    {
        // Arrange
        var openTime = DateTimeOffset.UtcNow.AddHours(-2);
        var position = Position.Open(
            CreatePair(),
            CreateOrderId(),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m),
            timestamp: openTime);

        // Act
        var age = position.Age;

        // Assert
        age.TotalHours.Should().BeApproximately(2, 0.1);
    }

    [Fact]
    public void TimeSinceLastBuy_AfterDCA_ReflectsLastBuyTime()
    {
        // Arrange
        var openTime = DateTimeOffset.UtcNow.AddHours(-2);
        var dcaTime = DateTimeOffset.UtcNow.AddMinutes(-30);

        var position = Position.Open(
            CreatePair(),
            CreateOrderId("order1"),
            CreatePrice(50000m),
            CreateQuantity(0.1m),
            CreateFees(5m),
            timestamp: openTime);

        position.AddDCAEntry(
            CreateOrderId("order2"),
            CreatePrice(45000m),
            CreateQuantity(0.15m),
            CreateFees(6m),
            timestamp: dcaTime);

        // Act
        var timeSinceLastBuy = position.TimeSinceLastBuy;

        // Assert
        timeSinceLastBuy.TotalMinutes.Should().BeApproximately(30, 1);
    }

    #endregion
}
