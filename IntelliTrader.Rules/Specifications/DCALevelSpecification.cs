namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates DCA (Dollar Cost Averaging) level conditions against a trading pair.
    /// Checks MinDCALevel and MaxDCALevel.
    /// </summary>
    public class DCALevelSpecification : Specification<ConditionContext>
    {
        private readonly int? _minDCALevel;
        private readonly int? _maxDCALevel;

        public DCALevelSpecification(int? minDCALevel, int? maxDCALevel)
        {
            _minDCALevel = minDCALevel;
            _maxDCALevel = maxDCALevel;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var tradingPair = context.TradingPair;

            // MinDCALevel check
            if (_minDCALevel != null)
            {
                if (tradingPair == null || tradingPair.DCALevel < _minDCALevel)
                {
                    return false;
                }
            }

            // MaxDCALevel check
            if (_maxDCALevel != null)
            {
                if (tradingPair == null || tradingPair.DCALevel > _maxDCALevel)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints => _minDCALevel != null || _maxDCALevel != null;
    }
}
