using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Determines how multiple matching rules are handled during evaluation.
    /// </summary>
    public enum RuleProcessingMode
    {
        /// <summary>
        /// Stop evaluating after the first matching rule is found.
        /// </summary>
        FirstMatch,

        /// <summary>
        /// Evaluate and apply all matching rules.
        /// </summary>
        AllMatches
    }
}
