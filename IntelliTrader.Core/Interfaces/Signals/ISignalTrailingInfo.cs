using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Provides information about an active trailing signal for a trading pair.
    /// </summary>
    public interface ISignalTrailingInfo
    {
        /// <summary>
        /// The rule that initiated the trailing.
        /// </summary>
        IRule Rule { get; }

        /// <summary>
        /// The timestamp when trailing started.
        /// </summary>
        DateTimeOffset StartTime { get; }

        /// <summary>
        /// The elapsed duration of the trailing in seconds.
        /// </summary>
        double Duration { get; }
    }
}
