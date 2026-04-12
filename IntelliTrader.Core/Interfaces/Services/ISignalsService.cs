using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for acquiring, aggregating, and querying trading signals from configured signal sources.
    /// </summary>
    public interface ISignalsService : IConfigurableService
    {
        /// <summary>
        /// The signals configuration.
        /// </summary>
        ISignalsConfig Config { get; }

        /// <summary>
        /// The signal rules module containing buy signal rules.
        /// </summary>
        IModuleRules Rules { get; }

        /// <summary>
        /// The signal rules processing configuration.
        /// </summary>
        ISignalRulesConfig RulesConfig { get; }

        /// <summary>
        /// Starts signal acquisition from all configured sources.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops signal acquisition.
        /// </summary>
        void Stop();

        /// <summary>
        /// Clears all active trailing signals.
        /// </summary>
        void ClearTrailing();

        /// <summary>
        /// Gets the list of pairs that currently have active trailing signals.
        /// </summary>
        /// <returns>List of pair symbols with active trailing.</returns>
        List<string> GetTrailingSignals();

        /// <summary>
        /// Gets detailed trailing information for a specific pair.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>Collection of active trailing info for the pair.</returns>
        IEnumerable<ISignalTrailingInfo> GetTrailingInfo(string pair);

        /// <summary>
        /// Gets the names of all configured signal sources.
        /// </summary>
        /// <returns>Collection of signal names.</returns>
        IEnumerable<string> GetSignalNames();

        /// <summary>
        /// Gets all current signals across all sources.
        /// </summary>
        /// <returns>Collection of all signals.</returns>
        IEnumerable<ISignal> GetAllSignals();

        /// <summary>
        /// Gets all signals from a specific signal source.
        /// </summary>
        /// <param name="signalName">The signal source name.</param>
        /// <returns>Collection of signals from the source.</returns>
        IEnumerable<ISignal> GetSignalsByName(string signalName);

        /// <summary>
        /// Gets all signals for a specific trading pair across all sources.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <returns>Collection of signals for the pair.</returns>
        IEnumerable<ISignal> GetSignalsByPair(string pair);

        /// <summary>
        /// Gets the rating for a pair from a specific signal source.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <param name="signalName">The signal source name.</param>
        /// <returns>The rating value, or null if no signal data exists.</returns>
        double? GetRating(string pair, string signalName);

        /// <summary>
        /// Gets the combined rating for a pair across multiple signal sources.
        /// </summary>
        /// <param name="pair">The trading pair symbol.</param>
        /// <param name="signalNames">The signal source names to combine.</param>
        /// <returns>The combined rating value, or null if no signal data exists.</returns>
        double? GetRating(string pair, IEnumerable<string> signalNames);

        /// <summary>
        /// Gets the global market rating computed from configured global rating signals.
        /// </summary>
        /// <returns>The global rating value, or null if insufficient data.</returns>
        double? GetGlobalRating();
    }
}
