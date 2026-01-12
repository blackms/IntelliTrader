using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Aggregates;

/// <summary>
/// Represents a trading portfolio that tracks balance and active positions.
/// This is the main aggregate for managing trading account state.
/// </summary>
public sealed class Portfolio : AggregateRoot<PortfolioId>
{
    private readonly Dictionary<TradingPair, PositionId> _activePositions = new();
    private readonly Dictionary<PositionId, Money> _positionCosts = new();

    /// <summary>
    /// The name of this portfolio.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// The market currency (e.g., "USDT", "BTC").
    /// </summary>
    public string Market { get; private set; } = null!;

    /// <summary>
    /// The current balance state.
    /// </summary>
    public PortfolioBalance Balance { get; private set; } = null!;

    /// <summary>
    /// Maximum number of concurrent positions allowed.
    /// </summary>
    public int MaxPositions { get; private set; }

    /// <summary>
    /// Minimum cost required per position.
    /// </summary>
    public Money MinPositionCost { get; private set; } = null!;

    /// <summary>
    /// The active positions (pair -> position ID).
    /// </summary>
    public IReadOnlyDictionary<TradingPair, PositionId> ActivePositions => _activePositions;

    /// <summary>
    /// The number of currently active positions.
    /// </summary>
    public int ActivePositionCount => _activePositions.Count;

    /// <summary>
    /// Whether the portfolio can open new positions.
    /// </summary>
    public bool CanOpenNewPosition => ActivePositionCount < MaxPositions && Balance.Available >= MinPositionCost;

    /// <summary>
    /// Whether the maximum position limit has been reached.
    /// </summary>
    public bool IsAtMaxPositions => ActivePositionCount >= MaxPositions;

    /// <summary>
    /// The timestamp when this portfolio was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    // Private constructor for controlled creation
    private Portfolio() { }

    /// <summary>
    /// Creates a new portfolio.
    /// </summary>
    public static Portfolio Create(
        string name,
        string market,
        decimal initialBalance,
        int maxPositions,
        decimal minPositionCost)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        if (string.IsNullOrWhiteSpace(market))
            throw new ArgumentException("Market cannot be null or empty", nameof(market));

        if (initialBalance < 0)
            throw new ArgumentException("Initial balance cannot be negative", nameof(initialBalance));

        if (maxPositions <= 0)
            throw new ArgumentException("Max positions must be greater than zero", nameof(maxPositions));

        if (minPositionCost < 0)
            throw new ArgumentException("Min position cost cannot be negative", nameof(minPositionCost));

        market = market.ToUpperInvariant();

