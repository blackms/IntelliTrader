using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the alerting subsystem that monitors trading health and notifies on anomalies.
    /// </summary>
    public interface IAlertingConfig
    {
        /// <summary>
        /// Whether the alerting subsystem is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Interval in seconds between alert checks.
        /// </summary>
        int CheckIntervalSeconds { get; }

        /// <summary>
        /// Whether to raise an alert when trading is suspended.
        /// </summary>
        bool TradingSuspendedAlert { get; }

        /// <summary>
        /// Number of consecutive health check failures before triggering an alert.
        /// </summary>
        int HealthCheckFailureThreshold { get; }

        /// <summary>
        /// Number of minutes after which a signal is considered stale and triggers an alert.
        /// </summary>
        int SignalStalenessMinutes { get; }

        /// <summary>
        /// Whether connectivity-related alerts are enabled.
        /// </summary>
        bool ConnectivityAlertEnabled { get; }

        /// <summary>
        /// Number of consecutive order failures before triggering an alert.
        /// </summary>
        int ConsecutiveOrderFailureThreshold { get; }

        /// <summary>
        /// Error rate threshold (0.0-1.0) above which an alert is raised.
        /// </summary>
        double HighErrorRateThreshold { get; }
    }
}
