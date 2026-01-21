namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates price-related conditions against a signal.
    /// Checks MinPrice, MaxPrice, MinPriceChange, and MaxPriceChange.
    /// </summary>
    public class PriceSpecification : Specification<ConditionContext>
    {
        private readonly decimal? _minPrice;
        private readonly decimal? _maxPrice;
        private readonly decimal? _minPriceChange;
        private readonly decimal? _maxPriceChange;

        public PriceSpecification(
            decimal? minPrice,
            decimal? maxPrice,
            decimal? minPriceChange,
            decimal? maxPriceChange)
        {
            _minPrice = minPrice;
            _maxPrice = maxPrice;
            _minPriceChange = minPriceChange;
            _maxPriceChange = maxPriceChange;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var signal = context.Signal;

            // MinPrice check
            if (_minPrice != null)
            {
                if (signal == null || signal.Price == null || signal.Price < _minPrice)
                {
                    return false;
                }
            }

            // MaxPrice check
            if (_maxPrice != null)
            {
                if (signal == null || signal.Price == null || signal.Price > _maxPrice)
                {
                    return false;
                }
            }

            // MinPriceChange check
            if (_minPriceChange != null)
            {
                if (signal == null || signal.PriceChange == null || signal.PriceChange < _minPriceChange)
                {
                    return false;
                }
            }

            // MaxPriceChange check
            if (_maxPriceChange != null)
            {
                if (signal == null || signal.PriceChange == null || signal.PriceChange > _maxPriceChange)
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
            _minPrice != null ||
            _maxPrice != null ||
            _minPriceChange != null ||
            _maxPriceChange != null;
    }
}
