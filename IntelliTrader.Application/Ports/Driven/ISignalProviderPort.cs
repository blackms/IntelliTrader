using IntelliTrader.Application.Common;
using IntelliTrader.Domain.Signals.ValueObjects;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Port for receiving trading signals from external signal providers.
/// This is a driven (secondary) port - the application defines it, infrastructure implements it.
/// </summary>
public interface ISignalProviderPort
{
    /// <summary>
    /// Gets the name of the signal provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the current signal for a specific trading pair.
    /// </summary>
    Task<Result<TradingSignal>> GetSignalAsync(
        TradingPair pair,
        string signalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all current signals for a trading pair.
    /// </summary>
    Task<Result<IReadOnlyList<TradingSignal>>> GetAllSignalsAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets signals for multiple trading pairs.
    /// </summary>
    Task<Result<IReadOnlyDictionary<TradingPair, IReadOnlyList<TradingSignal>>>> GetSignalsForPairsAsync(
        IEnumerable<TradingPair> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the aggregated signal rating for a trading pair.
    /// </summary>
    Task<Result<AggregatedSignal>> GetAggregatedSignalAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to signal updates for a trading pair.
    /// </summary>
    IObservable<TradingSignal> SubscribeToSignals(TradingPair pair);

    /// <summary>
    /// Subscribes to signal updates for all pairs.
    /// </summary>
    IObservable<TradingSignal> SubscribeToAllSignals();

    /// <summary>
    /// Tests connectivity to the signal provider.
    /// </summary>
    Task<Result<bool>> TestConnectivityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a trading signal from an external provider.
/// </summary>
public sealed record TradingSignal
{
    public required string SignalName { get; init; }
    public required TradingPair Pair { get; init; }
    public required SignalRating Rating { get; init; }
    public required SignalType Type { get; init; }
    public required string ProviderName { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    public bool IsBuySignal => Rating.IsBuySignal;
    public bool IsSellSignal => Rating.IsSellSignal;
    public bool IsNeutral => Rating.IsNeutral();
}

/// <summary>
/// Represents an aggregated signal combining multiple individual signals.
/// </summary>
public sealed record AggregatedSignal
{
    public required TradingPair Pair { get; init; }
    public required SignalRating OverallRating { get; init; }
    public required int BuySignalCount { get; init; }
    public required int SellSignalCount { get; init; }
    public required int NeutralSignalCount { get; init; }
    public required IReadOnlyList<TradingSignal> IndividualSignals { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    public int TotalSignalCount => BuySignalCount + SellSignalCount + NeutralSignalCount;
    public decimal BuyPercentage => TotalSignalCount > 0 ? (decimal)BuySignalCount / TotalSignalCount * 100 : 0;
    public decimal SellPercentage => TotalSignalCount > 0 ? (decimal)SellSignalCount / TotalSignalCount * 100 : 0;
}

/// <summary>
/// Type of trading signal.
/// </summary>
public enum SignalType
{
    /// <summary>Technical analysis based signal (e.g., RSI, MACD)</summary>
    Technical,

    /// <summary>Moving average based signal</summary>
    MovingAverage,

    /// <summary>Oscillator based signal</summary>
    Oscillator,

    /// <summary>Trend indicator based signal</summary>
    Trend,

    /// <summary>Volume based signal</summary>
    Volume,

    /// <summary>Sentiment based signal</summary>
    Sentiment,

    /// <summary>Combined/summary signal</summary>
    Summary,

    /// <summary>Custom or unknown signal type</summary>
    Custom
}
