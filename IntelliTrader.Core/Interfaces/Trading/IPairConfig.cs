using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Per-pair trading configuration that overrides global buy/sell settings for specific pairs.
    /// </summary>
    public interface IPairConfig : IBuyConfig, ISellConfig
    {
        /// <summary>
        /// Names of the trading rules that apply to this pair.
        /// </summary>
        IEnumerable<string> Rules { get; }

        /// <summary>
        /// Whether swapping (selling this pair to buy another) is enabled.
        /// </summary>
        bool SwapEnabled { get; }

        /// <summary>
        /// Signal rule names that can trigger a swap for this pair.
        /// </summary>
        List<string> SwapSignalRules { get; }

        /// <summary>
        /// Timeout in seconds for swap operations.
        /// </summary>
        int SwapTimeout { get; }

        /// <summary>
        /// The margin threshold for the current DCA level, if applicable.
        /// </summary>
        decimal? CurrentDCAMargin { get; }

        /// <summary>
        /// The margin threshold for the next DCA level, if applicable.
        /// </summary>
        decimal? NextDCAMargin { get; }
    }
}
