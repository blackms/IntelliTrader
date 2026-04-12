using Autofac;
using IntelliTrader.Core;
using IntelliTrader.Infrastructure.Telemetry;
using IntelliTrader.Web.BackgroundServices;
using IntelliTrader.Web.Hubs;
using IntelliTrader.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;

namespace IntelliTrader.Web
{
    public class Startup
    {
        /// <summary>
        /// The Autofac container/scope, must be set before web host starts.
        /// </summary>
        public static ILifetimeScope Container { get; set; }

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment Environment { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // Register services from Autofac container into ASP.NET Core DI
            // This bridges the Autofac container to proper constructor injection
            services.AddSingleton(_ => Container.Resolve<ICoreService>());
            services.AddSingleton(_ => Container.Resolve<ITradingService>());
            services.AddSingleton(_ => Container.Resolve<ISignalsService>());
            services.AddSingleton(_ => Container.Resolve<ILoggingService>());
            services.AddSingleton(_ => Container.Resolve<IHealthCheckService>());
            services.AddSingleton(_ => Container.Resolve<IEnumerable<IConfigurableService>>());

            // Register password service for secure password hashing (BCrypt)
            services.AddSingleton<IPasswordService, PasswordService>();

            // Register SignalR hub notifier for broadcasting real-time updates
            services.AddSingleton<ITradingHubNotifier, TradingHubNotifier>();

            // Register SignalR broadcaster background service for push-based updates
            services.AddHostedService<SignalRBroadcasterService>();

            var coreService = Container.Resolve<ICoreService>();

            services.AddAuthentication(options =>
            {
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            }).AddCookie(options =>
            {
                options.LoginPath = "/Login";
                options.Cookie.Name = $"{nameof(IntelliTrader)}_{coreService.Config.InstanceName}";
            });

            services.AddControllersWithViews()
                .AddJsonOptions(opts =>
                {
                    // Preserve property names as-is (no camelCase conversion)
                    opts.JsonSerializerOptions.PropertyNamingPolicy = null;
                    opts.JsonSerializerOptions.WriteIndented = false;
                });

            services.AddSignalR()
                .AddJsonProtocol(options =>
                {
                    // Preserve property names as-is (no camelCase conversion) - same as MVC JSON options
                    options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                });

            // Configure OpenTelemetry for comprehensive observability
            // Includes tracing, metrics for trading operations, ASP.NET Core, HTTP, and runtime
            var enableConsoleExporter = Environment.IsDevelopment();
            services.AddIntelliTraderTelemetry(enableConsoleExporter);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Security response headers middleware - placed early so all responses get headers
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:";

                if (context.Request.IsHttps)
                {
                    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }

                await next();
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(
                Path.Combine(env.ContentRootPath, "Static")),
                RequestPath = "/Static"
            });

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                // Map Minimal API endpoints for data-only operations
                endpoints.MapMinimalApiEndpoints();

                // Map MVC controller routes
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{action=Index}/{id?}",
                    defaults: new { controller = "Home" });

                // Map SignalR hub for real-time trading updates
                endpoints.MapHub<TradingHub>("/trading-hub");
            });
        }
    }
}
