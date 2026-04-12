using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service hosting the ASP.NET Core web dashboard for monitoring and control.
    /// </summary>
    public interface IWebService
    {
        /// <summary>
        /// The web service configuration.
        /// </summary>
        IWebConfig Config { get; }

        /// <summary>
        /// Starts the web dashboard server.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the web dashboard server.
        /// </summary>
        void Stop();
    }
}
