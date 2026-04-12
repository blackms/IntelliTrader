using Autofac;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Interface for building and configuring the DI container.
    /// </summary>
    public interface IApplicationBootstrapper
    {
        /// <summary>
        /// Builds and returns the DI container. Call this once at application startup.
        /// </summary>
        /// <returns>The built Autofac container</returns>
        IContainer BuildContainer();

        /// <summary>
        /// Builds and returns the DI container with a custom configuration action.
        /// </summary>
        /// <param name="configureBuilder">Action to customize the container builder before building</param>
        /// <returns>The built Autofac container</returns>
        IContainer BuildContainer(System.Action<ContainerBuilder> configureBuilder);
    }
}
