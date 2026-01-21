using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    public abstract class ConfigrableServiceBase<TConfig> : IConfigurableService
        where TConfig : class
    {
        private const double DELAY_BETWEEN_CONFIG_RELOADS_MILLISECONDS = 500;

        private readonly IConfigProvider _configProvider;

        public abstract string ServiceName { get; }

        /// <summary>
        /// Gets the logging service. Derived classes should override this to provide
        /// the injected logging service. Returns null by default (e.g., for LoggingService itself).
        /// </summary>
        protected virtual ILoggingService LoggingService => null;

        /// <summary>
        /// Gets the config provider. Derived classes should override this to provide
        /// the injected config provider. Returns the static fallback by default for backward compatibility.
        /// </summary>
#pragma warning disable CS0618 // Suppress obsolete warning - this is intentional backward compatibility
        protected virtual IConfigProvider ConfigProvider => _configProvider ?? Application.ConfigProvider;
#pragma warning restore CS0618

        /// <summary>
        /// Default constructor for backward compatibility.
        /// Services should migrate to use the constructor that accepts IConfigProvider.
        /// </summary>
        protected ConfigrableServiceBase()
        {
            _configProvider = null;
        }

        /// <summary>
        /// Constructor that accepts an injected IConfigProvider.
        /// Preferred constructor for proper DI usage.
        /// </summary>
        /// <param name="configProvider">The configuration provider</param>
        protected ConfigrableServiceBase(IConfigProvider configProvider)
        {
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        }

        public TConfig Config
        {
            get
            {
                lock (syncRoot)
                {
                    if (config == null)
                    {
                        config = RawConfig.Get<TConfig>();
                        PrepareConfig();
                    }
                    return config;
                }
            }
        }

        public IConfigurationSection RawConfig
        {
            get
            {
                lock (syncRoot)
                {
                    if (rawConfig == null)
                    {
                        rawConfig = ConfigProvider.GetSection(ServiceName, OnRawConfigChanged);
                    }
                    return rawConfig;
                }
            }
        }

        private TConfig config;
        private IConfigurationSection rawConfig;
        private DateTimeOffset lastReloadDate;
        private object syncRoot = new object();

        protected virtual void PrepareConfig() { }
        protected virtual void OnConfigReloaded() { }

        private void OnRawConfigChanged(IConfigurationSection changedRawConfig)
        {
            lock (syncRoot)
            {
                rawConfig = changedRawConfig;
                config = null;
            }

            if ((DateTimeOffset.Now - lastReloadDate).TotalMilliseconds > DELAY_BETWEEN_CONFIG_RELOADS_MILLISECONDS)
            {
                lastReloadDate = DateTimeOffset.Now;
                PrepareConfig();
                OnConfigReloaded();
                LoggingService?.Info($"{ServiceName} configuration reloaded");
            }
        }
    }
}
