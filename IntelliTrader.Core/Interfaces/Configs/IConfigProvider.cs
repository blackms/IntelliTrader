using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public interface IConfigProvider
    {
        string GetSectionJson(string sectionName);
        void SetSectionJson(string sectionName, string definition);
        IConfigurationSection GetSection(string sectionName, Action<IConfigurationSection> onChange = null);
        T GetSection<T>(string sectionName, Action<T> onChange = null) where T : class;

        /// <summary>
        /// Sets the logging service factory for deferred logging.
        /// Call this after the DI container is built to enable proper error logging.
        /// </summary>
        void SetLoggingServiceFactory(Func<ILoggingService> factory);
    }
}
