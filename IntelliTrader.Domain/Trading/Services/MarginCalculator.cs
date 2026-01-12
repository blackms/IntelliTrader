using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Services;

/// <summary>
/// Domain service for advanced margin and price calculations.
/// Provides break-even analysis, target price calculations, and portfolio-level statistics.
/// </summary>
public sealed class MarginCalculator
{
    /// <summary>
    /// Calculates the break-even price for a position (price needed to cover all costs including fees).
    /// </summary>
    /// <param name="position">The position to calculate break-even for</param>
    /// <param name="estimatedSellFeePercentage">Estimated sell fee as a percentage (e.g., 0.1 for 0.1%)</param>
    public Price CalculateBreakEvenPrice(Position position, decimal estimatedSellFeePercentage = 0m)
    {
        ArgumentNullException.ThrowIfNull(position);

        if (estimatedSellFeePercentage < 0)
            throw new ArgumentException("Fee percentage cannot be negative", nameof(estimatedSellFeePercentage));

        var totalCostWithBuyFees = position.TotalCost.Amount + position.TotalFees.Amount;
        var quantity = position.TotalQuantity.Value;

        if (quantity == 0)
            return Price.Zero;

        // Break-even price must cover: total cost + buy fees + sell fees
        // sell_fees = break_even_price * quantity * fee_percentage / 100
        // break_even_price * quantity = total_cost_with_buy_fees + (break_even_price * quantity * fee_percentage / 100)
        // break_even_price * quantity * (1 - fee_percentage / 100) = total_cost_with_buy_fees
        // break_even_price = total_cost_with_buy_fees / (quantity * (1 - fee_percentage / 100))

        var feeMultiplier = 1m - (estimatedSellFeePercentage / 100m);
        if (feeMultiplier <= 0)
            throw new ArgumentException("Fee percentage must be less than 100%", nameof(estimatedSellFeePercentage));

        var breakEvenPrice = totalCostWithBuyFees / (quantity * feeMultiplier);
        return Price.Create(breakEvenPrice);
    }

    /// <summary>
    /// Calculates the target sell price needed to achieve a specific margin.
    /// </summary>
    /// <param name="position">The position to calculate target price for</param>
    /// <param name="targetMargin">The desired margin percentage</param>
    /// <param name="estimatedSellFeePercentage">Estimated sell fee as a percentage (e.g., 0.1 for 0.1%)</param>
    public Price CalculateTargetSellPrice(Position position, Margin targetMargin, decimal estimatedSellFeePercentage = 0m)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(targetMargin);

        if (estimatedSellFeePercentage < 0)
            throw new ArgumentException("Fee percentage cannot be negative", nameof(estimatedSellFeePercentage));

        var totalCostWithBuyFees = position.TotalCost.Amount + position.TotalFees.Amount;
        var quantity = position.TotalQuantity.Value;

        if (quantity == 0)
            return Price.Zero;

        // Target value = cost * (1 + margin/100)
        var targetValue = totalCostWithBuyFees * (1m + targetMargin.Percentage / 100m);

        // Account for sell fees
        // target_value = sell_price * quantity - sell_fees
        // target_value = sell_price * quantity - (sell_price * quantity * fee_percentage / 100)
        // target_value = sell_price * quantity * (1 - fee_percentage / 100)
        // sell_price = target_value / (quantity * (1 - fee_percentage / 100))

        var feeMultiplier = 1m - (estimatedSellFeePercentage / 100m);
        if (feeMultiplier <= 0)
            throw new ArgumentException("Fee percentage must be less than 100%", nameof(estimatedSellFeePercentage));

