namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that evaluates rating-related conditions against a signal.
    /// Checks MinRating, MaxRating, MinRatingChange, and MaxRatingChange.
    /// </summary>
    public class RatingSpecification : Specification<ConditionContext>
    {
        private readonly double? _minRating;
        private readonly double? _maxRating;
        private readonly double? _minRatingChange;
        private readonly double? _maxRatingChange;

        public RatingSpecification(
            double? minRating,
            double? maxRating,
            double? minRatingChange,
            double? maxRatingChange)
        {
            _minRating = minRating;
            _maxRating = maxRating;
            _minRatingChange = minRatingChange;
            _maxRatingChange = maxRatingChange;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            var signal = context.Signal;

            // MinRating check
            if (_minRating != null)
            {
                if (signal == null || signal.Rating == null || signal.Rating < _minRating)
                {
                    return false;
                }
            }

            // MaxRating check
            if (_maxRating != null)
            {
                if (signal == null || signal.Rating == null || signal.Rating > _maxRating)
                {
                    return false;
                }
            }

            // MinRatingChange check
            if (_minRatingChange != null)
            {
                if (signal == null || signal.RatingChange == null || signal.RatingChange < _minRatingChange)
                {
                    return false;
                }
            }

            // MaxRatingChange check
            if (_maxRatingChange != null)
            {
                if (signal == null || signal.RatingChange == null || signal.RatingChange > _maxRatingChange)
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
            _minRating != null ||
            _maxRating != null ||
            _minRatingChange != null ||
            _maxRatingChange != null;
    }
}
