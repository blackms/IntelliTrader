using Autofac;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace IntelliTrader.Core
{
    public class Application
    {
        public readonly static IConfigProvider ConfigProvider = new ConfigProvider();

        public static double Speed { get; set; } = 1;

        /// <summary>
        /// Builds and returns the DI container. Call this once at application startup.
        /// </summary>
        /// <returns>The built Autofac container</returns>
        public static IContainer BuildContainer()
        {
            var builder = new ContainerBuilder();

            var assemblyPattern = new Regex($"{nameof(IntelliTrader)}.*.dll");
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => assemblyPattern.IsMatch(Path.GetFileName(a.Location)));
            var dynamicAssembliesPath = new Uri(Path.GetDirectoryName(Assembly.GetCallingAssembly().Location)).LocalPath;
            var dynamicAssemblies = Directory.EnumerateFiles(dynamicAssembliesPath, "*.dll", SearchOption.AllDirectories)
                       .Where(filename => assemblyPattern.IsMatch(Path.GetFileName(filename)) &&
                       !loadedAssemblies.Any(a => Path.GetFileName(a.Location) == Path.GetFileName(filename)));

            var allAssemblies = loadedAssemblies.Concat(dynamicAssemblies.Select(Assembly.LoadFrom)).Distinct();

            builder.RegisterAssemblyModules(allAssemblies.ToArray());
            return builder.Build();
        }
    }
}
