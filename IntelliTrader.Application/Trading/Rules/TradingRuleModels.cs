using IntelliTrader.Domain.Rules.Services;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Rules;

/// <summary>
/// Represents a configured trading rule with conditions and actions.
/// </summary>
public sealed record TradingRule
{
    /// <summary>
    /// Unique name/identifier for the rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether this rule is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The type of action to take when the rule matches.
    /// </summary>
    public required TradingRuleAction Action { get; init; }

    /// <summary>
    /// The conditions that must be satisfied for this rule to trigger.
    /// </summary>
    public required IReadOnlyList<RuleCondition> Conditions { get; init; }

    /// <summary>
    /// Priority of this rule (lower = higher priority).
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Optional target margin for sell actions.
    /// </summary>
    public decimal? TargetMargin { get; init; }

    /// <summary>
    /// Optional cost multiplier for DCA actions.
    /// </summary>
    public decimal? DCAMultiplier { get; init; }

    /// <summary>
    /// Optional fixed cost for DCA actions.
    /// </summary>
    public Money? DCACost { get; init; }
}

/// <summary>
/// The action to take when a trading rule matches.
/// </summary>
public enum TradingRuleAction
{
    /// <summary>
    /// Close the position (sell).
    /// </summary>
    Sell,

    /// <summary>
    /// Execute a DCA (Dollar Cost Average) buy.
    /// </summary>
    DCA,

    /// <summary>
    /// Close the position due to stop-loss trigger.
    /// </summary>
    StopLoss,

    /// <summary>
    /// Close the position due to take-profit trigger.
    /// </summary>
    TakeProfit,

    /// <summary>
    /// No action - just log/alert.
    /// </summary>
    Alert
}

/// <summary>
/// Result of processing trading rules for a position.
/// </summary>
public sealed record TradingRuleProcessingResult
{
    /// <summary>
    /// The position ID that was evaluated.
    /// </summary>
    public required PositionId PositionId { get; init; }

    /// <summary>
    /// The trading pair.
    /// </summary>
    public required TradingPair Pair { get; init; }

    /// <summary>
    /// The matched rule (if any).
    /// </summary>
    public TradingRule? MatchedRule { get; init; }

    /// <summary>
    /// The recommended action.
    /// </summary>
    public TradingRuleAction? RecommendedAction { get; init; }

    /// <summary>
    /// The current margin of the position.
    /// </summary>
    public required Margin CurrentMargin { get; init; }

    /// <summary>
    /// The current price.
    /// </summary>
    public required Price CurrentPrice { get; init; }

    /// <summary>
    /// Whether any rule matched for this position.
    /// </summary>
    public bool HasMatch => MatchedRule != null;

    /// <summary>
    /// Additional context about the match.
    /// </summary>
    public string? MatchReason { get; init; }

    /// <summary>
    /// The timestamp of the evaluation.
    /// </summary>
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Configuration for trading rule processing.
/// </summary>
public sealed record TradingRuleConfig
{
    /// <summary>
    /// Whether trading rule processing is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How to process rules when multiple match.
    /// </summary>
    public RuleProcessingMode ProcessingMode { get; init; } = RuleProcessingMode.FirstMatch;

    /// <summary>
    /// The configured trading rules.
    /// </summary>
    public IReadOnlyList<TradingRule> Rules { get; init; } = Array.Empty<TradingRule>();

    /// <summary>
    /// Default sell margin target.
    /// </summary>
    public decimal DefaultSellMargin { get; init; } = 1.5m;

    /// <summary>
    /// Default stop-loss margin.
    /// </summary>
    public decimal DefaultStopLossMargin { get; init; } = -10m;

    /// <summary>
    /// Whether stop-loss is enabled.
    /// </summary>
    public bool StopLossEnabled { get; init; } = true;

    /// <summary>
    /// Minimum position age before stop-loss can trigger (in seconds).
    /// </summary>
    public double StopLossMinAge { get; init; } = 300;

    /// <summary>
    /// Whether DCA is enabled globally.
    /// </summary>
    public bool DCAEnabled { get; init; } = true;

    /// <summary>
    /// Maximum DCA levels allowed.
    /// </summary>
    public int MaxDCALevels { get; init; } = 5;
}

/// <summary>
/// Mode for processing multiple matching rules.
/// </summary>
public enum RuleProcessingMode
{
    /// <summary>
    /// Stop at the first matching rule.
    /// </summary>
    FirstMatch,

    /// <summary>
    /// Process all matching rules (last one wins).
    /// </summary>
    AllMatches,

    /// <summary>
    /// Use the highest priority matching rule.
    /// </summary>
    HighestPriority
}
