using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents the current state of a named health check.
    /// </summary>
    public interface IHealthCheck
    {
        /// <summary>
        /// The unique name identifying this health check.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A human-readable message describing the health check status.
        /// </summary>
        string Message { get; }

        /// <summary>
        /// The timestamp of the last health check update.
        /// </summary>
        DateTimeOffset LastUpdated { get; }

        /// <summary>
        /// Whether this health check is currently in a failed state.
        /// </summary>
        bool Failed { get; }
    }
}
