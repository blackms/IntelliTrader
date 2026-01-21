namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates margin-related conditions against a trading pair.
    /// Checks MinMargin, MaxMargin, MinMarginChange, and MaxMarginChange.
    /// MarginChange is calculated as current margin minus last buy margin.
    /// </summary>
    public class MarginSpecification : Specification<ConditionContext>
    {
        private readonly decimal? _minMargin;
        private readonly decimal? _maxMargin;
        private readonly decimal? _minMarginChange;
        private readonly decimal? _maxMarginChange;

        public MarginSpecification(
            decimal? minMargin,
            decimal? maxMargin,
            decimal? minMarginChange,
            decimal? maxMarginChange)
        {
            _minMargin = minMargin;
            _maxMargin = maxMargin;
            _minMarginChange = minMarginChange;
            _maxMarginChange = maxMarginChange;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var tradingPair = context.TradingPair;

            // MinMargin check
            if (_minMargin != null)
            {
                if (tradingPair == null || tradingPair.CurrentMargin < _minMargin)
                {
                    return false;
                }
            }

            // MaxMargin check
            if (_maxMargin != null)
            {
                if (tradingPair == null || tradingPair.CurrentMargin > _maxMargin)
                {
                    return false;
                }
            }

            // MinMarginChange check - requires LastBuyMargin metadata
            if (_minMarginChange != null)
            {
                if (tradingPair == null ||
                    tradingPair.Metadata.LastBuyMargin == null ||
                    (tradingPair.CurrentMargin - tradingPair.Metadata.LastBuyMargin) < _minMarginChange)
                {
                    return false;
                }
            }

            // MaxMarginChange check - requires LastBuyMargin metadata
            if (_maxMarginChange != null)
            {
                if (tradingPair == null ||
                    tradingPair.Metadata.LastBuyMargin == null ||
                    (tradingPair.CurrentMargin - tradingPair.Metadata.LastBuyMargin) > _maxMarginChange)
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
            _minMargin != null ||
            _maxMargin != null ||
            _minMarginChange != null ||
            _maxMarginChange != null;
    }
}
