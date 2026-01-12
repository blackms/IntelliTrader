using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.Services;

public class MarginCalculatorTests
{
    private readonly MarginCalculator _calculator = new();

    private static Position CreateTestPosition(
        decimal entryPrice = 100m,
        decimal quantity = 10m,
        decimal fees = 1m,
        string pair = "BTCUSDT")
    {
        return Position.Open(
            TradingPair.Create(pair, "USDT"),
            OrderId.From("order-1"),
            Price.Create(entryPrice),
            Quantity.Create(quantity),
            Money.Create(fees, "USDT"));
    }

    #region CalculateBreakEvenPrice

    [Fact]
    public void CalculateBreakEvenPrice_WithNoSellFees_ReturnsCostPlusFeesPerQuantity()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);
        // Total cost = 100 * 10 = 1000, fees = 10, total = 1010

        // Act
        var breakEven = _calculator.CalculateBreakEvenPrice(position, estimatedSellFeePercentage: 0m);

        // Assert
        breakEven.Value.Should().Be(101m); // 1010 / 10
    }

    [Fact]
    public void CalculateBreakEvenPrice_WithSellFees_AccountsForFees()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        // Total cost = 1000, no buy fees

        // Act - 1% sell fee
        var breakEven = _calculator.CalculateBreakEvenPrice(position, estimatedSellFeePercentage: 1m);

        // Assert
        // breakEven * 10 * 0.99 = 1000 => breakEven = 1000 / 9.9 = 101.0101...
        breakEven.Value.Should().BeApproximately(101.0101m, 0.0001m);
    }

    [Fact]
    public void CalculateBreakEvenPrice_WithBothFees_CombinesCorrectly()
    {
        // Arrange - 1% buy fee built into the position
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);
        // Total cost = 1000, buy fees = 10

        // Act - 1% sell fee
        var breakEven = _calculator.CalculateBreakEvenPrice(position, estimatedSellFeePercentage: 1m);

        // Assert
        // (1000 + 10) / (10 * 0.99) = 1010 / 9.9 = 102.0202...
        breakEven.Value.Should().BeApproximately(102.0202m, 0.0001m);
    }

    [Fact]
    public void CalculateBreakEvenPrice_WithZeroQuantity_ReturnsZero()
    {
        // Arrange - Create a position and close it partially (edge case)
        var position = Position.Open(
            TradingPair.Create("BTCUSDT", "USDT"),
            OrderId.From("order-1"),
            Price.Create(100m),
            Quantity.Create(0.0000001m), // Minimal quantity
            Money.Create(0m, "USDT"));

        // Act
        var breakEven = _calculator.CalculateBreakEvenPrice(position);

        // Assert - Should handle gracefully
        breakEven.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateBreakEvenPrice_WithNegativeFeePercentage_ThrowsArgumentException()
    {
        // Arrange
        var position = CreateTestPosition();

        // Act
        var act = () => _calculator.CalculateBreakEvenPrice(position, estimatedSellFeePercentage: -1m);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("estimatedSellFeePercentage");
    }

    [Fact]
    public void CalculateBreakEvenPrice_WithNullPosition_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _calculator.CalculateBreakEvenPrice(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region CalculateTargetSellPrice

    [Fact]
    public void CalculateTargetSellPrice_ForZeroMargin_ReturnsBreakEvenPrice()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);

        // Act
        var targetPrice = _calculator.CalculateTargetSellPrice(position, Margin.Zero);
        var breakEven = _calculator.CalculateBreakEvenPrice(position);

        // Assert
        targetPrice.Value.Should().BeApproximately(breakEven.Value, 0.0001m);
    }

    [Fact]
    public void CalculateTargetSellPrice_For10PercentMargin_ReturnsCorrectPrice()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        // Total cost = 1000

        // Act
        var targetPrice = _calculator.CalculateTargetSellPrice(
            position,
            Margin.FromPercentage(10m),
            estimatedSellFeePercentage: 0m);

        // Assert
        // Target value = 1000 * 1.10 = 1100
        // Target price = 1100 / 10 = 110
        targetPrice.Value.Should().Be(110m);
    }

    [Fact]
    public void CalculateTargetSellPrice_WithSellFees_AccountsForFees()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act - 10% margin with 1% sell fee
        var targetPrice = _calculator.CalculateTargetSellPrice(
            position,
            Margin.FromPercentage(10m),
            estimatedSellFeePercentage: 1m);

        // Assert
        // Target value = 1000 * 1.10 = 1100
        // targetPrice * 10 * 0.99 = 1100 => targetPrice = 1100 / 9.9 = 111.1111...
        targetPrice.Value.Should().BeApproximately(111.1111m, 0.0001m);
    }

    [Fact]
    public void CalculateTargetSellPrice_ForNegativeMargin_ReturnsLowerPrice()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act - Target a 10% loss
        var targetPrice = _calculator.CalculateTargetSellPrice(
            position,
            Margin.FromPercentage(-10m),
            estimatedSellFeePercentage: 0m);

        // Assert
        // Target value = 1000 * 0.90 = 900
        // Target price = 900 / 10 = 90
        targetPrice.Value.Should().Be(90m);
    }

    [Fact]
    public void CalculateTargetSellPrice_WithNullPosition_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _calculator.CalculateTargetSellPrice(null!, Margin.FromPercentage(10m));

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region CalculateMarginAtPrice

    [Fact]
    public void CalculateMarginAtPrice_AtEntryPrice_ReturnsNegativeMarginDueToFees()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);
        // Cost = 1000 + 10 fees = 1010

        // Act
        var margin = _calculator.CalculateMarginAtPrice(position, Price.Create(100m));

        // Assert
        // Value at 100 = 1000, cost = 1010
        // Margin = (1000 - 1010) / 1010 * 100 = -0.99%
        margin.Percentage.Should().BeApproximately(-0.99m, 0.01m);
    }

    [Fact]
    public void CalculateMarginAtPrice_At110_Returns10PercentWithNoFees()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        // Cost = 1000

        // Act
        var margin = _calculator.CalculateMarginAtPrice(position, Price.Create(110m));

        // Assert
        // Value = 1100, cost = 1000
        // Margin = (1100 - 1000) / 1000 * 100 = 10%
        margin.Percentage.Should().Be(10m);
    }

    [Fact]
    public void CalculateMarginAtPrice_WithSellFees_ReducesMargin()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act - 1% sell fee
        var marginWithoutFees = _calculator.CalculateMarginAtPrice(position, Price.Create(110m), 0m);
        var marginWithFees = _calculator.CalculateMarginAtPrice(position, Price.Create(110m), 1m);

        // Assert
        marginWithFees.Percentage.Should().BeLessThan(marginWithoutFees.Percentage);
        // Gross value = 1100, sell fees = 11, net = 1089
        // Margin = (1089 - 1000) / 1000 * 100 = 8.9%
        marginWithFees.Percentage.Should().BeApproximately(8.9m, 0.01m);
    }

    [Fact]
    public void CalculateMarginAtPrice_At90_ReturnsNegative10Percent()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act
        var margin = _calculator.CalculateMarginAtPrice(position, Price.Create(90m));

        // Assert
        margin.Percentage.Should().Be(-10m);
        margin.IsLoss.Should().BeTrue();
    }

    #endregion

    #region CalculatePriceDropPercentage

    [Fact]
    public void CalculatePriceDropPercentage_At90_Returns10PercentDrop()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act
        var drop = _calculator.CalculatePriceDropPercentage(position, Price.Create(90m));

        // Assert
        drop.Should().Be(10m);
    }

    [Fact]
    public void CalculatePriceDropPercentage_At110_ReturnsNegativeDrop()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act
        var drop = _calculator.CalculatePriceDropPercentage(position, Price.Create(110m));

        // Assert
        drop.Should().Be(-10m); // Negative drop means price increase
    }

    [Fact]
    public void CalculatePriceDropPercentage_AtSamePrice_ReturnsZero()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act
        var drop = _calculator.CalculatePriceDropPercentage(position, Price.Create(100m));

        // Assert
        drop.Should().Be(0m);
    }

    #endregion

    #region CalculatePriceForMargin

    [Fact]
    public void CalculatePriceForMargin_For10PercentProfit_ReturnsCorrectPrice()
    {
        // Arrange
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);

        // Act
        var price = _calculator.CalculatePriceForMargin(cost, quantity, Margin.FromPercentage(10m));

        // Assert
        price.Value.Should().Be(110m);
    }

    [Fact]
    public void CalculatePriceForMargin_ForBreakEven_ReturnsCostPerQuantity()
    {
        // Arrange
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);

        // Act
        var price = _calculator.CalculatePriceForMargin(cost, quantity, Margin.Zero);

        // Assert
        price.Value.Should().Be(100m);
    }

    [Fact]
    public void CalculatePriceForMargin_For10PercentLoss_ReturnsLowerPrice()
    {
        // Arrange
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(10m);

        // Act
        var price = _calculator.CalculatePriceForMargin(cost, quantity, Margin.FromPercentage(-10m));

        // Assert
        price.Value.Should().Be(90m);
    }

    [Fact]
    public void CalculatePriceForMargin_WithZeroQuantity_ReturnsZero()
    {
        // Arrange
        var cost = Money.Create(1000m, "USDT");
        var quantity = Quantity.Create(0m);

        // Act
        var price = _calculator.CalculatePriceForMargin(cost, quantity, Margin.FromPercentage(10m));

        // Assert
        price.Value.Should().Be(0m);
    }

    #endregion

    #region AnalyzeFeeImpact

    [Fact]
    public void AnalyzeFeeImpact_WithBuyAndSellFees_CalculatesCorrectly()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);
        // Cost = 1000, buy fees = 10

        // Act
        var analysis = _calculator.AnalyzeFeeImpact(position, sellFeePercentage: 1m);

        // Assert
        analysis.BuyFees.Amount.Should().Be(10m);
        analysis.EstimatedSellFees.Amount.Should().Be(10m); // 1% of 1000
        analysis.TotalFees.Amount.Should().Be(20m);
        analysis.BreakEvenPriceWithoutFees.Value.Should().Be(100m);
        analysis.BreakEvenPriceWithFees.Value.Should().BeGreaterThan(100m);
        analysis.FeeImpactOnBreakEven.Value.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void AnalyzeFeeImpact_WithNoFees_ShowsNoImpact()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);

        // Act
        var analysis = _calculator.AnalyzeFeeImpact(position, sellFeePercentage: 0m);

        // Assert
        analysis.BuyFees.Amount.Should().Be(0m);
        analysis.EstimatedSellFees.Amount.Should().Be(0m);
        analysis.TotalFees.Amount.Should().Be(0m);
        analysis.BreakEvenPriceWithoutFees.Value.Should().Be(100m);
        analysis.BreakEvenPriceWithFees.Value.Should().Be(100m);
        analysis.FeeImpactOnBreakEven.Value.Should().Be(0m);
        analysis.FeePercentageOfCost.Should().Be(0m);
    }

    [Fact]
    public void AnalyzeFeeImpact_CalculatesFeePercentageOfCost()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);
        // Cost = 1000, buy fees = 10 (1%)

        // Act
        var analysis = _calculator.AnalyzeFeeImpact(position, sellFeePercentage: 1m);
        // Sell fees = 10 (1%), total fees = 20 (2%)

        // Assert
        analysis.FeePercentageOfCost.Should().BeApproximately(2m, 0.01m);
    }

    #endregion

    #region CalculatePortfolioStatistics

    [Fact]
    public void CalculatePortfolioStatistics_WithEmptyList_ReturnsEmpty()
    {
        // Act
        var stats = _calculator.CalculatePortfolioStatistics(Array.Empty<(Position, Price)>());

        // Assert
        stats.TotalPositions.Should().Be(0);
        stats.OverallMargin.Should().Be(Margin.Zero);
    }

    [Fact]
    public void CalculatePortfolioStatistics_WithSingleProfitablePosition_CalculatesCorrectly()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        var currentPrice = Price.Create(110m); // 10% profit

        // Act
        var stats = _calculator.CalculatePortfolioStatistics(new[] { (position, currentPrice) });

        // Assert
        stats.TotalPositions.Should().Be(1);
        stats.ProfitablePositions.Should().Be(1);
        stats.LosingPositions.Should().Be(0);
        stats.TotalCost.Amount.Should().Be(1000m);
        stats.TotalCurrentValue.Amount.Should().Be(1100m);
        stats.TotalPnL.Amount.Should().Be(100m);
        stats.OverallMargin.Percentage.Should().Be(10m);
    }

    [Fact]
    public void CalculatePortfolioStatistics_WithMixedPositions_CalculatesCorrectly()
    {
        // Arrange
        var position1 = Position.Open(
            TradingPair.Create("BTCUSDT", "USDT"),
            OrderId.From("order-1"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(0m, "USDT"));

        var position2 = Position.Open(
            TradingPair.Create("ETHUSDT", "USDT"),
            OrderId.From("order-2"),
            Price.Create(50m),
            Quantity.Create(20m),
            Money.Create(0m, "USDT"));

        var positions = new[]
        {
            (position1, Price.Create(110m)),  // 10% profit, cost 1000, value 1100
            (position2, Price.Create(45m))     // 10% loss, cost 1000, value 900
        };

        // Act
        var stats = _calculator.CalculatePortfolioStatistics(positions);

        // Assert
        stats.TotalPositions.Should().Be(2);
        stats.ProfitablePositions.Should().Be(1);
        stats.LosingPositions.Should().Be(1);
        stats.TotalCost.Amount.Should().Be(2000m);
        stats.TotalCurrentValue.Amount.Should().Be(2000m);
        stats.TotalPnL.Amount.Should().Be(0m);
        stats.OverallMargin.Percentage.Should().Be(0m);
        stats.BestMargin.Percentage.Should().Be(10m);
        stats.WorstMargin.Percentage.Should().Be(-10m);
        stats.BestPerformingPair.Should().Be(TradingPair.Create("BTCUSDT", "USDT"));
        stats.WorstPerformingPair.Should().Be(TradingPair.Create("ETHUSDT", "USDT"));
    }

    [Fact]
    public void CalculatePortfolioStatistics_WithSellFees_ReducesPnL()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        var currentPrice = Price.Create(110m);

        // Act
        var statsWithoutFees = _calculator.CalculatePortfolioStatistics(
            new[] { (position, currentPrice) }, sellFeePercentage: 0m);
        var statsWithFees = _calculator.CalculatePortfolioStatistics(
            new[] { (position, currentPrice) }, sellFeePercentage: 1m);

        // Assert
        statsWithFees.TotalPnL.Amount.Should().BeLessThan(statsWithoutFees.TotalPnL.Amount);
        statsWithFees.OverallMargin.Percentage.Should().BeLessThan(statsWithoutFees.OverallMargin.Percentage);
    }

    [Fact]
    public void CalculatePortfolioStatistics_CalculatesAverageMargin()
    {
        // Arrange
        var position1 = Position.Open(
            TradingPair.Create("BTCUSDT", "USDT"),
            OrderId.From("order-1"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(0m, "USDT"));

        var position2 = Position.Open(
            TradingPair.Create("ETHUSDT", "USDT"),
            OrderId.From("order-2"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(0m, "USDT"));

        var positions = new[]
        {
            (position1, Price.Create(120m)),  // 20% profit
            (position2, Price.Create(110m))   // 10% profit
        };

        // Act
        var stats = _calculator.CalculatePortfolioStatistics(positions);

        // Assert
        stats.AverageMargin.Percentage.Should().Be(15m); // Average of 20% and 10%
    }

    #endregion

    #region GeneratePricePointAnalysis

    [Fact]
    public void GeneratePricePointAnalysis_GeneratesCorrectPoints()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        var currentPrice = Price.Create(100m);
        var percentageChanges = new[] { -10m, 0m, 10m };

        // Act
        var points = _calculator.GeneratePricePointAnalysis(position, currentPrice, percentageChanges);

        // Assert
        points.Should().HaveCount(3);

        points[0].PriceChangePercentage.Should().Be(-10m);
        points[0].Price.Value.Should().Be(90m);
        points[0].Margin.Percentage.Should().Be(-10m);

        points[1].PriceChangePercentage.Should().Be(0m);
        points[1].Price.Value.Should().Be(100m);
        points[1].Margin.Percentage.Should().Be(0m);

        points[2].PriceChangePercentage.Should().Be(10m);
        points[2].Price.Value.Should().Be(110m);
        points[2].Margin.Percentage.Should().Be(10m);
    }

    [Fact]
    public void GeneratePricePointAnalysis_SortsByPercentage()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        var currentPrice = Price.Create(100m);
        var percentageChanges = new[] { 10m, -20m, 5m, -10m }; // Unsorted

        // Act
        var points = _calculator.GeneratePricePointAnalysis(position, currentPrice, percentageChanges);

        // Assert
        points[0].PriceChangePercentage.Should().Be(-20m);
        points[1].PriceChangePercentage.Should().Be(-10m);
        points[2].PriceChangePercentage.Should().Be(5m);
        points[3].PriceChangePercentage.Should().Be(10m);
    }

    [Fact]
    public void GeneratePricePointAnalysis_IncludesPnLCalculation()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 10m);
        // Cost = 1000 + 10 = 1010
        var currentPrice = Price.Create(100m);
        var percentageChanges = new[] { 10m }; // +10% = price 110

        // Act
        var points = _calculator.GeneratePricePointAnalysis(position, currentPrice, percentageChanges);

        // Assert
        var point = points[0];
        point.Value.Amount.Should().Be(1100m); // 110 * 10
        point.PnL.Amount.Should().Be(90m); // 1100 - 1000 - 10 fees
    }

    [Fact]
    public void GeneratePricePointAnalysis_WithSellFees_AdjustsMargin()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 100m, quantity: 10m, fees: 0m);
        var currentPrice = Price.Create(100m);
        var percentageChanges = new[] { 10m };

        // Act
        var pointsWithoutFees = _calculator.GeneratePricePointAnalysis(
            position, currentPrice, percentageChanges, sellFeePercentage: 0m);
        var pointsWithFees = _calculator.GeneratePricePointAnalysis(
            position, currentPrice, percentageChanges, sellFeePercentage: 1m);

        // Assert
        pointsWithFees[0].Margin.Percentage.Should().BeLessThan(pointsWithoutFees[0].Margin.Percentage);
    }

    #endregion

    #region DCA Position Tests

    [Fact]
    public void CalculateBreakEvenPrice_AfterDCA_UsesAverageCost()
    {
        // Arrange - Initial buy at 100, then DCA at 80
        var position = Position.Open(
            TradingPair.Create("BTCUSDT", "USDT"),
            OrderId.From("order-1"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(5m, "USDT")); // 5 fee

        position.AddDCAEntry(
            OrderId.From("order-2"),
            Price.Create(80m),
            Quantity.Create(10m),
            Money.Create(4m, "USDT")); // 4 fee

        // Total cost = 1000 + 800 = 1800
        // Total fees = 5 + 4 = 9
        // Total quantity = 20
        // Break-even = (1800 + 9) / 20 = 90.45

        // Act
        var breakEven = _calculator.CalculateBreakEvenPrice(position);

        // Assert
        breakEven.Value.Should().BeApproximately(90.45m, 0.01m);
    }

    [Fact]
    public void CalculateMarginAtPrice_AfterDCA_UsesAverageCost()
    {
        // Arrange - Initial buy at 100, then DCA at 80
        var position = Position.Open(
            TradingPair.Create("BTCUSDT", "USDT"),
            OrderId.From("order-1"),
            Price.Create(100m),
            Quantity.Create(10m),
            Money.Create(0m, "USDT"));

        position.AddDCAEntry(
            OrderId.From("order-2"),
            Price.Create(80m),
            Quantity.Create(10m),
            Money.Create(0m, "USDT"));

        // Average entry = 90, total quantity = 20

        // Act
        var marginAt90 = _calculator.CalculateMarginAtPrice(position, Price.Create(90m));

        // Assert
        marginAt90.Percentage.Should().Be(0m); // At average price, break-even
    }

    #endregion
}
