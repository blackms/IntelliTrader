using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Rules.Specifications;

#region Global Rating Specifications

/// <summary>
/// Specification that checks if the global market rating is at or above a minimum threshold.
/// </summary>
public class MinGlobalRatingSpecification : Specification<RuleEvaluationContext>
{
    private readonly double _minGlobalRating;

    public MinGlobalRatingSpecification(double minGlobalRating)
    {
        _minGlobalRating = minGlobalRating;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        return context.GlobalRating >= _minGlobalRating;
    }
}

/// <summary>
/// Specification that checks if the global market rating is at or below a maximum threshold.
/// </summary>
public class MaxGlobalRatingSpecification : Specification<RuleEvaluationContext>
{
    private readonly double _maxGlobalRating;

    public MaxGlobalRatingSpecification(double maxGlobalRating)
    {
        _maxGlobalRating = maxGlobalRating;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        return context.GlobalRating <= _maxGlobalRating;
    }
}

#endregion

#region Pair Filter Specifications

/// <summary>
/// Specification that checks if the trading pair is in an allowed whitelist.
/// </summary>
public class PairWhitelistSpecification : Specification<RuleEvaluationContext>
{
    private readonly HashSet<string> _allowedPairs;

    public PairWhitelistSpecification(IEnumerable<string> allowedPairs)
    {
        _allowedPairs = new HashSet<string>(allowedPairs, StringComparer.OrdinalIgnoreCase);
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        if (context.Pair is null) return false;
        return _allowedPairs.Contains(context.Pair);
    }
}

#endregion
