namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates volatility-related conditions against a signal.
    /// Checks MinVolatility and MaxVolatility.
    /// </summary>
    public class VolatilitySpecification : Specification<ConditionContext>
    {
        private readonly double? _minVolatility;
        private readonly double? _maxVolatility;

        public VolatilitySpecification(double? minVolatility, double? maxVolatility)
        {
            _minVolatility = minVolatility;
            _maxVolatility = maxVolatility;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var signal = context.Signal;

            // MinVolatility check
            if (_minVolatility != null)
            {
                if (signal == null || signal.Volatility == null || signal.Volatility < _minVolatility)
                {
                    return false;
                }
            }

            // MaxVolatility check
            if (_maxVolatility != null)
            {
                if (signal == null || signal.Volatility == null || signal.Volatility > _maxVolatility)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints => _minVolatility != null || _maxVolatility != null;
    }
}
