namespace IntelliTrader.Domain.Rules;

/// <summary>
/// Context containing all data needed to evaluate rule conditions.
/// This is the candidate passed to specifications.
/// </summary>
public record RuleEvaluationContext
{
    /// <summary>
    /// The trading pair being evaluated (e.g., "BTCUSDT")
    /// </summary>
    public string? Pair { get; init; }

    /// <summary>
    /// Signals indexed by signal name
    /// </summary>
    public IReadOnlyDictionary<string, SignalSnapshot> Signals { get; init; } = new Dictionary<string, SignalSnapshot>();

    /// <summary>
    /// The global rating across all configured signals
    /// </summary>
    public double? GlobalRating { get; init; }

    /// <summary>
    /// The current position data (if a position exists for this pair)
    /// </summary>
    public PositionSnapshot? Position { get; init; }

    /// <summary>
    /// Speed multiplier for age calculations (used in backtesting)
    /// </summary>
    public double SpeedMultiplier { get; init; } = 1.0;

    public SignalSnapshot? GetSignal(string? signalName)
    {
        if (signalName is null) return null;
        return Signals.TryGetValue(signalName, out var signal) ? signal : null;
    }
}

/// <summary>
/// Snapshot of signal data for rule evaluation
/// </summary>
public record SignalSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string Pair { get; init; } = string.Empty;
    public long? Volume { get; init; }
    public double? VolumeChange { get; init; }
    public decimal? Price { get; init; }
    public decimal? PriceChange { get; init; }
    public double? Rating { get; init; }
    public double? RatingChange { get; init; }
    public double? Volatility { get; init; }
}

/// <summary>
/// Snapshot of position data for rule evaluation
/// </summary>
public record PositionSnapshot
{
    public string Pair { get; init; } = string.Empty;
    public TimeSpan CurrentAge { get; init; }
    public TimeSpan LastBuyAge { get; init; }
    public decimal CurrentMargin { get; init; }
    public decimal? LastBuyMargin { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal CurrentCost { get; init; }
    public int DCALevel { get; init; }
    public string? SignalRule { get; init; }
}
