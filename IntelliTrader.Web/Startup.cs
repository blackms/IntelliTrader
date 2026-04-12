using Autofac;
using IntelliTrader.Core;
using IntelliTrader.Infrastructure.Telemetry;
using IntelliTrader.Web.BackgroundServices;
using IntelliTrader.Web.Hubs;
using IntelliTrader.Web.Middleware;
using IntelliTrader.Web.Models;
using IntelliTrader.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.RateLimiting;

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
            services.AddSingleton(_ => Container.Resolve<IAuditService>());
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
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.HttpOnly = true;
            });

            // Load users.json for RBAC if it exists, otherwise fall back to legacy single-password mode
            var usersConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "users.json");
            UsersConfig usersConfig = null;
            if (File.Exists(usersConfigPath))
            {
                try
                {
                    var usersJson = File.ReadAllText(usersConfigPath);
                    usersConfig = JsonSerializer.Deserialize<UsersConfig>(usersJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch
                {
                    // If users.json is malformed, fall back to legacy mode
                    usersConfig = null;
                }
            }
            services.AddSingleton(usersConfig ?? new UsersConfig());

            // Configure role-based authorization policies
            services.AddAuthorization(options =>
            {
                options.AddPolicy(AuthPolicies.AdminOnly, p => p.RequireRole(UserRoles.Admin));
                options.AddPolicy(AuthPolicies.TraderOrAbove, p => p.RequireRole(UserRoles.Admin, UserRoles.Trader));
                options.AddPolicy(AuthPolicies.ViewerOrAbove, p => p.RequireRole(UserRoles.Admin, UserRoles.Trader, UserRoles.Viewer));
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

            // Configure API rate limiting per endpoint category to prevent abuse.
            // Limits are configurable via web.json RateLimiting section.
            var rateLimitSection = Configuration.GetSection("Web:RateLimiting");
            var tradingLimit = rateLimitSection.GetValue<int?>("TradingPermitLimit") ?? 10;
            var statusLimit = rateLimitSection.GetValue<int?>("StatusPermitLimit") ?? 60;
            var configLimit = rateLimitSection.GetValue<int?>("ConfigPermitLimit") ?? 5;
            var windowSeconds = rateLimitSection.GetValue<int?>("WindowSeconds") ?? 60;

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.AddFixedWindowLimiter("trading", opt =>
                {
                    opt.PermitLimit = tradingLimit;
                    opt.Window = TimeSpan.FromSeconds(windowSeconds);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                options.AddFixedWindowLimiter("status", opt =>
                {
                    opt.PermitLimit = statusLimit;
                    opt.Window = TimeSpan.FromSeconds(windowSeconds);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                options.AddFixedWindowLimiter("config", opt =>
                {
                    opt.PermitLimit = configLimit;
                    opt.Window = TimeSpan.FromSeconds(windowSeconds);
                    opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    opt.QueueLimit = 0;
                });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        windowSeconds.ToString();
                    await Task.CompletedTask;
                };
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Conditionally enforce HTTPS redirection in production when configured.
            // Disabled by default so dev/Docker setups without TLS certificates don't break.
            if (!env.IsDevelopment() && Configuration.GetValue<bool>("Web:EnableHttpsRedirection"))
            {
                app.UseHttpsRedirection();
            }

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

            // Serve OpenAPI spec and Swagger UI (before auth so it's publicly accessible).
            // Only enabled in Development or when explicitly opted-in via config to avoid
            // leaking API surface details in production.
            var enableSwagger = env.IsDevelopment() || Configuration.GetValue<bool>("Swagger:Enabled");
            if (enableSwagger)
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "IntelliTrader API v1");
                    c.RoutePrefix = "swagger";
                });
            }

            // Expose /metrics endpoint for Prometheus scraping
            app.UseIntelliTraderPrometheusEndpoint();

            app.UseRouting();

            app.UseMiddleware<AuditMiddleware>();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseRateLimiter();

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
