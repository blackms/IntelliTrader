using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Signals.ValueObjects;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driving;

/// <summary>
/// Primary port for signal-related operations.
/// This is a driving (primary) port - external systems use it to interact with the application.
/// </summary>
public interface ISignalUseCase
{
    /// <summary>
    /// Gets the current signal for a trading pair.
    /// </summary>
    Task<Result<TradingSignal>> GetSignalAsync(
        TradingPair pair,
        string signalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all signals for a trading pair.
    /// </summary>
    Task<Result<IReadOnlyList<TradingSignal>>> GetAllSignalsAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the aggregated signal for a trading pair.
    /// </summary>
    Task<Result<AggregatedSignal>> GetAggregatedSignalAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets signals for multiple pairs.
    /// </summary>
    Task<Result<IReadOnlyDictionary<TradingPair, AggregatedSignal>>> GetSignalsForPairsAsync(
        IEnumerable<TradingPair> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pairs that currently have buy signals.
    /// </summary>
    Task<Result<IReadOnlyList<BuySignalCandidate>>> GetBuyCandidatesAsync(
        BuySignalCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates whether a pair should be bought based on signal rules.
    /// </summary>
    Task<Result<SignalEvaluationResult>> EvaluateBuySignalAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to signal updates.
    /// </summary>
    IObservable<SignalUpdate> SubscribeToSignalUpdates();
}

/// <summary>
/// Criteria for finding buy signal candidates.
/// </summary>
public sealed record BuySignalCriteria
{
    /// <summary>
    /// Market to search in (e.g., "USDT").
    /// </summary>
    public required string Market { get; init; }

    /// <summary>
    /// Minimum overall rating to consider as buy signal.
    /// </summary>
    public decimal MinRating { get; init; } = 0.5m;

    /// <summary>
    /// Minimum number of individual buy signals required.
    /// </summary>
    public int MinBuySignalCount { get; init; } = 1;

    /// <summary>
    /// Pairs to exclude from search.
    /// </summary>
    public IReadOnlySet<string>? ExcludedPairs { get; init; }

    /// <summary>
    /// Maximum number of candidates to return.
    /// </summary>
    public int MaxResults { get; init; } = 10;
}

/// <summary>
/// A trading pair that is a candidate for buying based on signals.
/// </summary>
public sealed record BuySignalCandidate
{
    public required TradingPair Pair { get; init; }
    public required SignalRating OverallRating { get; init; }
    public required int BuySignalCount { get; init; }
    public required int TotalSignalCount { get; init; }
    public required Price CurrentPrice { get; init; }
    public required IReadOnlyList<TradingSignal> Signals { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
}

/// <summary>
/// Result of evaluating buy signals for a pair.
/// </summary>
public sealed record SignalEvaluationResult
{
    public required TradingPair Pair { get; init; }
    public required bool ShouldBuy { get; init; }
    public required SignalRating OverallRating { get; init; }
    public required string? MatchedRule { get; init; }
    public required IReadOnlyList<TradingSignal> Signals { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public required DateTimeOffset EvaluatedAt { get; init; }
}

/// <summary>
/// Signal update notification.
/// </summary>
public sealed record SignalUpdate
{
    public required TradingPair Pair { get; init; }
    public required TradingSignal Signal { get; init; }
    public required SignalUpdateType UpdateType { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Type of signal update.
/// </summary>
public enum SignalUpdateType
{
    New,
    Changed,
    Expired
}
