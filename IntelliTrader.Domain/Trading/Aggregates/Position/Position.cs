using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Aggregates;

/// <summary>
/// Represents an open trading position for a specific trading pair.
/// This is the main aggregate for tracking trades with DCA (Dollar Cost Averaging) support.
/// </summary>
public sealed class Position : AggregateRoot<PositionId>
{
    private readonly List<PositionEntry> _entries = new();

    /// <summary>
    /// The trading pair (e.g., BTCUSDT).
    /// </summary>
    public TradingPair Pair { get; private set; } = null!;

    /// <summary>
    /// The quote/market currency for this position.
    /// </summary>
    public string Currency { get; private set; } = null!;

    /// <summary>
    /// The signal rule that triggered this position (if any).
    /// </summary>
    public string? SignalRule { get; private set; }

    /// <summary>
    /// All buy entries in this position (initial + DCA buys).
    /// </summary>
    public IReadOnlyList<PositionEntry> Entries => _entries.AsReadOnly();

    /// <summary>
    /// The current DCA level (0 = initial buy, 1+ = number of DCA buys).
    /// </summary>
    public int DCALevel => Math.Max(0, _entries.Count - 1);

    /// <summary>
    /// The timestamp when this position was opened.
    /// </summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>
    /// The timestamp of the last buy (initial or DCA).
    /// </summary>
    public DateTimeOffset LastBuyAt { get; private set; }

    /// <summary>
    /// Indicates whether this position is closed.
    /// </summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// The timestamp when this position was closed (if closed).
    /// </summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>
    /// The total quantity of the asset held.
    /// </summary>
    public Quantity TotalQuantity => _entries.Aggregate(
        Quantity.Zero,
        (sum, entry) => sum + entry.Quantity);

    /// <summary>
    /// The total cost of all entries (excluding fees).
    /// </summary>
    public Money TotalCost => _entries.Aggregate(
        Money.Zero(Currency),
        (sum, entry) => sum + entry.Cost);

    /// <summary>
    /// The total fees paid across all entries.
    /// </summary>
    public Money TotalFees => _entries.Aggregate(
        Money.Zero(Currency),
        (sum, entry) => sum + entry.Fees);

    /// <summary>
    /// The average buy price weighted by quantity.
    /// </summary>
    public Price AveragePrice
    {
        get
        {
            var totalQty = TotalQuantity;
            if (totalQty.IsZero)
                return Price.Zero;

            return Price.Create(TotalCost.Amount / totalQty.Value);
        }
    }

    /// <summary>
    /// The current age of the position since it was opened.
    /// </summary>
    public TimeSpan Age => DateTimeOffset.UtcNow - OpenedAt;

    /// <summary>
    /// The time elapsed since the last buy.
    /// </summary>
    public TimeSpan TimeSinceLastBuy => DateTimeOffset.UtcNow - LastBuyAt;

    // Private constructor for controlled creation
    private Position() { }

    /// <summary>
    /// Opens a new position with an initial buy entry.
    /// </summary>
    public static Position Open(
        TradingPair pair,
        OrderId orderId,
        Price price,
        Quantity quantity,
        Money fees,
        string? signalRule = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(fees);

        if (price.IsZero)
            throw new ArgumentException("Price cannot be zero", nameof(price));

        if (quantity.IsZero)
            throw new ArgumentException("Quantity cannot be zero", nameof(quantity));

        var now = timestamp ?? DateTimeOffset.UtcNow;
        var position = new Position
        {
            Id = PositionId.Create(),
            Pair = pair,
            Currency = fees.Currency,
            SignalRule = signalRule,
            OpenedAt = now,
            LastBuyAt = now,
            IsClosed = false
        };

        var entry = PositionEntry.Create(orderId, price, quantity, fees, now);
        position._entries.Add(entry);

        position.AddDomainEvent(new PositionOpened(
            position.Id,
            pair,
            orderId,
            price,
            quantity,
            entry.Cost,
            fees,
            signalRule));

        return position;
    }

    /// <summary>
    /// Adds a DCA (Dollar Cost Average) buy entry to the position.
    /// </summary>
    public void AddDCAEntry(
        OrderId orderId,
        Price price,
        Quantity quantity,
        Money fees,
        DateTimeOffset? timestamp = null)
    {
        EnsureNotClosed();

        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(fees);

        if (price.IsZero)
            throw new ArgumentException("Price cannot be zero", nameof(price));

        if (quantity.IsZero)
            throw new ArgumentException("Quantity cannot be zero", nameof(quantity));

        if (fees.Currency != Currency)
            throw new InvalidOperationException($"Fee currency mismatch. Expected {Currency}, got {fees.Currency}");

        var now = timestamp ?? DateTimeOffset.UtcNow;
        var entry = PositionEntry.Create(orderId, price, quantity, fees, now);
        _entries.Add(entry);
        LastBuyAt = now;

        AddDomainEvent(new DCAExecuted(
            Id,
            Pair,
            orderId,
            DCALevel,
            price,
            quantity,
            entry.Cost,
            fees,
            AveragePrice,
            TotalQuantity,
            TotalCost));
    }

