using Autofac;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Static application facade providing backward compatibility during the transition to DI.
    ///
    /// MIGRATION GUIDE:
    /// - Use IApplicationBootstrapper for building containers (inject or use ApplicationBootstrapper directly)
    /// - Use IConfigProvider injected via constructor instead of Application.ConfigProvider
    /// - Use IApplicationContext injected via constructor instead of Application.Speed
    ///
    /// The static members are marked as [Obsolete] to encourage migration to the DI-based approach.
    /// </summary>
    public class Application
    {
        // Backing instances - initialized during bootstrapping
        private static IConfigProvider _configProvider;
        private static IApplicationContext _applicationContext;

        /// <summary>
        /// Gets the configuration provider.
        /// Prefer using IConfigProvider via dependency injection instead.
        /// </summary>
        [Obsolete("Use IConfigProvider via dependency injection instead. This static accessor will be removed in a future version.")]
        public static IConfigProvider ConfigProvider
        {
            get
            {
                // Lazy initialization for backward compatibility during bootstrap
                if (_configProvider == null)
                {
                    _configProvider = new ConfigProvider();
                }
                return _configProvider;
            }
        }

        /// <summary>
        /// Gets or sets the speed multiplier.
        /// Prefer using IApplicationContext via dependency injection instead.
        /// </summary>
        [Obsolete("Use IApplicationContext.Speed via dependency injection instead. This static accessor will be removed in a future version.")]
        public static double Speed
        {
            get => _applicationContext?.Speed ?? 1;
            set
            {
                if (_applicationContext != null)
                {
                    _applicationContext.Speed = value;
                }
            }
        }

        /// <summary>
        /// Initializes the static facade with the DI-resolved instances.
        /// Called by ApplicationBootstrapper after building the container.
        /// </summary>
        /// <param name="configProvider">The config provider instance</param>
        /// <param name="applicationContext">The application context instance</param>
        internal static void Initialize(IConfigProvider configProvider, IApplicationContext applicationContext)
        {
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        }

        /// <summary>
        /// Builds and returns the DI container. Call this once at application startup.
        /// Prefer using IApplicationBootstrapper instead.
        /// </summary>
        /// <returns>The built Autofac container</returns>
        [Obsolete("Use IApplicationBootstrapper.BuildContainer() instead. This static method will be removed in a future version.")]
        public static IContainer BuildContainer()
        {
            var bootstrapper = new ApplicationBootstrapper();
            var container = bootstrapper.BuildContainer();

            // Initialize the static facade with the bootstrapper's instances
            Initialize(bootstrapper.ConfigProvider, bootstrapper.ApplicationContext);

            return container;
        }
    }
}
