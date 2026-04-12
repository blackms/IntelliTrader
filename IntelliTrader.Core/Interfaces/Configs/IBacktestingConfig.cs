using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for backtesting mode, which replays historical snapshots to simulate trading.
    /// </summary>
    public interface IBacktestingConfig
    {
        /// <summary>
        /// Whether backtesting mode is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Whether to replay historical snapshots.
        /// </summary>
        bool Replay { get; }

        /// <summary>
        /// Whether to output detailed replay information during backtesting.
        /// </summary>
        bool ReplayOutput { get; }

        /// <summary>
        /// Speed multiplier for replay (e.g., 2.0 replays at double speed).
        /// </summary>
        double ReplaySpeed { get; }

        /// <summary>
        /// Optional starting snapshot index for partial replay.
        /// </summary>
        int? ReplayStartIndex { get; }

        /// <summary>
        /// Optional ending snapshot index for partial replay.
        /// </summary>
        int? ReplayEndIndex { get; }

        /// <summary>
        /// Whether to delete existing log files before starting backtesting.
        /// </summary>
        bool DeleteLogs { get; }

        /// <summary>
        /// Whether to delete existing account data before starting backtesting.
        /// </summary>
        bool DeleteAccountData { get; }

        /// <summary>
        /// File path to copy account data from before starting backtesting.
        /// </summary>
        string CopyAccountDataPath { get; }

        /// <summary>
        /// Interval in seconds between snapshot captures.
        /// </summary>
        int SnapshotsInterval { get; }

        /// <summary>
        /// Directory path where snapshots are stored.
        /// </summary>
        string SnapshotsPath { get; }
    }
}
