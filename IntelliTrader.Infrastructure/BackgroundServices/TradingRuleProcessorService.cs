using Microsoft.Extensions.Logging;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Configuration for the TradingRuleProcessorService.
/// </summary>
public sealed class TradingRuleProcessorOptions
{
    /// <summary>
    /// Interval between rule processing cycles. Default: 3 seconds.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Delay before starting rule processing. Default: 1 second.
    /// </summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The quote currency for the market.
    /// </summary>
    public string QuoteCurrency { get; set; } = "USDT";
}

/// <summary>
/// Processed pair configuration containing trading parameters.
/// </summary>
public sealed class PairTradingConfig
{
    public TradingPair Pair { get; init; } = null!;
    public IReadOnlyList<string> AppliedRules { get; init; } = Array.Empty<string>();

    // Buy configuration
    public bool BuyEnabled { get; init; }
    public decimal BuyMaxCost { get; init; }
    public decimal BuyMultiplier { get; init; } = 1m;
    public decimal BuyMinBalance { get; init; }
    public TimeSpan BuySamePairTimeout { get; init; }
    public decimal BuyTrailing { get; init; }
    public decimal BuyTrailingStopMargin { get; init; }

    // Sell configuration
    public bool SellEnabled { get; init; }
    public decimal SellMargin { get; init; }
    public decimal SellTrailing { get; init; }
    public decimal SellTrailingStopMargin { get; init; }
    public bool SellStopLossEnabled { get; init; }
    public decimal SellStopLossMargin { get; init; }
    public TimeSpan SellStopLossMinAge { get; init; }

    // DCA configuration
    public bool DCAEnabled { get; init; }
    public int CurrentDCALevel { get; init; }
    public decimal? CurrentDCAMargin { get; init; }
    public decimal? NextDCAMargin { get; init; }
}

/// <summary>
/// Event raised when pair configurations are updated.
/// </summary>
public sealed class PairConfigsUpdatedEventArgs : EventArgs
{
    public IReadOnlyDictionary<TradingPair, PairTradingConfig> Configurations { get; }
    public DateTimeOffset Timestamp { get; }