        return new Portfolio
        {
            Id = PortfolioId.Create(),
            Name = name,
            Market = market,
            Balance = PortfolioBalance.Create(initialBalance, market),
            MaxPositions = maxPositions,
            MinPositionCost = Money.Create(minPositionCost, market),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Checks if the portfolio can afford the specified cost.
    /// </summary>
    public bool CanAfford(Money cost)
    {
        EnsureSameCurrency(cost);
        return Balance.CanAfford(cost);
    }

    /// <summary>
    /// Checks if a position exists for the specified trading pair.
    /// </summary>
    public bool HasPositionFor(TradingPair pair)
    {
        return _activePositions.ContainsKey(pair);
    }

    /// <summary>
    /// Gets the position ID for the specified trading pair.
    /// </summary>
    public PositionId? GetPositionId(TradingPair pair)
    {
        return _activePositions.TryGetValue(pair, out var id) ? id : null;
    }

    /// <summary>
    /// Records a new position being opened.
    /// </summary>
    public void RecordPositionOpened(PositionId positionId, TradingPair pair, Money cost)
    {
        ArgumentNullException.ThrowIfNull(positionId);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(cost);
        EnsureSameCurrency(cost);

        if (HasPositionFor(pair))
            throw new InvalidOperationException($"Position already exists for {pair}");

        if (IsAtMaxPositions)
        {
            AddDomainEvent(new MaxPositionsReached(Id, MaxPositions, ActivePositionCount));
            throw new InvalidOperationException($"Maximum positions ({MaxPositions}) reached");
        }

        if (!CanAfford(cost))
            throw new InvalidOperationException($"Insufficient funds. Available: {Balance.Available}, Required: {cost}");

        var previousBalance = Balance;
        Balance = Balance.Reserve(cost);
        _activePositions[pair] = positionId;
        _positionCosts[positionId] = cost;

        AddDomainEvent(new PositionAddedToPortfolio(
            Id,
            positionId,
            pair,
            cost,
            ActivePositionCount));

        AddDomainEvent(new PortfolioBalanceChanged(
            Id,
            previousBalance.Total,
            Balance.Total,
            previousBalance.Available,
            Balance.Available,
            $"Position opened for {pair}"));
    }

    /// <summary>
    /// Records additional cost for an existing position (DCA).
    /// </summary>
    public void RecordPositionCostIncreased(PositionId positionId, TradingPair pair, Money additionalCost)
    {
        ArgumentNullException.ThrowIfNull(positionId);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(additionalCost);
        EnsureSameCurrency(additionalCost);

        if (!HasPositionFor(pair))
            throw new InvalidOperationException($"No position exists for {pair}");

        if (_activePositions[pair] != positionId)
            throw new InvalidOperationException($"Position ID mismatch for {pair}");

        if (!CanAfford(additionalCost))
            throw new InvalidOperationException($"Insufficient funds. Available: {Balance.Available}, Required: {additionalCost}");

        var previousBalance = Balance;
        Balance = Balance.Reserve(additionalCost);
        _positionCosts[positionId] = _positionCosts[positionId] + additionalCost;

        AddDomainEvent(new PortfolioBalanceChanged(
            Id,
            previousBalance.Total,
            Balance.Total,
            previousBalance.Available,
            Balance.Available,
            $"DCA executed for {pair}"));
    }

    /// <summary>
    /// Records a position being closed.
    /// </summary>
    public void RecordPositionClosed(PositionId positionId, TradingPair pair, Money proceeds)
    {
        ArgumentNullException.ThrowIfNull(positionId);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(proceeds);
        EnsureSameCurrency(proceeds);

        if (!HasPositionFor(pair))
            throw new InvalidOperationException($"No position exists for {pair}");

        if (_activePositions[pair] != positionId)
            throw new InvalidOperationException($"Position ID mismatch for {pair}");

        var cost = _positionCosts[positionId];
        var pnl = proceeds - cost;

        var previousBalance = Balance;

        // Release the reserved cost and record PnL
        Balance = Balance.Release(cost);
        Balance = Balance.RecordPnL(pnl);

        _activePositions.Remove(pair);
        _positionCosts.Remove(positionId);

        AddDomainEvent(new PositionRemovedFromPortfolio(
            Id,
            positionId,
            pair,
            proceeds,
            pnl,
            ActivePositionCount));

        AddDomainEvent(new PortfolioBalanceChanged(
            Id,
            previousBalance.Total,
            Balance.Total,
            previousBalance.Available,
            Balance.Available,
            $"Position closed for {pair}, PnL: {pnl}"));
    }

    /// <summary>
    /// Updates the portfolio configuration.
    /// </summary>
    public void UpdateConfiguration(int? maxPositions = null, decimal? minPositionCost = null)
    {
        if (maxPositions.HasValue)
        {
            if (maxPositions.Value <= 0)
                throw new ArgumentException("Max positions must be greater than zero", nameof(maxPositions));

            MaxPositions = maxPositions.Value;
        }

        if (minPositionCost.HasValue)
        {
            if (minPositionCost.Value < 0)
                throw new ArgumentException("Min position cost cannot be negative", nameof(minPositionCost));

            MinPositionCost = Money.Create(minPositionCost.Value, Market);
        }
    }

    /// <summary>
    /// Synchronizes the balance with the exchange.
    /// </summary>
    public void SyncBalance(decimal exchangeBalance)
    {
        var previousBalance = Balance;
        Balance = Balance.UpdateTotal(exchangeBalance);

        if (previousBalance.Total != Balance.Total)
        {
            AddDomainEvent(new PortfolioBalanceChanged(
                Id,
                previousBalance.Total,
                Balance.Total,
                previousBalance.Available,
                Balance.Available,
                "Balance synced with exchange"));
        }
    }

    /// <summary>
    /// Gets the total cost currently invested in positions.
    /// </summary>
    public Money GetTotalInvestedCost()
    {
        return _positionCosts.Values.Aggregate(
            Money.Zero(Market),
            (sum, cost) => sum + cost);
    }

    /// <summary>
    /// Gets the cost for a specific position.
    /// </summary>
    public Money? GetPositionCost(PositionId positionId)
    {
        return _positionCosts.TryGetValue(positionId, out var cost) ? cost : null;
    }

    /// <summary>
    /// Gets all active trading pairs.
    /// </summary>
    public IReadOnlyCollection<TradingPair> GetActivePairs()
    {
        return _activePositions.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Calculates the maximum cost allowed for a new position based on available balance.
    /// </summary>
    /// <param name="maxPercentage">Maximum percentage of available balance to use</param>
    public Money CalculateMaxPositionCost(decimal maxPercentage = 100m)
    {
        if (maxPercentage <= 0 || maxPercentage > 100)
            throw new ArgumentException("Percentage must be between 0 and 100", nameof(maxPercentage));

        var maxCost = Balance.Available * (maxPercentage / 100m);

        // Ensure it's at least the minimum position cost if we have enough funds
        if (maxCost < MinPositionCost && Balance.Available >= MinPositionCost)
            return MinPositionCost;

        return maxCost;
    }

    private void EnsureSameCurrency(Money money)
    {
        if (money.Currency != Market)
            throw new InvalidOperationException($"Currency mismatch: expected {Market}, got {money.Currency}");
    }
}
