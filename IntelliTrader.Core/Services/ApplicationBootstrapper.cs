using Autofac;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Builds and configures the Autofac DI container.
    /// Replaces the static Application.BuildContainer() method.
    /// </summary>
    public class ApplicationBootstrapper : IApplicationBootstrapper
    {
        private readonly IConfigProvider _configProvider;
        private readonly IApplicationContext _applicationContext;

        /// <summary>
        /// Creates a new ApplicationBootstrapper with default ConfigProvider and ApplicationContext.
        /// </summary>
        public ApplicationBootstrapper()
            : this(new ConfigProvider(), new ApplicationContext())
        {
        }

        /// <summary>
        /// Creates a new ApplicationBootstrapper with the specified dependencies.
        /// </summary>
        /// <param name="configProvider">The configuration provider to use</param>
        /// <param name="applicationContext">The application context to use</param>
        public ApplicationBootstrapper(IConfigProvider configProvider, IApplicationContext applicationContext)
        {
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _applicationContext = applicationContext ?? throw new ArgumentNullException(nameof(applicationContext));
        }

        /// <summary>
        /// Gets the ConfigProvider instance used by this bootstrapper.
        /// </summary>
        public IConfigProvider ConfigProvider => _configProvider;

        /// <summary>
        /// Gets the ApplicationContext instance used by this bootstrapper.
        /// </summary>
        public IApplicationContext ApplicationContext => _applicationContext;

        /// <inheritdoc/>
        public IContainer BuildContainer()
        {
            return BuildContainer(null);
        }

        /// <inheritdoc/>
        public IContainer BuildContainer(Action<ContainerBuilder> configureBuilder)
        {
            var builder = new ContainerBuilder();

            // Register the singleton instances created during bootstrapping
            builder.RegisterInstance(_configProvider).As<IConfigProvider>().SingleInstance();
            builder.RegisterInstance(_applicationContext).As<IApplicationContext>().SingleInstance();

            // Discover and register all IntelliTrader modules
            var assemblyPattern = new Regex($"{nameof(IntelliTrader)}.*.dll");
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => assemblyPattern.IsMatch(Path.GetFileName(a.Location)));
            var dynamicAssembliesPath = new Uri(Path.GetDirectoryName(Assembly.GetCallingAssembly().Location)).LocalPath;
            var dynamicAssemblies = Directory.EnumerateFiles(dynamicAssembliesPath, "*.dll", SearchOption.AllDirectories)
                .Where(filename => assemblyPattern.IsMatch(Path.GetFileName(filename)) &&
                    !loadedAssemblies.Any(a => Path.GetFileName(a.Location) == Path.GetFileName(filename)));

            var allAssemblies = loadedAssemblies.Concat(dynamicAssemblies.Select(Assembly.LoadFrom)).Distinct();

            builder.RegisterAssemblyModules(allAssemblies.ToArray());

            // Allow custom configuration
            configureBuilder?.Invoke(builder);

            return builder.Build();
        }
    }
}
