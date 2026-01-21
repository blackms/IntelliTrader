using System.Collections.Generic;

namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Specification that checks if a trading pair's signal rule is in the allowed list.
    /// </summary>
    public class SignalRulesSpecification : Specification<ConditionContext>
    {
        private readonly List<string>? _signalRules;

        public SignalRulesSpecification(List<string>? signalRules)
        {
            _signalRules = signalRules;
        }

        public override bool IsSatisfiedBy(ConditionContext context)
        {
            // SignalRules check - trading pair's signal rule must be in the allowed list
            if (_signalRules != null)
            {
                var tradingPair = context.TradingPair;
                if (tradingPair == null ||
                    tradingPair.Metadata.SignalRule == null ||
                    !_signalRules.Contains(tradingPair.Metadata.SignalRule))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns true if this specification has any constraints to check.
        /// </summary>
        public bool HasConstraints => _signalRules != null;
    }
}
