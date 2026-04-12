using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the signal acquisition subsystem that collects and aggregates trading signals.
    /// </summary>
    public interface ISignalsConfig
    {
        /// <summary>
        /// Whether signal processing is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Names of signals used to compute the global market rating.
        /// </summary>
        IEnumerable<string> GlobalRatingSignals { get; }

        /// <summary>
        /// Signal source definitions specifying receivers and their configurations.
        /// </summary>
        IEnumerable<ISignalDefinition> Definitions { get; }
    }
}
