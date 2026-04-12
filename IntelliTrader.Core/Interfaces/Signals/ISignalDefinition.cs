using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Defines a signal source including its receiver type and configuration.
    /// </summary>
    public interface ISignalDefinition
    {
        /// <summary>
        /// The unique name for this signal definition (e.g., "TradingView-5m").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The signal receiver type that processes this signal (e.g., "TradingViewCryptoSignalReceiver").
        /// </summary>
        string Receiver { get; }

        /// <summary>
        /// The receiver-specific configuration section.
        /// </summary>
        IConfigurationSection Configuration { get; }
    }
}