        var targetPrice = targetValue / (quantity * feeMultiplier);
        return Price.Create(Math.Max(0, targetPrice));
    }

    /// <summary>
    /// Calculates what margin would be achieved at a given sell price.
    /// </summary>
    /// <param name="position">The position</param>
    /// <param name="sellPrice">The hypothetical sell price</param>
    /// <param name="estimatedSellFeePercentage">Estimated sell fee as a percentage</param>
    public Margin CalculateMarginAtPrice(Position position, Price sellPrice, decimal estimatedSellFeePercentage = 0m)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(sellPrice);

        if (estimatedSellFeePercentage < 0)
            throw new ArgumentException("Fee percentage cannot be negative", nameof(estimatedSellFeePercentage));

        var totalCostWithBuyFees = position.TotalCost.Amount + position.TotalFees.Amount;

        if (totalCostWithBuyFees == 0)
            return Margin.Zero;

        var grossValue = sellPrice.Value * position.TotalQuantity.Value;
        var sellFees = grossValue * (estimatedSellFeePercentage / 100m);
        var netValue = grossValue - sellFees;

        return Margin.Calculate(totalCostWithBuyFees, netValue);
    }

    /// <summary>
    /// Calculates the price drop percentage from entry to current price.
    /// </summary>
    /// <param name="position">The position</param>
    /// <param name="currentPrice">The current market price</param>
    public decimal CalculatePriceDropPercentage(Position position, Price currentPrice)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(currentPrice);

        var avgEntry = position.AveragePrice.Value;
        if (avgEntry == 0)
            return 0m;

        return ((avgEntry - currentPrice.Value) / avgEntry) * 100m;
    }

    /// <summary>
    /// Calculates the price at which a specific margin would be achieved.
    /// </summary>
    /// <param name="costBasis">The total cost basis</param>
    /// <param name="quantity">The quantity held</param>
    /// <param name="targetMargin">The target margin</param>
    public Price CalculatePriceForMargin(Money costBasis, Quantity quantity, Margin targetMargin)
    {
        ArgumentNullException.ThrowIfNull(costBasis);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(targetMargin);

        if (quantity.Value == 0)
            return Price.Zero;

        var targetValue = costBasis.Amount * (1m + targetMargin.Percentage / 100m);
        var price = targetValue / quantity.Value;
        return Price.Create(Math.Max(0, price));
    }

    /// <summary>
    /// Analyzes fee impact on a position's profitability.
    /// </summary>
    public FeeImpactAnalysis AnalyzeFeeImpact(Position position, decimal sellFeePercentage = 0m)
    {
        ArgumentNullException.ThrowIfNull(position);

        var buyFees = position.TotalFees;
        var estimatedSellFees = Money.Create(
            position.TotalCost.Amount * (sellFeePercentage / 100m),
            position.Currency);

        var totalFees = buyFees + estimatedSellFees;
        var breakEvenWithoutFees = position.TotalCost.Amount / position.TotalQuantity.Value;
        var breakEvenWithFees = CalculateBreakEvenPrice(position, sellFeePercentage).Value;
        var feeImpactOnBreakEven = breakEvenWithFees - breakEvenWithoutFees;

        // Calculate fee percentage of total cost
        var feePercentageOfCost = position.TotalCost.Amount > 0
            ? (totalFees.Amount / position.TotalCost.Amount) * 100m
            : 0m;

        return new FeeImpactAnalysis(
            BuyFees: buyFees,
            EstimatedSellFees: estimatedSellFees,
            TotalFees: totalFees,
            BreakEvenPriceWithoutFees: Price.Create(breakEvenWithoutFees),
            BreakEvenPriceWithFees: Price.Create(breakEvenWithFees),
            FeeImpactOnBreakEven: Price.Create(feeImpactOnBreakEven),
            FeePercentageOfCost: feePercentageOfCost);
    }

    /// <summary>
    /// Calculates margin statistics across multiple positions.
    /// </summary>
    public PortfolioMarginStatistics CalculatePortfolioStatistics(
        IEnumerable<(Position Position, Price CurrentPrice)> positionsWithPrices,
        decimal sellFeePercentage = 0m)
    {
        ArgumentNullException.ThrowIfNull(positionsWithPrices);

        var positions = positionsWithPrices.ToList();
        if (positions.Count == 0)
        {
            return PortfolioMarginStatistics.Empty();
        }

        var totalCost = 0m;
        var totalCurrentValue = 0m;
        var totalFees = 0m;
        var margins = new List<decimal>();
        var profitableCount = 0;
        var losingCount = 0;
        var bestMargin = decimal.MinValue;
        var worstMargin = decimal.MaxValue;
        Position? bestPosition = null;
        Position? worstPosition = null;

        foreach (var (position, currentPrice) in positions)
        {
            var cost = position.TotalCost.Amount + position.TotalFees.Amount;
            var value = currentPrice.Value * position.TotalQuantity.Value;
            var sellFees = value * (sellFeePercentage / 100m);
            var netValue = value - sellFees;

            totalCost += cost;
            totalCurrentValue += netValue;
            totalFees += position.TotalFees.Amount + sellFees;

            var margin = CalculateMarginAtPrice(position, currentPrice, sellFeePercentage);
            margins.Add(margin.Percentage);

            if (margin.IsProfit) profitableCount++;
            else if (margin.IsLoss) losingCount++;

            if (margin.Percentage > bestMargin)
            {
                bestMargin = margin.Percentage;
                bestPosition = position;
            }

            if (margin.Percentage < worstMargin)
            {
                worstMargin = margin.Percentage;
                worstPosition = position;
            }
        }

        var totalPnL = totalCurrentValue - totalCost;
        var overallMargin = totalCost > 0 ? Margin.Calculate(totalCost, totalCurrentValue) : Margin.Zero;
        var averageMargin = margins.Count > 0 ? Margin.FromPercentage(margins.Average()) : Margin.Zero;

        return new PortfolioMarginStatistics(
            TotalPositions: positions.Count,
            ProfitablePositions: profitableCount,
            LosingPositions: losingCount,
            BreakEvenPositions: positions.Count - profitableCount - losingCount,
            TotalCost: Money.Create(totalCost, positions.First().Position.Currency),
            TotalCurrentValue: Money.Create(totalCurrentValue, positions.First().Position.Currency),
            TotalPnL: Money.Create(totalPnL, positions.First().Position.Currency),
            TotalFees: Money.Create(totalFees, positions.First().Position.Currency),
            OverallMargin: overallMargin,
            AverageMargin: averageMargin,
            BestMargin: Margin.FromPercentage(bestMargin),
            WorstMargin: Margin.FromPercentage(worstMargin),
            BestPerformingPair: bestPosition?.Pair,
            WorstPerformingPair: worstPosition?.Pair);
    }

    /// <summary>
    /// Generates a margin analysis at multiple price points.
    /// </summary>
    public IReadOnlyList<PricePoint> GeneratePricePointAnalysis(
        Position position,
        Price currentPrice,
        decimal[] percentageChanges,
        decimal sellFeePercentage = 0m)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(currentPrice);
        ArgumentNullException.ThrowIfNull(percentageChanges);

        var results = new List<PricePoint>();

        foreach (var change in percentageChanges.OrderBy(x => x))
        {
            var price = Price.Create(currentPrice.Value * (1m + change / 100m));
            var margin = CalculateMarginAtPrice(position, price, sellFeePercentage);
            var value = Money.Create(price.Value * position.TotalQuantity.Value, position.Currency);
            var pnl = value - position.TotalCost - position.TotalFees;

            results.Add(new PricePoint(
                PriceChangePercentage: change,
                Price: price,
                Margin: margin,
                Value: value,
                PnL: pnl));
        }

        return results.AsReadOnly();
    }
}

