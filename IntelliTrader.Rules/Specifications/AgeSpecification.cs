namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates age-related conditions against a trading pair.
    /// Checks MinAge, MaxAge, MinLastBuyAge, and MaxLastBuyAge.
    /// Age values are adjusted by the application speed multiplier.
    /// </summary>
    public class AgeSpecification : Specification<ConditionContext>
    {
        private readonly double? _minAge;
        private readonly double? _maxAge;
        private readonly double? _minLastBuyAge;
        private readonly double? _maxLastBuyAge;

        public AgeSpecification(
            double? minAge,
            double? maxAge,
            double? minLastBuyAge,
            double? maxLastBuyAge)
        {
            _minAge = minAge;
            _maxAge = maxAge;
            _minLastBuyAge = minLastBuyAge;
            _maxLastBuyAge = maxLastBuyAge;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var tradingPair = context.TradingPair;
            var speed = context.Speed;

            // MinAge check - adjusted by speed
            if (_minAge != null)
            {
                if (tradingPair == null || tradingPair.CurrentAge < _minAge / speed)
                {
                    return false;
                }
            }

            // MaxAge check - adjusted by speed
            if (_maxAge != null)
            {
                if (tradingPair == null || tradingPair.CurrentAge > _maxAge / speed)
                {
                    return false;
                }
            }

            // MinLastBuyAge check - adjusted by speed
            if (_minLastBuyAge != null)
            {
                if (tradingPair == null || tradingPair.LastBuyAge < _minLastBuyAge / speed)
                {
                    return false;
                }
            }

            // MaxLastBuyAge check - adjusted by speed
            if (_maxLastBuyAge != null)
            {
                if (tradingPair == null || tradingPair.LastBuyAge > _maxLastBuyAge / speed)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints =>
            _minAge != null ||
            _maxAge != null ||
            _minLastBuyAge != null ||
            _maxLastBuyAge != null;
    }
}
