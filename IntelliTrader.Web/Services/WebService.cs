using Autofac;
using IntelliTrader.Core;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace IntelliTrader.Web
{
    internal class WebService(
        ILoggingService loggingService,
        ILifetimeScope container,
        IConfigProvider configProvider) : ConfigrableServiceBase<WebConfig>(configProvider), IWebService
    {
        public override string ServiceName => Constants.ServiceNames.WebService;

        protected override ILoggingService LoggingService => loggingService;

        IWebConfig IWebService.Config => Config;

        private IWebHost webHost;

        public void Start()
        {
            loggingService.Info($"Start Web service (Port: {Config.Port})...");

            try
            {
                // Set the container for Startup to use when registering services
                Startup.Container = container;

                var contentRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "IntelliTrader.Web"));
#if RELEASE
                if (!System.Diagnostics.Debugger.IsAttached)
                {
                    contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "bin");
                }
#endif

                var webHostBuilder = new WebHostBuilder()
                    .UseContentRoot(contentRoot)
                    .UseStartup<Startup>()
                    .UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Any, Config.Port);
                    });

                if (Config.DebugMode)
                {
                    webHostBuilder.UseEnvironment("Development");
                }
                else
                {
                    webHostBuilder.UseEnvironment("Production");
                }

                webHost = webHostBuilder.Build();

                // Suppress WebHost startup messages
                var consOut = Console.Out;
                webHost.Start();
                Console.SetOut(consOut);
            }
            catch (Exception ex)
            {
                loggingService.Error($"Unable to start Web service", ex);
            }

            loggingService.Info($"Web service started");
        }

        public void Stop()
        {
            loggingService.Info($"Stop Web service...");

            try
            {
                webHost.Dispose();
                loggingService.Info($"Web service stopped");
            }
            catch (Exception ex)
            {
                loggingService.Error($"Unable to stop Web service", ex);
            }
        }
    }
}
