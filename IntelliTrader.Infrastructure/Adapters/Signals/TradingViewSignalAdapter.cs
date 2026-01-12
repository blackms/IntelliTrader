using System.Reactive.Linq;
using System.Reactive.Subjects;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using IntelliTrader.Domain.Signals.ValueObjects;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.Adapters.Signals;

/// <summary>
/// Adapter that wraps the legacy ISignalsService to implement the new ISignalProviderPort interface.
/// This allows the new hexagonal architecture to use the existing TradingView signal integration.
/// </summary>
public sealed class TradingViewSignalAdapter : ISignalProviderPort, IDisposable
{
    private const string DefaultProviderName = "TradingView";

    private readonly ISignalsService _legacySignals;
    private readonly string _quoteCurrency;
    private readonly Subject<TradingSignal> _signalSubject;
    private bool _disposed;

    /// <summary>
    /// Creates a new TradingViewSignalAdapter.
    /// </summary>
    /// <param name="legacySignals">The legacy signals service to wrap.</param>
    /// <param name="quoteCurrency">The quote currency for the market (e.g., "USDT", "BTC").</param>
    public TradingViewSignalAdapter(ISignalsService legacySignals, string quoteCurrency = "USDT")
    {
        _legacySignals = legacySignals ?? throw new ArgumentNullException(nameof(legacySignals));
        _quoteCurrency = quoteCurrency ?? throw new ArgumentNullException(nameof(quoteCurrency));
        _signalSubject = new Subject<TradingSignal>();
    }

    /// <inheritdoc />
    public string ProviderName => DefaultProviderName;

