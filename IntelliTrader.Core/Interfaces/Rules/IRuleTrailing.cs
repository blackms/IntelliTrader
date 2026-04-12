using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for trailing behavior that delays rule action execution to confirm signal persistence.
    /// </summary>
    public interface IRuleTrailing
    {
        /// <summary>
        /// Whether trailing is enabled for this rule.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Minimum duration in seconds that conditions must be met before the action triggers.
        /// </summary>
        int MinDuration { get; }

        /// <summary>
        /// Maximum duration in seconds after which the trailing expires without triggering.
        /// </summary>
        int MaxDuration { get; }

        /// <summary>
        /// Conditions that must be met to initiate trailing. Separate from the rule's main conditions.
        /// </summary>
        IEnumerable<IRuleCondition> StartConditions { get; }
    }
}
