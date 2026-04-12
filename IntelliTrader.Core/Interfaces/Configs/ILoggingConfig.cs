using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the logging subsystem including structured JSON output.
    /// </summary>
    public interface ILoggingConfig
    {
        /// <summary>
        /// Whether logging is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// When true, enables an additional JSON-formatted file sink for structured log output.
        /// Useful for production environments where logs are ingested by centralized logging systems.
        /// </summary>
        bool JsonOutputEnabled { get; }

        /// <summary>
        /// File path pattern for the JSON log output (e.g., "log/structured-.json").
        /// Only used when JsonOutputEnabled is true.
        /// </summary>
        string? JsonOutputPath { get; }
    }
}
