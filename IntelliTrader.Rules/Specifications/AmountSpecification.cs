namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates amount and cost conditions against a trading pair.
    /// Checks MinAmount, MaxAmount, MinCost, and MaxCost.
    /// </summary>
    public class AmountSpecification : Specification<ConditionContext>
    {
        private readonly decimal? _minAmount;
        private readonly decimal? _maxAmount;
        private readonly decimal? _minCost;
        private readonly decimal? _maxCost;

        public AmountSpecification(
            decimal? minAmount,
            decimal? maxAmount,
            decimal? minCost,
            decimal? maxCost)
        {
            _minAmount = minAmount;
            _maxAmount = maxAmount;
            _minCost = minCost;
            _maxCost = maxCost;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var tradingPair = context.TradingPair;

            // MinAmount check
            if (_minAmount != null)
            {
                if (tradingPair == null || tradingPair.TotalAmount < _minAmount)
                {
                    return false;
                }
            }

            // MaxAmount check
            if (_maxAmount != null)
            {
                if (tradingPair == null || tradingPair.TotalAmount > _maxAmount)
                {
                    return false;
                }
            }

            // MinCost check
            if (_minCost != null)
            {
                if (tradingPair == null || tradingPair.CurrentCost < _minCost)
                {
                    return false;
                }
            }

            // MaxCost check
            if (_maxCost != null)
            {
                if (tradingPair == null || tradingPair.CurrentCost > _maxCost)
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
            _minAmount != null ||
            _maxAmount != null ||
            _minCost != null ||
            _maxCost != null;
    }
}