    /// <inheritdoc />
    public Task<Result<TradingSignal>> GetSignalAsync(
        TradingPair pair,
        string signalName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(signalName);

        try
        {
            var legacySignals = _legacySignals.GetSignalsByPair(pair.Symbol);
            var signal = legacySignals.FirstOrDefault(s =>
                s.Name.Equals(signalName, StringComparison.OrdinalIgnoreCase));

            if (signal == null)
            {
                return Task.FromResult(Result<TradingSignal>.Failure(
                    Error.NotFound("Signal", $"{signalName} for {pair.Symbol}")));
            }

            var tradingSignal = ConvertToTradingSignal(signal, pair);
            return Task.FromResult(Result<TradingSignal>.Success(tradingSignal));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<TradingSignal>.Failure(
                Error.ExchangeError($"Failed to get signal {signalName} for {pair.Symbol}: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TradingSignal>>> GetAllSignalsAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);

        try
        {
            var legacySignals = _legacySignals.GetSignalsByPair(pair.Symbol);
            var signals = legacySignals
                .Where(s => s.Rating.HasValue)
                .Select(s => ConvertToTradingSignal(s, pair))
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<TradingSignal>>.Success(signals));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyList<TradingSignal>>.Failure(
                Error.ExchangeError($"Failed to get signals for {pair.Symbol}: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyDictionary<TradingPair, IReadOnlyList<TradingSignal>>>> GetSignalsForPairsAsync(
        IEnumerable<TradingPair> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        try
        {
            var result = new Dictionary<TradingPair, IReadOnlyList<TradingSignal>>();
            var allLegacySignals = _legacySignals.GetAllSignals()
                .Where(s => s.Rating.HasValue)
                .ToLookup(s => s.Pair);

            foreach (var pair in pairs)
            {
                var pairSignals = allLegacySignals[pair.Symbol]
                    .Select(s => ConvertToTradingSignal(s, pair))
                    .ToList();

                if (pairSignals.Count > 0)
                {
                    result[pair] = pairSignals;
                }
            }

            return Task.FromResult(Result<IReadOnlyDictionary<TradingPair, IReadOnlyList<TradingSignal>>>.Success(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyDictionary<TradingPair, IReadOnlyList<TradingSignal>>>.Failure(
                Error.ExchangeError($"Failed to get signals for pairs: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public Task<Result<AggregatedSignal>> GetAggregatedSignalAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pair);

        try
        {
            var signalNames = _legacySignals.GetSignalNames();
            var overallRating = _legacySignals.GetRating(pair.Symbol, signalNames);

            if (!overallRating.HasValue)
            {
                return Task.FromResult(Result<AggregatedSignal>.Failure(
                    Error.NotFound("AggregatedSignal", pair.Symbol)));
            }

            var legacySignals = _legacySignals.GetSignalsByPair(pair.Symbol)
                .Where(s => s.Rating.HasValue)
                .ToList();

            var individualSignals = legacySignals
                .Select(s => ConvertToTradingSignal(s, pair))
                .ToList();

            var buyCount = individualSignals.Count(s => s.IsBuySignal);
            var sellCount = individualSignals.Count(s => s.IsSellSignal);
            var neutralCount = individualSignals.Count(s => s.IsNeutral);

            var aggregated = new AggregatedSignal
            {
                Pair = pair,
                OverallRating = ConvertToSignalRating(overallRating.Value),
                BuySignalCount = buyCount,
                SellSignalCount = sellCount,
                NeutralSignalCount = neutralCount,
                IndividualSignals = individualSignals,
                Timestamp = DateTimeOffset.UtcNow
            };

            return Task.FromResult(Result<AggregatedSignal>.Success(aggregated));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<AggregatedSignal>.Failure(
                Error.ExchangeError($"Failed to get aggregated signal for {pair.Symbol}: {ex.Message}")));
        }
    }

    /// <inheritdoc />
    public IObservable<TradingSignal> SubscribeToSignals(TradingPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        return _signalSubject
            .Where(s => s.Pair.Symbol == pair.Symbol)
            .AsObservable();
    }

    /// <inheritdoc />
    public IObservable<TradingSignal> SubscribeToAllSignals()
    {
        return _signalSubject.AsObservable();
    }

    /// <inheritdoc />
    public Task<Result<bool>> TestConnectivityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get signal names as a connectivity test
            var signalNames = _legacySignals.GetSignalNames();
            return Task.FromResult(Result<bool>.Success(signalNames.Any()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<bool>.Failure(
                Error.ExchangeError($"Failed to connect to signal provider: {ex.Message}")));
        }
    }

    /// <summary>
    /// Publishes a signal update to subscribers.
    /// This can be called by external components when signals are updated.
    /// </summary>
    public void PublishSignalUpdate(TradingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _signalSubject.OnNext(signal);
    }

    /// <summary>
    /// Gets all available signal names from the provider.
    /// </summary>
    public IEnumerable<string> GetAvailableSignalNames()
    {
        return _legacySignals.GetSignalNames();
    }

    /// <summary>
    /// Gets the global rating across all signals.
    /// </summary>
    public double? GetGlobalRating()
    {
        return _legacySignals.GetGlobalRating();
    }

    private TradingSignal ConvertToTradingSignal(ISignal legacySignal, TradingPair pair)
    {
        var rating = ConvertToSignalRating(legacySignal.Rating ?? 0);
        var signalType = DetermineSignalType(legacySignal.Name);

        return new TradingSignal
        {
            SignalName = legacySignal.Name,
            Pair = pair,
            Rating = rating,
            Type = signalType,
            ProviderName = ProviderName,
            Timestamp = DateTimeOffset.UtcNow,
            Description = BuildSignalDescription(legacySignal),
            Metadata = BuildSignalMetadata(legacySignal)
        };
    }

    private static SignalRating ConvertToSignalRating(double rating)
    {
        // Legacy ratings are typically in the range -1 to 1
        // Clamp to ensure valid range
        return SignalRating.Create(rating, clamp: true);
    }

    private static SignalType DetermineSignalType(string signalName)
    {
        var name = signalName.ToUpperInvariant();

        // Check summary/recommendation first (highest priority)
        if (name.Contains("RECOMMEND") || name.Contains("SUMMARY"))
        {
            return SignalType.Summary;
        }

        // Check trend indicators before moving averages (MACD contains "MA")
        if (name.Contains("MACD") || name.Contains("ADX") || name.Contains("AROON") ||
            name.Contains("PSAR") || name.Contains("BBP"))
        {
            return SignalType.Trend;
        }

        // Check volume indicators
        if (name.Contains("VOLUME") || name.Contains("OBV") || name.Contains("MFI"))
        {
            return SignalType.Volume;
        }

        // Check oscillators
        if (name.Contains("RSI") || name.Contains("STOCH") || name.Contains("CCI") ||
            name.Contains("ATR") || name.Contains("HIGHLOW") || name.Contains("UO") ||
            name.Contains("WILLIAMS"))
        {
            return SignalType.Oscillator;
        }

        // Check moving averages last (since other indicators may contain "MA")
        if (name.Contains("SMA") || name.Contains("EMA") || name.Contains("MA") ||
            name.Contains("ICHIMOKU") || name.Contains("VWMA") || name.Contains("HMA"))
        {
            return SignalType.MovingAverage;
        }

        return SignalType.Technical;
    }

    private static string? BuildSignalDescription(ISignal signal)
    {
        var parts = new List<string>();

        if (signal.Price.HasValue)
        {
            parts.Add($"Price: {signal.Price:F8}");
        }

        if (signal.PriceChange.HasValue)
        {
            parts.Add($"Change: {signal.PriceChange:F2}%");
        }

        if (signal.Volatility.HasValue)
        {
            parts.Add($"Volatility: {signal.Volatility:F2}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static IReadOnlyDictionary<string, object>? BuildSignalMetadata(ISignal signal)
    {
        var metadata = new Dictionary<string, object>();

        if (signal.Price.HasValue)
        {
            metadata["price"] = signal.Price.Value;
        }

        if (signal.PriceChange.HasValue)
        {
            metadata["priceChange"] = signal.PriceChange.Value;
        }

        if (signal.Volume.HasValue)
        {
            metadata["volume"] = signal.Volume.Value;
        }

        if (signal.VolumeChange.HasValue)
        {
            metadata["volumeChange"] = signal.VolumeChange.Value;
        }

        if (signal.Volatility.HasValue)
        {
            metadata["volatility"] = signal.Volatility.Value;
        }

        if (signal.RatingChange.HasValue)
        {
            metadata["ratingChange"] = signal.RatingChange.Value;
        }

        return metadata.Count > 0 ? metadata : null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _signalSubject.OnCompleted();
        _signalSubject.Dispose();
        _disposed = true;
    }
}
