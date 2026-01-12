using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Rules.Specifications;

/// <summary>
/// Base class for position-based specifications
/// </summary>
public abstract class PositionSpecification : Specification<RuleEvaluationContext>
{
    protected PositionSnapshot? GetPosition(RuleEvaluationContext context) => context.Position;
}

#region Age Specifications

public class MinAgeSpecification : PositionSpecification
{
    private readonly TimeSpan _minAge;

    public MinAgeSpecification(double minAgeSeconds)
    {
        _minAge = TimeSpan.FromSeconds(minAgeSeconds);
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position is null) return false;

        var adjustedMinAge = TimeSpan.FromTicks((long)(_minAge.Ticks / context.SpeedMultiplier));
        return position.CurrentAge >= adjustedMinAge;
    }
}

public class MaxAgeSpecification : PositionSpecification
{
    private readonly TimeSpan _maxAge;

    public MaxAgeSpecification(double maxAgeSeconds)
    {
        _maxAge = TimeSpan.FromSeconds(maxAgeSeconds);
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position is null) return false;

        var adjustedMaxAge = TimeSpan.FromTicks((long)(_maxAge.Ticks / context.SpeedMultiplier));
        return position.CurrentAge <= adjustedMaxAge;
    }
}

public class MinLastBuyAgeSpecification : PositionSpecification
{
    private readonly TimeSpan _minLastBuyAge;

    public MinLastBuyAgeSpecification(double minLastBuyAgeSeconds)
    {
        _minLastBuyAge = TimeSpan.FromSeconds(minLastBuyAgeSeconds);
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position is null) return false;

        var adjustedMinAge = TimeSpan.FromTicks((long)(_minLastBuyAge.Ticks / context.SpeedMultiplier));
        return position.LastBuyAge >= adjustedMinAge;
    }
}

public class MaxLastBuyAgeSpecification : PositionSpecification
{
    private readonly TimeSpan _maxLastBuyAge;

    public MaxLastBuyAgeSpecification(double maxLastBuyAgeSeconds)
    {
        _maxLastBuyAge = TimeSpan.FromSeconds(maxLastBuyAgeSeconds);
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position is null) return false;

        var adjustedMaxAge = TimeSpan.FromTicks((long)(_maxLastBuyAge.Ticks / context.SpeedMultiplier));
        return position.LastBuyAge <= adjustedMaxAge;
    }
}

#endregion

#region Margin Specifications

public class MinMarginSpecification : PositionSpecification
{
    private readonly decimal _minMargin;

    public MinMarginSpecification(decimal minMargin)
    {
        _minMargin = minMargin;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.CurrentMargin >= _minMargin;
    }
}

public class MaxMarginSpecification : PositionSpecification
{
    private readonly decimal _maxMargin;

    public MaxMarginSpecification(decimal maxMargin)
    {
        _maxMargin = maxMargin;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.CurrentMargin <= _maxMargin;
    }
}

public class MinMarginChangeSpecification : PositionSpecification
{
    private readonly decimal _minMarginChange;

    public MinMarginChangeSpecification(decimal minMarginChange)
    {
        _minMarginChange = minMarginChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position is null || position.LastBuyMargin is null) return false;

        var marginChange = position.CurrentMargin - position.LastBuyMargin.Value;
        return marginChange >= _minMarginChange;
    }
}

public class MaxMarginChangeSpecification : PositionSpecification
{
    private readonly decimal _maxMarginChange;

    public MaxMarginChangeSpecification(decimal maxMarginChange)
    {
        _maxMarginChange = maxMarginChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position is null || position.LastBuyMargin is null) return false;

        var marginChange = position.CurrentMargin - position.LastBuyMargin.Value;
        return marginChange <= _maxMarginChange;
    }
}

#endregion

#region Amount and Cost Specifications

public class MinAmountSpecification : PositionSpecification
{
    private readonly decimal _minAmount;

    public MinAmountSpecification(decimal minAmount)
    {
        _minAmount = minAmount;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.TotalAmount >= _minAmount;
    }
}

public class MaxAmountSpecification : PositionSpecification
{
    private readonly decimal _maxAmount;

    public MaxAmountSpecification(decimal maxAmount)
    {
        _maxAmount = maxAmount;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.TotalAmount <= _maxAmount;
    }
}

public class MinCostSpecification : PositionSpecification
{
    private readonly decimal _minCost;

    public MinCostSpecification(decimal minCost)
    {
        _minCost = minCost;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.CurrentCost >= _minCost;
    }
}

public class MaxCostSpecification : PositionSpecification
{
    private readonly decimal _maxCost;

    public MaxCostSpecification(decimal maxCost)
    {
        _maxCost = maxCost;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.CurrentCost <= _maxCost;
    }
}

#endregion

#region DCA Level Specifications

public class MinDCALevelSpecification : PositionSpecification
{
    private readonly int _minDCALevel;

    public MinDCALevelSpecification(int minDCALevel)
    {
        _minDCALevel = minDCALevel;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.DCALevel >= _minDCALevel;
    }
}

public class MaxDCALevelSpecification : PositionSpecification
{
    private readonly int _maxDCALevel;

    public MaxDCALevelSpecification(int maxDCALevel)
    {
        _maxDCALevel = maxDCALevel;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        return position?.DCALevel <= _maxDCALevel;
    }
}

#endregion

#region Signal Rule Specifications

public class SignalRulesSpecification : PositionSpecification
{
    private readonly HashSet<string> _signalRules;

    public SignalRulesSpecification(IEnumerable<string> signalRules)
    {
        _signalRules = new HashSet<string>(signalRules, StringComparer.OrdinalIgnoreCase);
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var position = GetPosition(context);
        if (position?.SignalRule is null) return false;
        return _signalRules.Contains(position.SignalRule);
    }
}

#endregion
