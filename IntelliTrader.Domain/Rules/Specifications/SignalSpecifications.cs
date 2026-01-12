using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Rules.Specifications;

/// <summary>
/// Base class for signal-based specifications
/// </summary>
public abstract class SignalSpecification : Specification<RuleEvaluationContext>
{
    protected readonly string? SignalName;

    protected SignalSpecification(string? signalName)
    {
        SignalName = signalName;
    }

    protected SignalSnapshot? GetSignal(RuleEvaluationContext context)
    {
        return context.GetSignal(SignalName);
    }
}

#region Volume Specifications

public class MinVolumeSpecification : SignalSpecification
{
    private readonly long _minVolume;

    public MinVolumeSpecification(string? signalName, long minVolume) : base(signalName)
    {
        _minVolume = minVolume;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Volume >= _minVolume;
    }
}

public class MaxVolumeSpecification : SignalSpecification
{
    private readonly long _maxVolume;

    public MaxVolumeSpecification(string? signalName, long maxVolume) : base(signalName)
    {
        _maxVolume = maxVolume;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Volume <= _maxVolume;
    }
}

public class MinVolumeChangeSpecification : SignalSpecification
{
    private readonly double _minVolumeChange;

    public MinVolumeChangeSpecification(string? signalName, double minVolumeChange) : base(signalName)
    {
        _minVolumeChange = minVolumeChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.VolumeChange >= _minVolumeChange;
    }
}

public class MaxVolumeChangeSpecification : SignalSpecification
{
    private readonly double _maxVolumeChange;

    public MaxVolumeChangeSpecification(string? signalName, double maxVolumeChange) : base(signalName)
    {
        _maxVolumeChange = maxVolumeChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.VolumeChange <= _maxVolumeChange;
    }
}

#endregion

#region Price Specifications

public class MinPriceSpecification : SignalSpecification
{
    private readonly decimal _minPrice;

    public MinPriceSpecification(string? signalName, decimal minPrice) : base(signalName)
    {
        _minPrice = minPrice;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Price >= _minPrice;
    }
}

public class MaxPriceSpecification : SignalSpecification
{
    private readonly decimal _maxPrice;

    public MaxPriceSpecification(string? signalName, decimal maxPrice) : base(signalName)
    {
        _maxPrice = maxPrice;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Price <= _maxPrice;
    }
}

public class MinPriceChangeSpecification : SignalSpecification
{
    private readonly decimal _minPriceChange;

    public MinPriceChangeSpecification(string? signalName, decimal minPriceChange) : base(signalName)
    {
        _minPriceChange = minPriceChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.PriceChange >= _minPriceChange;
    }
}

public class MaxPriceChangeSpecification : SignalSpecification
{
    private readonly decimal _maxPriceChange;

    public MaxPriceChangeSpecification(string? signalName, decimal maxPriceChange) : base(signalName)
    {
        _maxPriceChange = maxPriceChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.PriceChange <= _maxPriceChange;
    }
}

#endregion

#region Rating Specifications

public class MinRatingSpecification : SignalSpecification
{
    private readonly double _minRating;

    public MinRatingSpecification(string? signalName, double minRating) : base(signalName)
    {
        _minRating = minRating;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Rating >= _minRating;
    }
}

public class MaxRatingSpecification : SignalSpecification
{
    private readonly double _maxRating;

    public MaxRatingSpecification(string? signalName, double maxRating) : base(signalName)
    {
        _maxRating = maxRating;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Rating <= _maxRating;
    }
}

public class MinRatingChangeSpecification : SignalSpecification
{
    private readonly double _minRatingChange;

    public MinRatingChangeSpecification(string? signalName, double minRatingChange) : base(signalName)
    {
        _minRatingChange = minRatingChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.RatingChange >= _minRatingChange;
    }
}

public class MaxRatingChangeSpecification : SignalSpecification
{
    private readonly double _maxRatingChange;

    public MaxRatingChangeSpecification(string? signalName, double maxRatingChange) : base(signalName)
    {
        _maxRatingChange = maxRatingChange;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.RatingChange <= _maxRatingChange;
    }
}

#endregion

#region Volatility Specifications

public class MinVolatilitySpecification : SignalSpecification
{
    private readonly double _minVolatility;

    public MinVolatilitySpecification(string? signalName, double minVolatility) : base(signalName)
    {
        _minVolatility = minVolatility;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Volatility >= _minVolatility;
    }
}

public class MaxVolatilitySpecification : SignalSpecification
{
    private readonly double _maxVolatility;

    public MaxVolatilitySpecification(string? signalName, double maxVolatility) : base(signalName)
    {
        _maxVolatility = maxVolatility;
    }

    public override bool IsSatisfiedBy(RuleEvaluationContext context)
    {
        var signal = GetSignal(context);
        return signal?.Volatility <= _maxVolatility;
    }
}

#endregion
