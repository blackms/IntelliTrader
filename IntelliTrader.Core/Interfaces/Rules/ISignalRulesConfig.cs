using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for signal rule processing behavior.
    /// </summary>
    public interface ISignalRulesConfig
    {
        /// <summary>
        /// How rules are processed: stop at first match or evaluate all.
        /// </summary>
        RuleProcessingMode ProcessingMode { get; }

        /// <summary>
        /// Interval in seconds between signal rule evaluations.
        /// </summary>
        double CheckInterval { get; }
    }
}
