using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the ASP.NET Core web dashboard.
    /// </summary>
    public interface IWebConfig
    {
        /// <summary>
        /// Whether the web dashboard is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Whether to run the web dashboard in debug mode with detailed error pages.
        /// </summary>
        bool DebugMode { get; }

        /// <summary>
        /// The TCP port the web dashboard listens on.
        /// </summary>
        int Port { get; }
    }
}
