using IntelliTrader.Domain.Rules.Specifications;
using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Rules.Services;

/// <summary>
/// Evaluates trading rules using the Specification pattern.
/// Replaces the monolithic RulesService.CheckConditions() method.
/// </summary>
public class RuleEvaluator
{
    /// <summary>
    /// Evaluates if all conditions are satisfied by the given context.
    /// </summary>
    public bool Evaluate(IEnumerable<RuleCondition> conditions, RuleEvaluationContext context)
    {
        var specification = BuildCompositeSpecification(conditions);
        return specification.IsSatisfiedBy(context);
    }

    /// <summary>
    /// Builds a composite specification from a list of conditions.
    /// All conditions must be satisfied (AND logic).
    /// </summary>
    public Specification<RuleEvaluationContext> BuildCompositeSpecification(IEnumerable<RuleCondition> conditions)
    {
        Specification<RuleEvaluationContext>? composite = null;

        foreach (var condition in conditions)
        {
            var conditionSpec = BuildConditionSpecification(condition);
            composite = composite is null ? conditionSpec : composite.And(conditionSpec);
        }

        return composite ?? new TrueSpecification<RuleEvaluationContext>();
    }

    /// <summary>
    /// Builds a composite specification from a single condition.
    /// A condition may have multiple constraints (all must be satisfied).
    /// </summary>
    public Specification<RuleEvaluationContext> BuildConditionSpecification(RuleCondition condition)
    {
        var specs = new List<Specification<RuleEvaluationContext>>();

        // Signal-based specifications
        AddIfNotNull(specs, condition.MinVolume, v => new MinVolumeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxVolume, v => new MaxVolumeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MinVolumeChange, v => new MinVolumeChangeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxVolumeChange, v => new MaxVolumeChangeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MinPrice, v => new MinPriceSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxPrice, v => new MaxPriceSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MinPriceChange, v => new MinPriceChangeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxPriceChange, v => new MaxPriceChangeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MinRating, v => new MinRatingSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxRating, v => new MaxRatingSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MinRatingChange, v => new MinRatingChangeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxRatingChange, v => new MaxRatingChangeSpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MinVolatility, v => new MinVolatilitySpecification(condition.Signal, v));
        AddIfNotNull(specs, condition.MaxVolatility, v => new MaxVolatilitySpecification(condition.Signal, v));

        // Global specifications
        AddIfNotNull(specs, condition.MinGlobalRating, v => new MinGlobalRatingSpecification(v));
        AddIfNotNull(specs, condition.MaxGlobalRating, v => new MaxGlobalRatingSpecification(v));

        // Pair filter
        if (condition.Pairs is { Count: > 0 })
        {
            specs.Add(new PairWhitelistSpecification(condition.Pairs));
        }

        // Position-based specifications
        AddIfNotNull(specs, condition.MinAge, v => new MinAgeSpecification(v));
        AddIfNotNull(specs, condition.MaxAge, v => new MaxAgeSpecification(v));
        AddIfNotNull(specs, condition.MinLastBuyAge, v => new MinLastBuyAgeSpecification(v));
        AddIfNotNull(specs, condition.MaxLastBuyAge, v => new MaxLastBuyAgeSpecification(v));
        AddIfNotNull(specs, condition.MinMargin, v => new MinMarginSpecification(v));
        AddIfNotNull(specs, condition.MaxMargin, v => new MaxMarginSpecification(v));
        AddIfNotNull(specs, condition.MinMarginChange, v => new MinMarginChangeSpecification(v));
        AddIfNotNull(specs, condition.MaxMarginChange, v => new MaxMarginChangeSpecification(v));
        AddIfNotNull(specs, condition.MinAmount, v => new MinAmountSpecification(v));
        AddIfNotNull(specs, condition.MaxAmount, v => new MaxAmountSpecification(v));
        AddIfNotNull(specs, condition.MinCost, v => new MinCostSpecification(v));
        AddIfNotNull(specs, condition.MaxCost, v => new MaxCostSpecification(v));
        AddIfNotNull(specs, condition.MinDCALevel, v => new MinDCALevelSpecification(v));
        AddIfNotNull(specs, condition.MaxDCALevel, v => new MaxDCALevelSpecification(v));

        // Signal rules filter
        if (condition.SignalRules is { Count: > 0 })
        {
            specs.Add(new SignalRulesSpecification(condition.SignalRules));
        }

        // Combine all specifications with AND logic
        if (specs.Count == 0)
        {
            return new TrueSpecification<RuleEvaluationContext>();
        }

        var result = specs[0];
        for (var i = 1; i < specs.Count; i++)
        {
            result = result.And(specs[i]);
        }

        return result;
    }

    private static void AddIfNotNull<T>(
        List<Specification<RuleEvaluationContext>> specs,
        T? value,
        Func<T, Specification<RuleEvaluationContext>> factory) where T : struct
    {
        if (value.HasValue)
        {
            specs.Add(factory(value.Value));
        }
    }
}

/// <summary>
/// Represents a rule condition for evaluation.
/// This is a domain model that maps from the legacy IRuleCondition.
/// </summary>
public record RuleCondition
{
    public string? Signal { get; init; }

    // Volume conditions
    public long? MinVolume { get; init; }
    public long? MaxVolume { get; init; }
    public double? MinVolumeChange { get; init; }
    public double? MaxVolumeChange { get; init; }

    // Price conditions
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public decimal? MinPriceChange { get; init; }
    public decimal? MaxPriceChange { get; init; }

    // Rating conditions
    public double? MinRating { get; init; }
    public double? MaxRating { get; init; }
    public double? MinRatingChange { get; init; }
    public double? MaxRatingChange { get; init; }

    // Volatility conditions
    public double? MinVolatility { get; init; }
    public double? MaxVolatility { get; init; }

    // Global conditions
    public double? MinGlobalRating { get; init; }
    public double? MaxGlobalRating { get; init; }

    // Pair filters
    public List<string>? Pairs { get; init; }

    // Position age conditions (in seconds)
    public double? MinAge { get; init; }
    public double? MaxAge { get; init; }
    public double? MinLastBuyAge { get; init; }
    public double? MaxLastBuyAge { get; init; }

    // Margin conditions
    public decimal? MinMargin { get; init; }
    public decimal? MaxMargin { get; init; }
    public decimal? MinMarginChange { get; init; }
    public decimal? MaxMarginChange { get; init; }

    // Amount and cost conditions
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public decimal? MinCost { get; init; }
    public decimal? MaxCost { get; init; }

    // DCA conditions
    public int? MinDCALevel { get; init; }
    public int? MaxDCALevel { get; init; }

    // Signal rule filters
    public List<string>? SignalRules { get; init; }
}
