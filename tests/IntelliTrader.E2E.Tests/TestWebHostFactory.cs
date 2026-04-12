using Autofac;
using IntelliTrader.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace IntelliTrader.E2E.Tests
{
    /// <summary>
    /// Creates an in-memory test server that boots the real <see cref="IntelliTrader.Web.Startup"/>
    /// pipeline with mocked domain services. This allows full HTTP request/response E2E testing
    /// against the actual middleware stack (auth, rate limiting, routing, security headers, etc.)
    /// without requiring a running exchange connection or trading engine.
    /// </summary>
    /// <remarks>
    /// The production <see cref="IntelliTrader.Web.Startup"/> resolves services from a static
    /// <c>Startup.Container</c> (an Autofac <see cref="ILifetimeScope"/>). We build a real
    /// Autofac container populated with Moq instances so the Resolve extension methods work
    /// correctly without trying to mock Autofac internals.
    /// </remarks>
    public sealed class TestWebHostFactory : IDisposable
    {
        private readonly TestServer _server;
        private readonly HttpClient _client;
        private readonly IContainer _autofacContainer;
        private readonly string _contentRoot;

        public Mock<ICoreService> MockCoreService { get; }
        public Mock<ITradingService> MockTradingService { get; }
        public Mock<ISignalsService> MockSignalsService { get; }
        public Mock<ILoggingService> MockLoggingService { get; }
        public Mock<IHealthCheckService> MockHealthCheckService { get; }
        public Mock<IAuditService> MockAuditService { get; }

        public HttpClient Client => _client;

        public TestWebHostFactory()
        {
            // Create mocks for all services that Startup resolves from the Autofac container
            MockCoreService = new Mock<ICoreService>();
            MockTradingService = new Mock<ITradingService>();
            MockSignalsService = new Mock<ISignalsService>();
            MockLoggingService = new Mock<ILoggingService>();
            MockHealthCheckService = new Mock<IHealthCheckService>();
            MockAuditService = new Mock<IAuditService>();

            // Setup default return values
            var mockCoreConfig = new Mock<ICoreConfig>();
            mockCoreConfig.Setup(c => c.InstanceName).Returns("TestInstance");
            mockCoreConfig.Setup(c => c.PasswordProtected).Returns(false);
            MockCoreService.Setup(s => s.Config).Returns(mockCoreConfig.Object);
            MockCoreService.Setup(s => s.Running).Returns(true);

            MockTradingService.Setup(s => s.IsTradingSuspended).Returns(false);
            MockTradingService.Setup(s => s.GetTrailingBuys()).Returns(new List<string>());
            MockTradingService.Setup(s => s.GetTrailingSells()).Returns(new List<string>());

            MockSignalsService.Setup(s => s.GetTrailingSignals()).Returns(new List<string>());
            MockSignalsService.Setup(s => s.GetSignalNames()).Returns(new List<string> { "Signal1" });

            MockHealthCheckService.Setup(s => s.GetHealthChecks()).Returns(new List<IHealthCheck>());

            MockLoggingService.Setup(s => s.GetLogEntries()).Returns(Array.Empty<string>());

            // Build a real Autofac container with mock registrations.
            // This is much more reliable than trying to mock ILifetimeScope
            // because Autofac's Resolve<T>() extension methods depend on
            // internal IComponentContext.ResolveComponent() plumbing.
            var containerBuilder = new ContainerBuilder();
            containerBuilder.RegisterInstance(MockCoreService.Object).As<ICoreService>();
            containerBuilder.RegisterInstance(MockTradingService.Object).As<ITradingService>();
            containerBuilder.RegisterInstance(MockSignalsService.Object).As<ISignalsService>();
            containerBuilder.RegisterInstance(MockLoggingService.Object).As<ILoggingService>();
            containerBuilder.RegisterInstance(MockHealthCheckService.Object).As<IHealthCheckService>();
            containerBuilder.RegisterInstance(MockAuditService.Object).As<IAuditService>();
            // Autofac automatically resolves IEnumerable<T> from all T registrations.
            // Registering an empty enumerable as a specific type is not needed;
            // if no IConfigurableService instances are registered, Autofac returns
            // an empty collection by default. But we register explicitly to be safe.
            containerBuilder.RegisterInstance(new List<IConfigurableService>()).As<IEnumerable<IConfigurableService>>();

            _autofacContainer = containerBuilder.Build();

            // Set the static container before building the host
            IntelliTrader.Web.Startup.Container = _autofacContainer;

            // Create a temporary content root with the "Static" subdirectory
            // that Startup.Configure expects for UseStaticFiles. Without it,
            // PhysicalFileProvider throws DirectoryNotFoundException.
            _contentRoot = Path.Combine(Path.GetTempPath(), $"IntelliTrader.E2E.Tests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_contentRoot, "Static"));

            var builder = new WebHostBuilder()
                .UseContentRoot(_contentRoot)
                .UseStartup<IntelliTrader.Web.Startup>()
                .UseEnvironment("Development");

            _server = new TestServer(builder);
            _client = _server.CreateClient();
        }

        public void Dispose()
        {
            _client?.Dispose();
            _server?.Dispose();
            _autofacContainer?.Dispose();

            try
            {
                if (Directory.Exists(_contentRoot))
                {
                    Directory.Delete(_contentRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup of temp directory
            }
        }
    }
}
