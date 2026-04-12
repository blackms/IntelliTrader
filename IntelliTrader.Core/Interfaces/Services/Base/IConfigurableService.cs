using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Base interface for services that have associated configuration sections.
    /// </summary>
    public interface IConfigurableService : INamedService
    {
        /// <summary>
        /// The raw configuration section bound to this service.
        /// </summary>
        IConfigurationSection RawConfig { get; }
    }
}
