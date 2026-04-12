using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service that monitors trading health and triggers alerts for anomalies
    /// such as stale signals, connectivity issues, and high error rates.
    /// </summary>
    public interface IAlertingService
    {
        /// <summary>
        /// Starts the periodic alert monitoring loop.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the alert monitoring loop.
        /// </summary>
        void Stop();

        /// <summary>
        /// Performs a single alert evaluation cycle, checking all configured alert conditions.
        /// </summary>
        void CheckAlerts();
    }
}