/// <summary>
/// Analysis of fee impact on a position.
/// </summary>
public sealed record FeeImpactAnalysis(
    Money BuyFees,
    Money EstimatedSellFees,
    Money TotalFees,
    Price BreakEvenPriceWithoutFees,
    Price BreakEvenPriceWithFees,
    Price FeeImpactOnBreakEven,
    decimal FeePercentageOfCost);

/// <summary>
/// Portfolio-wide margin statistics.
/// </summary>
public sealed record PortfolioMarginStatistics(
    int TotalPositions,
    int ProfitablePositions,
    int LosingPositions,
    int BreakEvenPositions,
    Money TotalCost,
    Money TotalCurrentValue,
    Money TotalPnL,
    Money TotalFees,
    Margin OverallMargin,
    Margin AverageMargin,
    Margin BestMargin,
    Margin WorstMargin,
    TradingPair? BestPerformingPair,
    TradingPair? WorstPerformingPair)
{
    public static PortfolioMarginStatistics Empty() => new(
        TotalPositions: 0,
        ProfitablePositions: 0,
        LosingPositions: 0,
        BreakEvenPositions: 0,
        TotalCost: Money.Zero("USDT"),
        TotalCurrentValue: Money.Zero("USDT"),
        TotalPnL: Money.Zero("USDT"),
        TotalFees: Money.Zero("USDT"),
        OverallMargin: Margin.Zero,
        AverageMargin: Margin.Zero,
        BestMargin: Margin.Zero,
        WorstMargin: Margin.Zero,
        BestPerformingPair: null,
        WorstPerformingPair: null);
}

/// <summary>
/// Represents analysis at a specific price point.
/// </summary>
public sealed record PricePoint(
    decimal PriceChangePercentage,
    Price Price,
    Margin Margin,
    Money Value,
    Money PnL);
