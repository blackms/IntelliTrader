using Microsoft.Extensions.Configuration;
using System;

namespace IntelliTrader.Core
{
    public abstract class ConfigurableServiceBase<TConfig> : IConfigurableService
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
        /// Gets the config provider injected via constructor.
        /// </summary>
        protected IConfigProvider ConfigProvider => _configProvider;

        /// <summary>
        /// Constructor that accepts an injected IConfigProvider.
        /// </summary>
        /// <param name="configProvider">The configuration provider</param>
        protected ConfigurableServiceBase(IConfigProvider configProvider)
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
        private readonly object syncRoot = new object();

        protected virtual void PrepareConfig() { }
        protected virtual void OnConfigReloaded() { }

        private void OnRawConfigChanged(IConfigurationSection changedRawConfig)
        {
            bool shouldReload;
            lock (syncRoot)
            {
                rawConfig = changedRawConfig;
                config = null;

                if ((DateTimeOffset.Now - lastReloadDate).TotalMilliseconds > DELAY_BETWEEN_CONFIG_RELOADS_MILLISECONDS)
                {
                    lastReloadDate = DateTimeOffset.Now;
                    shouldReload = true;
                }
                else
                {
                    shouldReload = false;
                }
            }

            if (shouldReload)
            {
                PrepareConfig();
                OnConfigReloaded();
                LoggingService?.Info($"{ServiceName} configuration reloaded");
            }
        }
    }
}
