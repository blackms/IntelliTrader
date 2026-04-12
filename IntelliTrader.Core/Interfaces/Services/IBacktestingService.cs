using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for replaying historical trading snapshots to backtest strategies.
    /// </summary>
    public interface IBacktestingService : IConfigurableService
    {
        /// <summary>
        /// The backtesting configuration.
        /// </summary>
        IBacktestingConfig Config { get; }

        /// <summary>
        /// Synchronization object for thread-safe snapshot access.
        /// </summary>
        object SyncRoot { get; }

        /// <summary>
        /// Starts the backtesting replay.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the backtesting replay.
        /// </summary>
        void Stop();

        /// <summary>
        /// Marks the backtesting run as complete and logs summary statistics.
        /// </summary>
        /// <param name="skippedSignalSnapshots">Number of signal snapshots that were skipped.</param>
        /// <param name="skippedTickerSnapshots">Number of ticker snapshots that were skipped.</param>
        void Complete(int skippedSignalSnapshots, int skippedTickerSnapshots);

        /// <summary>
        /// Gets the file path for a specific snapshot entity.
        /// </summary>
        /// <param name="snapshotEntity">The entity name (e.g., "signals", "tickers").</param>
        /// <returns>The full file path for the snapshot.</returns>
        string GetSnapshotFilePath(string snapshotEntity);

        /// <summary>
        /// Gets the current signal data from the active snapshot.
        /// </summary>
        /// <returns>Dictionary of signal name to signal collection.</returns>
        Dictionary<string, IEnumerable<ISignal>> GetCurrentSignals();

        /// <summary>
        /// Gets the current ticker data from the active snapshot.
        /// </summary>
        /// <returns>Dictionary of pair name to ticker data.</returns>
        Dictionary<string, ITicker> GetCurrentTickers();

        /// <summary>
        /// Gets the total number of snapshots available for replay.
        /// </summary>
        /// <returns>The total snapshot count.</returns>
        int GetTotalSnapshots();
    }
}
