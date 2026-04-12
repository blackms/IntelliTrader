using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Provides access to application configuration sections with support for hot-reload and change notifications.
    /// </summary>
    public interface IConfigProvider : IDisposable
    {
        /// <summary>
        /// Gets the raw JSON string for a configuration section.
        /// </summary>
        /// <param name="sectionName">The name of the configuration section.</param>
        /// <returns>The JSON string representation of the section.</returns>
        string GetSectionJson(string sectionName);

        /// <summary>
        /// Sets the raw JSON string for a configuration section.
        /// </summary>
        /// <param name="sectionName">The name of the configuration section.</param>
        /// <param name="definition">The JSON string to set.</param>
        void SetSectionJson(string sectionName, string definition);

        /// <summary>
        /// Gets a configuration section with an optional change callback for hot-reload.
        /// </summary>
        /// <param name="sectionName">The name of the configuration section.</param>
        /// <param name="onChange">Optional callback invoked when the section changes.</param>
        /// <returns>The configuration section.</returns>
        IConfigurationSection GetSection(string sectionName, Action<IConfigurationSection> onChange = null);

        /// <summary>
        /// Gets a strongly-typed configuration section with an optional change callback for hot-reload.
        /// </summary>
        /// <typeparam name="T">The type to bind the configuration section to.</typeparam>
        /// <param name="sectionName">The name of the configuration section.</param>
        /// <param name="onChange">Optional callback invoked with the updated configuration when the section changes.</param>
        /// <returns>The bound configuration object.</returns>
        T GetSection<T>(string sectionName, Action<T> onChange = null) where T : class;

        /// <summary>
        /// Sets the logging service factory for deferred logging.
        /// Call this after the DI container is built to enable proper error logging.
        /// </summary>
        void SetLoggingServiceFactory(Func<ILoggingService> factory);
    }
}
