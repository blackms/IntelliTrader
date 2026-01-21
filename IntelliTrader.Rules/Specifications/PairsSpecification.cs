using System.Collections.Generic;

namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that checks if a pair is in the allowed list of pairs.
    /// </summary>
    public class PairsSpecification : Specification<ConditionContext>
    {
        private readonly List<string>? _pairs;

        public PairsSpecification(List<string>? pairs)
        {
            _pairs = pairs;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            // Pairs check - pair must be in the allowed list
            if (_pairs != null)
            {
                if (context.Pair == null || !_pairs.Contains(context.Pair))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints => _pairs != null;
    }
}
