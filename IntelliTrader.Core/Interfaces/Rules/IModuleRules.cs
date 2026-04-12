using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents a named group of trading rules belonging to a specific module (e.g., signals, trading).
    /// </summary>
    public interface IModuleRules
    {
        /// <summary>
        /// The module name this rule group belongs to (e.g., "Signals", "Trading").
        /// </summary>
        string Module { get; }

        /// <summary>
        /// The raw configuration section for module-level settings.
        /// </summary>
        IConfigurationSection Configuration { get; }

        /// <summary>
        /// The collection of individual rules within this module.
        /// </summary>
        IEnumerable<IRule> Entries { get; }

        /// <summary>
        /// Gets the module configuration bound to a strongly-typed object.
        /// </summary>
        /// <typeparam name="T">The configuration type to bind to.</typeparam>
        /// <returns>The bound configuration object.</returns>
        T GetConfiguration<T>();
    }
}
