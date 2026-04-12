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
        IConfigProvider configProvider) : ConfigurableServiceBase<WebConfig>(configProvider), IWebService
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

                // Resolve the ASP.NET content root.
                //
                // The legacy logic computed `cwd/../IntelliTrader.Web` for
                // dev runs and `cwd/bin` for Release builds. Both
                // assumptions break inside a published container, where
                // the executable, views and wwwroot all live next to each
                // other under /app — there is no parent IntelliTrader.Web
                // folder and no `bin` subdirectory.
                //
                // Use AppContext.BaseDirectory (the directory of the
                // running assembly) which is correct in every environment:
                // dev runs from bin/Debug/..., Release publishes flat,
                // and containers have /app as the base directory.
                var contentRoot = AppContext.BaseDirectory;
                if (!Directory.Exists(Path.Combine(contentRoot, "Views")) &&
                    !Directory.Exists(Path.Combine(contentRoot, "wwwroot")))
                {
                    // Legacy dev fallback: Visual Studio runs from
                    // IntelliTrader/bin/Debug/net9.0 and the static
                    // content lives two levels up under IntelliTrader.Web.
                    var devFallback = Path.GetFullPath(
                        Path.Combine(Directory.GetCurrentDirectory(), "..", "IntelliTrader.Web"));
                    if (Directory.Exists(devFallback))
                    {
                        contentRoot = devFallback;
                    }
                }

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
