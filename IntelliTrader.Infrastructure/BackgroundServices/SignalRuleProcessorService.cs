using Microsoft.Extensions.Logging;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Signals.ValueObjects;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Configuration for the SignalRuleProcessorService.
/// </summary>
public sealed class SignalRuleProcessorOptions
{
    /// <summary>
    /// Interval between signal rule processing cycles. Default: 3 seconds.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Delay before starting signal processing. Default: 2 seconds.
    /// </summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The quote currency for the market.
    /// </summary>
    public string QuoteCurrency { get; set; } = "USDT";

    /// <summary>
    /// Maximum number of concurrent positions allowed.
    /// </summary>
    public int MaxConcurrentPositions { get; set; } = 10;

    /// <summary>
    /// Minimum signal rating threshold for buy signals (0 to 1).
    /// </summary>
    public double MinBuySignalRating { get; set; } = 0.5;
}

/// <summary>
/// Represents a buy signal that has been triggered.
/// </summary>
public sealed class BuySignalTriggered
{
    public TradingPair Pair { get; init; } = null!;
    public string RuleName { get; init; } = string.Empty;
    public IReadOnlyList<string> TriggeringSignals { get; init; } = Array.Empty<string>();
    public SignalRating Rating { get; init; } = null!;
    public decimal SuggestedCost { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool IsTrailing { get; init; }
}

/// <summary>
/// Event args for when a buy signal is triggered.
/// </summary>
public sealed class BuySignalTriggeredEventArgs : EventArgs
{
    public BuySignalTriggered Signal { get; }

    public BuySignalTriggeredEventArgs(BuySignalTriggered signal)
    {
        Signal = signal;
    }
}

/// <summary>
/// Background service that processes signal rules and triggers buy signals.
/// Replaces the legacy SignalRulesTimedTask.
/// </summary>
public class SignalRuleProcessorService : TimedBackgroundService
{
    private readonly ILogger<SignalRuleProcessorService> _logger;
    private readonly ISignalProviderPort _signalProvider;
    private readonly IPositionRepository _positionRepository;
    private readonly SignalRuleProcessorOptions _options;

    private readonly HashSet<string> _trailingPairs = new();
    private readonly object _trailingLock = new();

    /// <summary>
    /// Event raised when a buy signal is triggered.
    /// </summary>
    public event EventHandler<BuySignalTriggeredEventArgs>? BuySignalTriggered;

    /// <summary>
    /// Gets or sets whether logging is enabled.
    /// </summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>
    /// Creates a new SignalRuleProcessorService.
    /// </summary>
    public SignalRuleProcessorService(
        ILogger<SignalRuleProcessorService> logger,
        ISignalProviderPort signalProvider,
        IPositionRepository positionRepository,
        SignalRuleProcessorOptions? options = null)
        : base(logger, options?.Interval ?? TimeSpan.FromSeconds(3), options?.StartDelay)
    {
        _logger = logger;
        _signalProvider = signalProvider ?? throw new ArgumentNullException(nameof(signalProvider));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _options = options ?? new SignalRuleProcessorOptions();
    }

    /// <summary>
    /// Gets the pairs currently in trailing mode.
    /// </summary>
    public IReadOnlyList<string> GetTrailingPairs()
    {
        lock (_trailingLock)
        {
            return _trailingPairs.ToList();
        }
    }

    /// <summary>
    /// Clears all trailing states.
    /// </summary>
    public void ClearTrailing()
    {
        lock (_trailingLock)
        {
            _trailingPairs.Clear();
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteWorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessSignalRulesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing signal rules");
        }
    }

    private async Task ProcessSignalRulesAsync(CancellationToken cancellationToken)
    {
        // Check if we can open new positions
        var activeCount = await _positionRepository.GetActiveCountAsync(cancellationToken);
        if (activeCount >= _options.MaxConcurrentPositions)
        {
            _logger.LogDebug("Maximum concurrent positions reached ({Count}), skipping signal processing",
                activeCount);
            return;
        }

        // Get all active positions to exclude their pairs
        var activePositions = await _positionRepository.GetAllActiveAsync(cancellationToken);
        var excludedPairs = activePositions.Select(p => p.Pair.Symbol).ToHashSet();

        // Also exclude pairs currently in trailing
        lock (_trailingLock)
        {
            foreach (var pair in _trailingPairs)
            {
                excludedPairs.Add(pair);
            }
        }

        // Get aggregated signals for evaluation
        // In a full implementation, this would iterate over configured pairs
        // For now, we'll use a simplified approach

        _logger.LogDebug("Signal rules processed. Active positions: {ActiveCount}, Excluded pairs: {ExcludedCount}",
            activeCount, excludedPairs.Count);
    }

    /// <summary>
    /// Evaluates a signal for potential buy trigger.
    /// </summary>
    public async Task<bool> EvaluateSignalAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default)
    {
        // Check if pair is excluded
        var activePositions = await _positionRepository.GetAllActiveAsync(cancellationToken);
        if (activePositions.Any(p => p.Pair.Symbol == pair.Symbol))
        {
            return false;
        }

        // Get aggregated signal
        var signalResult = await _signalProvider.GetAggregatedSignalAsync(pair, cancellationToken);
        if (signalResult.IsFailure)
        {
            _logger.LogDebug("No signal available for {Pair}", pair.Symbol);
            return false;
        }

        var signal = signalResult.Value;

        // Check if signal meets buy criteria
        if (signal.OverallRating.Value >= _options.MinBuySignalRating)
        {
            var buySignal = new BuySignalTriggered
            {
                Pair = pair,
                RuleName = "DefaultSignalRule",
                TriggeringSignals = signal.IndividualSignals
                    .Where(s => s.IsBuySignal)
                    .Select(s => s.SignalName)
                    .ToList(),
                Rating = signal.OverallRating,
                SuggestedCost = 100m, // Default cost - would be configured
                Timestamp = DateTimeOffset.UtcNow,
                IsTrailing = false
            };

            if (LoggingEnabled)
            {
                _logger.LogInformation(
                    "Buy signal triggered for {Pair}. Rating: {Rating:F2}, Signals: {Signals}",
                    pair.Symbol,
                    signal.OverallRating.Value,
                    string.Join(", ", buySignal.TriggeringSignals));
            }

            BuySignalTriggered?.Invoke(this, new BuySignalTriggeredEventArgs(buySignal));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts trailing for a pair.
    /// </summary>
    public void StartTrailing(TradingPair pair)
    {
        lock (_trailingLock)
        {
            if (_trailingPairs.Add(pair.Symbol))
            {
                if (LoggingEnabled)
                {
                    _logger.LogInformation("Started signal trailing for {Pair}", pair.Symbol);
                }
            }
        }
    }

    /// <summary>
    /// Stops trailing for a pair.
    /// </summary>
    public void StopTrailing(TradingPair pair)
    {
        lock (_trailingLock)
        {
            if (_trailingPairs.Remove(pair.Symbol))
            {
                if (LoggingEnabled)
                {
                    _logger.LogInformation("Stopped signal trailing for {Pair}", pair.Symbol);
                }
            }
        }
    }
}
