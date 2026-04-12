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
using Microsoft.OpenApi.Models;
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

            // Configure OpenAPI/Swagger documentation
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "IntelliTrader API",
                    Version = "v1",
                    Description = "Cryptocurrency trading bot REST API for managing trades, monitoring signals, and checking system health.",
                    Contact = new OpenApiContact
                    {
                        Name = "IntelliTrader",
                        Url = new Uri("https://github.com/nicamedic/IntelliTrader")
                    }
                });

                // Include MVC controller actions in Swagger docs
                c.DocInclusionPredicate((docName, apiDesc) => true);
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
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

            // Serve OpenAPI spec and Swagger UI (before auth so it's publicly accessible)
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "IntelliTrader API v1");
                c.RoutePrefix = "swagger";
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
