using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for caching frequently accessed data with configurable expiration.
    /// </summary>
    public interface ICachingService : IConfigurableService
    {
        /// <summary>
        /// The caching configuration.
        /// </summary>
        ICachingConfig Config { get; }

        /// <summary>
        /// Gets a cached object or refreshes it if expired or missing.
        /// </summary>
        /// <typeparam name="T">The type of the cached object.</typeparam>
        /// <param name="objectName">The unique name identifying the cached object.</param>
        /// <param name="refresh">Factory function to create or refresh the object.</param>
        /// <returns>The cached or freshly created object.</returns>
        T GetOrRefresh<T>(string objectName, Func<T> refresh);
    }
}