    public PairConfigsUpdatedEventArgs(IReadOnlyDictionary<TradingPair, PairTradingConfig> configurations)
    {
        Configurations = configurations;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Background service that processes trading rules and generates pair configurations.
/// Replaces the legacy TradingRulesTimedTask.
/// </summary>
public class TradingRuleProcessorService : TimedBackgroundService
{
    private readonly ILogger<TradingRuleProcessorService> _logger;
    private readonly ISignalProviderPort _signalProvider;
    private readonly IExchangePort _exchangePort;
    private readonly IPositionRepository _positionRepository;
    private readonly TradingRuleProcessorOptions _options;

    private readonly object _configLock = new();
    private Dictionary<TradingPair, PairTradingConfig> _pairConfigs = new();

    /// <summary>
    /// Event raised when pair configurations are updated.
    /// </summary>
    public event EventHandler<PairConfigsUpdatedEventArgs>? ConfigurationsUpdated;

    /// <summary>
    /// Creates a new TradingRuleProcessorService.
    /// </summary>
    public TradingRuleProcessorService(
        ILogger<TradingRuleProcessorService> logger,
        ISignalProviderPort signalProvider,
        IExchangePort exchangePort,
        IPositionRepository positionRepository,
        TradingRuleProcessorOptions? options = null)
        : base(logger, options?.Interval ?? TimeSpan.FromSeconds(3), options?.StartDelay)
    {
        _logger = logger;
        _signalProvider = signalProvider ?? throw new ArgumentNullException(nameof(signalProvider));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _options = options ?? new TradingRuleProcessorOptions();
    }

    /// <summary>
    /// Gets the current trading configuration for a pair.
    /// </summary>
    public PairTradingConfig? GetPairConfig(TradingPair pair)
    {
        lock (_configLock)
        {
            return _pairConfigs.TryGetValue(pair, out var config) ? config : null;
        }
    }

    /// <summary>
    /// Gets all current pair configurations.
    /// </summary>
    public IReadOnlyDictionary<TradingPair, PairTradingConfig> GetAllPairConfigs()
    {
        lock (_configLock)
        {
            return new Dictionary<TradingPair, PairTradingConfig>(_pairConfigs);
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteWorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessRulesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing trading rules");
        }
    }

    private async Task ProcessRulesAsync(CancellationToken cancellationToken)
    {
        // Get all active positions
        var positions = await _positionRepository.GetAllActiveAsync(cancellationToken);
        var positionsByPair = positions.ToDictionary(p => p.Pair);

        // Get current prices for all pairs with positions
        var newConfigs = new Dictionary<TradingPair, PairTradingConfig>();

        foreach (var position in positions)
        {
            var priceResult = await _exchangePort.GetCurrentPriceAsync(position.Pair, cancellationToken);
            if (priceResult.IsFailure)
            {
                _logger.LogWarning("Failed to get price for {Pair}: {Error}",
                    position.Pair.Symbol, priceResult.Error.Message);
                continue;
            }

            var currentPrice = priceResult.Value;
            var currentMargin = position.CalculateMargin(currentPrice);

            // Get aggregated signal for the pair
            var signalResult = await _signalProvider.GetAggregatedSignalAsync(position.Pair, cancellationToken);

            // Create pair configuration based on position state and signals
            var config = CreatePairConfig(position.Pair, position, currentMargin.Percentage, signalResult.IsSuccess ? signalResult.Value : null);
            newConfigs[position.Pair] = config;
        }

        // Update configurations atomically
        lock (_configLock)
        {
            _pairConfigs = newConfigs;
        }

        // Raise event
        ConfigurationsUpdated?.Invoke(this, new PairConfigsUpdatedEventArgs(newConfigs));

        _logger.LogDebug("Processed trading rules for {PairCount} pairs", newConfigs.Count);
    }

    private PairTradingConfig CreatePairConfig(
        TradingPair pair,
        Domain.Trading.Aggregates.Position position,
        decimal currentMargin,
        AggregatedSignal? signal)
    {
        // Default configuration - in production this would be driven by rules
        // This is a simplified version showing the structure
        var dcaLevel = position.DCALevel;

        return new PairTradingConfig
        {
            Pair = pair,
            AppliedRules = Array.Empty<string>(),

            // Buy configuration
            BuyEnabled = dcaLevel < 5, // Allow up to 5 DCA levels
            BuyMaxCost = 100m,
            BuyMultiplier = 1m + (dcaLevel * 0.5m), // Increase with DCA level
            BuyMinBalance = 10m,
            BuySamePairTimeout = TimeSpan.FromMinutes(30),
            BuyTrailing = 0.5m,
            BuyTrailingStopMargin = 2m,

            // Sell configuration
            SellEnabled = true,
            SellMargin = dcaLevel > 0 ? 1m : 2m, // Lower target after DCA
            SellTrailing = 0.5m,
            SellTrailingStopMargin = 0.1m,
            SellStopLossEnabled = true,
            SellStopLossMargin = -10m,
            SellStopLossMinAge = TimeSpan.FromHours(24),

            // DCA configuration
            DCAEnabled = true,
            CurrentDCALevel = dcaLevel,
            CurrentDCAMargin = GetDCAMarginForLevel(dcaLevel),
            NextDCAMargin = GetDCAMarginForLevel(dcaLevel + 1)
        };
    }

    private static decimal? GetDCAMarginForLevel(int level)
    {
        // Standard DCA levels - could be configured
        return level switch
        {
            1 => -5m,
            2 => -10m,
            3 => -15m,
            4 => -20m,
            5 => -25m,
            _ => null
        };
    }
}
