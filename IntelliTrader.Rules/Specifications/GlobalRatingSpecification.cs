namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates global rating conditions.
    /// Checks MinGlobalRating and MaxGlobalRating against the aggregated rating.
    /// </summary>
    public class GlobalRatingSpecification : Specification<ConditionContext>
    {
        private readonly double? _minGlobalRating;
        private readonly double? _maxGlobalRating;

        public GlobalRatingSpecification(double? minGlobalRating, double? maxGlobalRating)
        {
            _minGlobalRating = minGlobalRating;
            _maxGlobalRating = maxGlobalRating;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var globalRating = context.GlobalRating;

            // MinGlobalRating check
            if (_minGlobalRating != null)
            {
                if (globalRating == null || globalRating < _minGlobalRating)
                {
                    return false;
                }
            }

            // MaxGlobalRating check
            if (_maxGlobalRating != null)
            {
                if (globalRating == null || globalRating > _maxGlobalRating)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints => _minGlobalRating != null || _maxGlobalRating != null;
    }
}