    /// <summary>
    /// Closes the position with a sell order.
    /// </summary>
    public void Close(
        OrderId sellOrderId,
        Price sellPrice,
        Money sellFees,
        DateTimeOffset? timestamp = null)
    {
        EnsureNotClosed();

        ArgumentNullException.ThrowIfNull(sellOrderId);
        ArgumentNullException.ThrowIfNull(sellPrice);
        ArgumentNullException.ThrowIfNull(sellFees);

        if (sellPrice.IsZero)
            throw new ArgumentException("Sell price cannot be zero", nameof(sellPrice));

        var now = timestamp ?? DateTimeOffset.UtcNow;
        var proceeds = Money.Create(sellPrice.Value * TotalQuantity.Value, Currency);
        var allFees = TotalFees + sellFees;
        var finalMargin = CalculateMargin(sellPrice, sellFees);

        IsClosed = true;
        ClosedAt = now;

        AddDomainEvent(new PositionClosed(
            Id,
            Pair,
            sellOrderId,
            sellPrice,
            TotalQuantity,
            proceeds,
            TotalCost,
            allFees,
            finalMargin,
            DCALevel,
            now - OpenedAt));
    }

    /// <summary>
    /// Calculates the current margin (profit/loss percentage) at the given price.
    /// </summary>
    /// <param name="currentPrice">The current market price</param>
    /// <param name="estimatedSellFees">Optional estimated sell fees to include in calculation</param>
    public Margin CalculateMargin(Price currentPrice, Money? estimatedSellFees = null)
    {
        ArgumentNullException.ThrowIfNull(currentPrice);

        var currentValue = currentPrice.Value * TotalQuantity.Value;
        var totalCostWithFees = TotalCost.Amount + TotalFees.Amount;

        if (estimatedSellFees != null)
        {
            totalCostWithFees += estimatedSellFees.Amount;
        }

        if (totalCostWithFees == 0)
            return Margin.Zero;

        return Margin.Calculate(totalCostWithFees, currentValue);
    }

    /// <summary>
    /// Calculates the current value of the position at the given price.
    /// </summary>
    public Money CalculateCurrentValue(Price currentPrice)
    {
        ArgumentNullException.ThrowIfNull(currentPrice);
        return Money.Create(currentPrice.Value * TotalQuantity.Value, Currency);
    }

    /// <summary>
    /// Calculates the unrealized profit/loss at the given price.
    /// </summary>
    public Money CalculateUnrealizedPnL(Price currentPrice, Money? estimatedSellFees = null)
    {
        var currentValue = CalculateCurrentValue(currentPrice);
        var costBasis = TotalCost + TotalFees;

        if (estimatedSellFees != null)
        {
            costBasis = costBasis + estimatedSellFees;
        }

        return currentValue - costBasis;
    }

    /// <summary>
    /// Checks if DCA is allowed based on price drop from average.
    /// </summary>
    /// <param name="currentPrice">The current market price</param>
    /// <param name="minPriceDropPercent">Minimum percentage drop required from average price</param>
    public bool CanDCAByPriceDrop(Price currentPrice, decimal minPriceDropPercent)
    {
        if (IsClosed || AveragePrice.IsZero)
            return false;

        var dropPercent = ((AveragePrice.Value - currentPrice.Value) / AveragePrice.Value) * 100;
        return dropPercent >= minPriceDropPercent;
    }

    /// <summary>
    /// Checks if the position meets the minimum age requirement for DCA.
    /// </summary>
    public bool MeetsMinimumAgeForDCA(TimeSpan minAge)
    {
        return TimeSinceLastBuy >= minAge;
    }

    /// <summary>
    /// Gets the entry at the specified DCA level.
    /// </summary>
    /// <param name="level">0 for initial buy, 1+ for DCA levels</param>
    public PositionEntry? GetEntryAtLevel(int level)
    {
        if (level < 0 || level >= _entries.Count)
            return null;

        return _entries[level];
    }

    /// <summary>
    /// Gets the margin at the time of the last buy.
    /// </summary>
    public Margin? GetLastBuyMargin(Price currentPrice)
    {
        var lastEntry = _entries.LastOrDefault();
        if (lastEntry == null)
            return null;

        return lastEntry.CalculateMargin(currentPrice);
    }

    private void EnsureNotClosed()
    {
        if (IsClosed)
            throw new InvalidOperationException("Cannot modify a closed position");
    }
}
