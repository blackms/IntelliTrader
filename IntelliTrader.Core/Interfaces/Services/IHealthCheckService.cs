using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for managing health check registrations and monitoring service health.
    /// </summary>
    public interface IHealthCheckService
    {
        /// <summary>
        /// Starts the health check monitoring service.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the health check monitoring service.
        /// </summary>
        void Stop();

        /// <summary>
        /// Creates or updates a named health check with the current status.
        /// </summary>
        /// <param name="name">The unique health check name.</param>
        /// <param name="message">Optional status message.</param>
        /// <param name="failed">Whether the health check is in a failed state.</param>
        void UpdateHealthCheck(string name, string message = null, bool failed = false);

        /// <summary>
        /// Removes a named health check from monitoring.
        /// </summary>
        /// <param name="name">The health check name to remove.</param>
        void RemoveHealthCheck(string name);

        /// <summary>
        /// Gets all currently registered health checks.
        /// </summary>
        /// <returns>Collection of health check statuses.</returns>
        IEnumerable<IHealthCheck> GetHealthChecks();
    }
}
