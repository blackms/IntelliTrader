using System.Collections.Generic;
using IntelliTrader.Core;

namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Builder that creates a composite specification from a rule condition.
    /// All individual specifications are combined with AND logic.
    /// </summary>
    public static class ConditionSpecificationBuilder
    {
        /// <summary>
        /// Builds a composite specification from a rule condition.
        /// </summary>
        /// <param name="condition">The condition to convert into a specification</param>
        /// <returns>A specification that evaluates all the condition's constraints</returns>
        public static ISpecification<ConditionContext> Build(IRuleCondition condition)
        {
            var specifications = new List<ISpecification<ConditionContext>>();

            // Signal-based specifications
            var volumeSpec = new VolumeSpecification(
                condition.MinVolume,
                condition.MaxVolume,
                condition.MinVolumeChange,
                condition.MaxVolumeChange);
            if (volumeSpec.HasConstraints)
            {
                specifications.Add(volumeSpec);
            }

            var ratingSpec = new RatingSpecification(
                condition.MinRating,
                condition.MaxRating,
                condition.MinRatingChange,
                condition.MaxRatingChange);
            if (ratingSpec.HasConstraints)
            {
                specifications.Add(ratingSpec);
            }

            var priceSpec = new PriceSpecification(
                condition.MinPrice,
                condition.MaxPrice,
                condition.MinPriceChange,
                condition.MaxPriceChange);
            if (priceSpec.HasConstraints)
            {
                specifications.Add(priceSpec);
            }

            var volatilitySpec = new VolatilitySpecification(
                condition.MinVolatility,
                condition.MaxVolatility);
            if (volatilitySpec.HasConstraints)
            {
                specifications.Add(volatilitySpec);
            }

            // Global specifications
            var globalRatingSpec = new GlobalRatingSpecification(
                condition.MinGlobalRating,
                condition.MaxGlobalRating);
            if (globalRatingSpec.HasConstraints)
            {
                specifications.Add(globalRatingSpec);
            }

            var pairsSpec = new PairsSpecification(condition.Pairs);
            if (pairsSpec.HasConstraints)
            {
                specifications.Add(pairsSpec);
            }

            // Trading pair specifications
            var ageSpec = new AgeSpecification(
                condition.MinAge,
                condition.MaxAge,
                condition.MinLastBuyAge,
                condition.MaxLastBuyAge);
            if (ageSpec.HasConstraints)
            {
                specifications.Add(ageSpec);
            }

            var marginSpec = new MarginSpecification(
                condition.MinMargin,
                condition.MaxMargin,
                condition.MinMarginChange,
                condition.MaxMarginChange);
            if (marginSpec.HasConstraints)
            {
                specifications.Add(marginSpec);
            }

            var amountSpec = new AmountSpecification(
                condition.MinAmount,
                condition.MaxAmount,
                condition.MinCost,
                condition.MaxCost);
            if (amountSpec.HasConstraints)
            {
                specifications.Add(amountSpec);
            }

            var dcaLevelSpec = new DCALevelSpecification(
                condition.MinDCALevel,
                condition.MaxDCALevel);
            if (dcaLevelSpec.HasConstraints)
            {
                specifications.Add(dcaLevelSpec);
            }

            var signalRulesSpec = new SignalRulesSpecification(condition.SignalRules);
            if (signalRulesSpec.HasConstraints)
            {
                specifications.Add(signalRulesSpec);
            }

            // If no constraints, return a specification that always passes
            if (specifications.Count == 0)
            {
                return new TrueSpecification<ConditionContext>();
            }

            // Combine all specifications with AND logic
            return new CompositeAndSpecification<ConditionContext>(specifications);
        }
    }
}
