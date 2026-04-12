using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Core application configuration controlling debug mode, authentication, and health checks.
    /// </summary>
    public interface ICoreConfig
    {
        /// <summary>
        /// Whether debug mode is enabled, providing additional logging and diagnostics.
        /// </summary>
        bool DebugMode { get; }

        /// <summary>
        /// Whether the web dashboard requires password authentication.
        /// </summary>
        bool PasswordProtected { get; }

        /// <summary>
        /// The password hash used for web dashboard authentication.
        /// </summary>
        string Password { get; }

        /// <summary>
        /// Display name for this bot instance, used in notifications and the dashboard.
        /// </summary>
        string InstanceName { get; }

        /// <summary>
        /// Timezone offset in hours from UTC for display purposes.
        /// </summary>
        double TimezoneOffset { get; }

        /// <summary>
        /// Whether periodic health checks are enabled.
        /// </summary>
        bool HealthCheckEnabled { get; set; }

        /// <summary>
        /// Interval in seconds between health check evaluations.
        /// </summary>
        double HealthCheckInterval { get; }

        /// <summary>
        /// Time in seconds after which trading is suspended if a health check fails continuously.
        /// </summary>
        double HealthCheckSuspendTradingTimeout { get; }

        /// <summary>
        /// Number of consecutive health check failures that trigger a service restart.
        /// </summary>
        int HealthCheckFailuresToRestartServices { get; }
    }
}
