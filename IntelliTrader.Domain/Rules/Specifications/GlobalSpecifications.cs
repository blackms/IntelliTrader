using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Rules.Specifications;

#region Global Rating Specifications

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
