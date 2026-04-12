using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Base interface for services that expose a human-readable name for logging and diagnostics.
    /// </summary>
    public interface INamedService
    {
        /// <summary>
        /// The display name of this service, used in logging and health checks.
        /// </summary>
        string ServiceName { get; }
    }
}
