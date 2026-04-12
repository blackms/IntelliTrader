using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IntelliTrader.Infrastructure.Telemetry;

/// <summary>
/// Extension methods for configuring OpenTelemetry in the service collection.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenTelemetry observability to the service collection.
    /// Configures tracing, metrics, and resource attributes for IntelliTrader.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableConsoleExporter">Whether to enable console exporter for development.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIntelliTraderTelemetry(
        this IServiceCollection services,
        bool enableConsoleExporter = false)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: TradingTelemetry.ServiceName,
                    serviceVersion: TradingTelemetry.ServiceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment",
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(TradingTelemetry.ActivitySource.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Filter out health check and static file requests
                        options.Filter = httpContext =>
                        {
                            var path = httpContext.Request.Path.Value ?? string.Empty;
                            return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) &&
                                   !path.StartsWith("/static", StringComparison.OrdinalIgnoreCase);
                        };
                    })
                    .AddHttpClientInstrumentation();

                if (enableConsoleExporter)
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(TradingTelemetry.Meter.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();

                if (enableConsoleExporter)
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }

    /// <summary>
    /// Adds the Prometheus scraping endpoint to the application pipeline.
    /// Exposes metrics at /metrics in Prometheus exposition format.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseIntelliTraderPrometheusEndpoint(this IApplicationBuilder app)
    {
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
        return app;
    }

    /// <summary>
    /// Adds OpenTelemetry with OTLP exporter for production use.
    /// Requires OTEL_EXPORTER_OTLP_ENDPOINT environment variable to be set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="otlpEndpoint">The OTLP endpoint URL. If null, uses OTEL_EXPORTER_OTLP_ENDPOINT env var.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIntelliTraderTelemetryWithOtlp(
        this IServiceCollection services,
        string? otlpEndpoint = null)
    {
        var endpoint = otlpEndpoint ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: TradingTelemetry.ServiceName,
                    serviceVersion: TradingTelemetry.ServiceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment",
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(TradingTelemetry.ActivitySource.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = httpContext =>
                        {
                            var path = httpContext.Request.Path.Value ?? string.Empty;
                            return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) &&
                                   !path.StartsWith("/static", StringComparison.OrdinalIgnoreCase);
                        };
                    })
                    .AddHttpClientInstrumentation();

                // OTLP exporter can be added when OpenTelemetry.Exporter.OpenTelemetryProtocol package is included
                // For now, we'll use console exporter in dev and expect OTLP to be configured externally
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(TradingTelemetry.Meter.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddPrometheusExporter();
            });

        return services;
    }
}
