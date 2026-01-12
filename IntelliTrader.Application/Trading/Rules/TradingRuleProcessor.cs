using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Domain.Rules;
using IntelliTrader.Domain.Rules.Services;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Rules;

/// <summary>
/// Processes trading rules for open positions and determines required actions.
/// This extracts the rule processing logic from the legacy TradingRulesTimedTask.
/// </summary>
public sealed class TradingRuleProcessor
{
    private readonly IPositionRepository _positionRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IExchangePort _exchangePort;
    private readonly ISignalProviderPort? _signalProvider;
    private readonly RuleEvaluator _ruleEvaluator;

    public TradingRuleProcessor(
        IPositionRepository positionRepository,
        IPortfolioRepository portfolioRepository,
        IExchangePort exchangePort,
        RuleEvaluator ruleEvaluator,
        ISignalProviderPort? signalProvider = null)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _portfolioRepository = portfolioRepository ?? throw new ArgumentNullException(nameof(portfolioRepository));
        _exchangePort = exchangePort ?? throw new ArgumentNullException(nameof(exchangePort));
        _ruleEvaluator = ruleEvaluator ?? throw new ArgumentNullException(nameof(ruleEvaluator));
        _signalProvider = signalProvider;
    }

    /// <summary>
    /// Processes trading rules for all open positions.
    /// </summary>
    public async Task<Result<IReadOnlyList<TradingRuleProcessingResult>>> ProcessAllPositionsAsync(
        TradingRuleConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enabled)
        {
            return Result<IReadOnlyList<TradingRuleProcessingResult>>.Success(
                Array.Empty<TradingRuleProcessingResult>());
        }

        // Get portfolio to find all open positions
        var portfolio = await _portfolioRepository.GetDefaultAsync(cancellationToken);
        if (portfolio is null)
        {
            return Result<IReadOnlyList<TradingRuleProcessingResult>>.Failure(
                Error.NotFound("Portfolio", "default"));
        }

        var activePairs = portfolio.GetActivePairs();
        if (activePairs.Count == 0)
        {
            return Result<IReadOnlyList<TradingRuleProcessingResult>>.Success(
                Array.Empty<TradingRuleProcessingResult>());
        }

        // Get current prices for all active pairs
        var pricesResult = await _exchangePort.GetCurrentPricesAsync(activePairs, cancellationToken);
        if (pricesResult.IsFailure)
        {
            return Result<IReadOnlyList<TradingRuleProcessingResult>>.Failure(pricesResult.Error);
        }

        var prices = pricesResult.Value;

        // Process each position
        var results = new List<TradingRuleProcessingResult>();
        foreach (var pair in activePairs)
        {
            var positionId = portfolio.GetPositionId(pair);
            if (positionId is null) continue;

            var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
            if (position is null || position.IsClosed) continue;

            if (!prices.TryGetValue(pair, out var currentPrice))
            {
                continue; // Skip if price not available
            }

            var result = await ProcessPositionAsync(position, currentPrice, config, cancellationToken);
            results.Add(result);
        }

        return Result<IReadOnlyList<TradingRuleProcessingResult>>.Success(results);
    }

    /// <summary>
    /// Processes trading rules for a specific position.
    /// </summary>
    public async Task<TradingRuleProcessingResult> ProcessPositionAsync(
        Position position,
        Price currentPrice,
        TradingRuleConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(currentPrice);
        ArgumentNullException.ThrowIfNull(config);

        var currentMargin = position.CalculateMargin(currentPrice);

        // Build the evaluation context
        var context = await BuildEvaluationContextAsync(position, currentPrice, cancellationToken);

        // Check for stop-loss first (highest priority)
        if (config.StopLossEnabled && ShouldTriggerStopLoss(position, currentMargin, config))
        {
            return new TradingRuleProcessingResult
            {
                PositionId = position.Id,
                Pair = position.Pair,
                CurrentMargin = currentMargin,
                CurrentPrice = currentPrice,
                RecommendedAction = TradingRuleAction.StopLoss,
                MatchReason = $"Stop-loss triggered at {currentMargin.Percentage:F2}% (threshold: {config.DefaultStopLossMargin:F2}%)"
            };
        }

        // Check for take-profit (target margin reached)
        if (currentMargin.Percentage >= config.DefaultSellMargin)
        {
            return new TradingRuleProcessingResult
            {
                PositionId = position.Id,
                Pair = position.Pair,
                CurrentMargin = currentMargin,
                CurrentPrice = currentPrice,
                RecommendedAction = TradingRuleAction.TakeProfit,
                MatchReason = $"Take-profit target reached at {currentMargin.Percentage:F2}% (target: {config.DefaultSellMargin:F2}%)"
            };
        }

        // Evaluate custom rules
        var enabledRules = config.Rules
            .Where(r => r.Enabled)
            .OrderBy(r => r.Priority)
            .ToList();

        TradingRule? matchedRule = null;

        foreach (var rule in enabledRules)
        {
            if (_ruleEvaluator.Evaluate(rule.Conditions, context))
            {
                matchedRule = rule;

                if (config.ProcessingMode == RuleProcessingMode.FirstMatch)
                {
                    break;
                }
            }
        }

        if (matchedRule != null)
        {
            // Check if DCA is allowed
            if (matchedRule.Action == TradingRuleAction.DCA)
            {
                if (!config.DCAEnabled || position.DCALevel >= config.MaxDCALevels)
                {
                    // DCA not allowed, return no match
                    return new TradingRuleProcessingResult
                    {
                        PositionId = position.Id,
                        Pair = position.Pair,
                        CurrentMargin = currentMargin,
                        CurrentPrice = currentPrice,
                        MatchReason = $"DCA rule matched but DCA not allowed (level: {position.DCALevel}, max: {config.MaxDCALevels})"
                    };
                }
            }

            return new TradingRuleProcessingResult
            {
                PositionId = position.Id,
                Pair = position.Pair,
                MatchedRule = matchedRule,
                RecommendedAction = matchedRule.Action,
                CurrentMargin = currentMargin,
                CurrentPrice = currentPrice,
                MatchReason = $"Rule '{matchedRule.Name}' matched with action {matchedRule.Action}"
            };
        }

        // No rules matched
        return new TradingRuleProcessingResult
        {
            PositionId = position.Id,
            Pair = position.Pair,
            CurrentMargin = currentMargin,
            CurrentPrice = currentPrice
        };
    }

    /// <summary>
    /// Gets positions that require action based on rule processing results.
    /// </summary>
    public IReadOnlyList<TradingRuleProcessingResult> GetPositionsRequiringAction(
        IReadOnlyList<TradingRuleProcessingResult> results)
    {
        return results.Where(r => r.HasMatch || r.RecommendedAction != null).ToList();
    }

    /// <summary>
    /// Creates a ClosePositionCommand from a rule processing result.
    /// </summary>
    public ClosePositionCommand? CreateCloseCommand(TradingRuleProcessingResult result)
    {
        if (result.RecommendedAction is not (TradingRuleAction.Sell or TradingRuleAction.StopLoss or TradingRuleAction.TakeProfit))
        {
            return null;
        }

        var reason = result.RecommendedAction switch
        {
            TradingRuleAction.StopLoss => CloseReason.StopLoss,
            TradingRuleAction.TakeProfit => CloseReason.TakeProfit,
            TradingRuleAction.Sell => CloseReason.SignalRule,
            _ => CloseReason.Manual
        };

        return new ClosePositionCommand
        {
            PositionId = result.PositionId,
            Reason = reason
        };
    }

    /// <summary>
    /// Creates an ExecuteDCACommand from a rule processing result.
    /// </summary>
    public ExecuteDCACommand? CreateDCACommand(TradingRuleProcessingResult result, Money defaultDCACost)
    {
        if (result.RecommendedAction != TradingRuleAction.DCA)
        {
            return null;
        }

        var cost = result.MatchedRule?.DCACost ?? defaultDCACost;

        return new ExecuteDCACommand
        {
            PositionId = result.PositionId,
            Cost = cost
        };
    }

    private bool ShouldTriggerStopLoss(Position position, Margin currentMargin, TradingRuleConfig config)
    {
        // Check if margin is below stop-loss threshold
        if (currentMargin.Percentage > config.DefaultStopLossMargin)
        {
            return false;
        }

        // Check minimum age requirement
        var ageSeconds = position.Age.TotalSeconds;
        if (ageSeconds < config.StopLossMinAge)
        {
            return false;
        }

        return true;
    }

    private async Task<RuleEvaluationContext> BuildEvaluationContextAsync(
        Position position,
        Price currentPrice,
        CancellationToken cancellationToken)
    {
        var signals = new Dictionary<string, SignalSnapshot>();

        // Try to get signals if provider is available
        if (_signalProvider != null)
        {
            var signalsResult = await _signalProvider.GetAllSignalsAsync(position.Pair, cancellationToken);
            if (signalsResult.IsSuccess)
            {
                foreach (var signal in signalsResult.Value)
                {
                    signals[signal.SignalName] = new SignalSnapshot
                    {
                        Name = signal.SignalName,
                        Pair = position.Pair.Symbol,
                        Rating = signal.Rating.Value
                    };
                }
            }
        }

        // Calculate margin change if we have last buy margin
        decimal? marginChange = null;
        var currentMargin = position.CalculateMargin(currentPrice);
        var lastBuyMargin = position.GetLastBuyMargin(currentPrice);
        if (lastBuyMargin != null)
        {
            marginChange = currentMargin.Percentage - lastBuyMargin.Percentage;
        }

        return new RuleEvaluationContext
        {
            Pair = position.Pair.Symbol,
            Signals = signals,
            Position = new PositionSnapshot
            {
                Pair = position.Pair.Symbol,
                CurrentAge = position.Age,
                LastBuyAge = position.TimeSinceLastBuy,
                CurrentMargin = currentMargin.Percentage,
                LastBuyMargin = lastBuyMargin?.Percentage,
                TotalAmount = position.TotalQuantity.Value,
                CurrentCost = position.TotalCost.Amount,
                DCALevel = position.DCALevel,
                SignalRule = position.SignalRule
            }
        };
    }
}
