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

    /// <summary>
    /// Gets the signal snapshot for a specific signal name.
    /// </summary>
    /// <param name="signalName">The signal name to look up.</param>
    /// <returns>The signal snapshot, or null if not found.</returns>
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
    /// <summary>The signal source name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The trading pair symbol.</summary>
    public string Pair { get; init; } = string.Empty;

    /// <summary>The 24-hour trading volume.</summary>
    public long? Volume { get; init; }

    /// <summary>The percentage change in volume.</summary>
    public double? VolumeChange { get; init; }

    /// <summary>The current price from the signal source.</summary>
    public decimal? Price { get; init; }

    /// <summary>The percentage change in price.</summary>
    public decimal? PriceChange { get; init; }

    /// <summary>The signal rating score.</summary>
    public double? Rating { get; init; }

    /// <summary>The change in signal rating.</summary>
    public double? RatingChange { get; init; }

    /// <summary>The price volatility measurement.</summary>
    public double? Volatility { get; init; }
}

/// <summary>
/// Snapshot of position data for rule evaluation
/// </summary>
public record PositionSnapshot
{
    /// <summary>The trading pair symbol.</summary>
    public string Pair { get; init; } = string.Empty;

    /// <summary>Time since the first buy order.</summary>
    public TimeSpan CurrentAge { get; init; }

    /// <summary>Time since the most recent buy order.</summary>
    public TimeSpan LastBuyAge { get; init; }

    /// <summary>Current profit/loss margin as a percentage.</summary>
    public decimal CurrentMargin { get; init; }

    /// <summary>Margin at the time of the last buy, if available.</summary>
    public decimal? LastBuyMargin { get; init; }

    /// <summary>Total amount held in the pair's base currency.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Current cost of the position at market price.</summary>
    public decimal CurrentCost { get; init; }

    /// <summary>The current DCA level (0 = initial buy).</summary>
    public int DCALevel { get; init; }

    /// <summary>The signal rule that triggered the initial buy.</summary>
    public string? SignalRule { get; init; }
}
