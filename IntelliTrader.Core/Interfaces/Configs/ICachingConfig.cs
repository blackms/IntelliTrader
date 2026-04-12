using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the caching subsystem that stores and reuses frequently accessed data.
    /// </summary>
    public interface ICachingConfig
    {
        /// <summary>
        /// Whether caching is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Whether to use a shared cache across multiple instances.
        /// </summary>
        bool Shared { get; }

        /// <summary>
        /// File system path for the shared cache storage.
        /// </summary>
        string SharedCachePath { get; }

        /// <summary>
        /// Interval in seconds between shared cache cleanup operations.
        /// </summary>
        double SharedCacheCleanupInterval { get; }

        /// <summary>
        /// Maximum age (in seconds) for cached items, keyed by cache object name.
        /// </summary>
        IEnumerable<KeyValuePair<string, double>> MaxAge { get; }
    }
}
