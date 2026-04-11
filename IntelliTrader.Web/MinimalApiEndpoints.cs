using IntelliTrader.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Web
{
    /// <summary>
    /// Extension methods to register Minimal API endpoints for data-only operations.
    /// These endpoints replace the legacy MVC controller actions that only return JSON data.
    /// </summary>
    public static class MinimalApiEndpoints
    {
        /// <summary>
        /// Maps all Minimal API endpoints to the endpoint route builder.
        /// </summary>
        public static IEndpointRouteBuilder MapMinimalApiEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // GET /api/health - Anonymous liveness probe used by Docker
            // HEALTHCHECK and Kubernetes probes. Must stay outside of the
            // authenticated /api group so it can be reached without a session.
            endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
                .AllowAnonymous()
                .WithName("HealthCheck")
                .WithDescription("Liveness probe returning 200 OK whenever the web host is running");

            // GET /health/live - Kubernetes-style liveness probe.
            // Returns 200 whenever the web host is running. This is a
            // pure process-aliveness signal: if Kestrel can answer, the
            // container is alive and the kubelet should not kill it.
            endpoints.MapGet("/health/live", () => Results.Ok(new
                {
                    status = "alive",
                    timestamp = DateTimeOffset.UtcNow
                }))
                .AllowAnonymous()
                .WithName("LivenessProbe")
                .WithDescription("Kubernetes liveness probe — 200 OK while the process is running");

            // GET /health/ready - Kubernetes-style readiness probe.
            // Returns 200 only when the bot is fully initialized AND
            // actively trading. Returns 503 when:
            //   * CoreService has not finished Start() yet, or
            //   * trading has been suspended (manual or by health rules), or
            //   * any IHealthCheck registered on IHealthCheckService is failing.
            // The payload always includes the per-component breakdown so
            // operators can see exactly which check flipped the probe.
            endpoints.MapGet("/health/ready", (
                ICoreService coreService,
                ITradingService tradingService,
                IHealthCheckService healthCheckService) =>
                {
                    var checks = healthCheckService.GetHealthChecks()
                        .OrderBy(c => c.Name)
                        .ToList();

                    var failingChecks = checks.Where(c => c.Failed).ToList();
                    var isRunning = coreService.Running;
                    var isSuspended = tradingService.IsTradingSuspended;
                    var isReady = isRunning && !isSuspended && failingChecks.Count == 0;

                    var payload = new
                    {
                        status = isReady ? "ready" : "not_ready",
                        timestamp = DateTimeOffset.UtcNow,
                        coreRunning = isRunning,
                        tradingSuspended = isSuspended,
                        failingCheckCount = failingChecks.Count,
                        checks = checks.Select(c => new
                        {
                            name = c.Name,
                            failed = c.Failed,
                            message = c.Message,
                            lastUpdated = c.LastUpdated
                        })
                    };

                    return isReady
                        ? Results.Ok(payload)
                        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
                })
                .AllowAnonymous()
                .WithName("ReadinessProbe")
                .WithDescription("Kubernetes readiness probe — 200 when bot is ready, 503 otherwise");

            var apiGroup = endpoints.MapGroup("/api")
                .RequireAuthorization();

            // GET /api/status - Get current trading status
            apiGroup.MapGet("/status", (
                ITradingService tradingService,
                ISignalsService signalsService,
                IHealthCheckService healthCheckService,
                ILoggingService loggingService) =>
            {
                var status = new
                {
                    Balance = tradingService.Account.GetBalance(),
                    GlobalRating = signalsService.GetGlobalRating()?.ToString("0.000") ?? "N/A",
                    TrailingBuys = tradingService.GetTrailingBuys(),
                    TrailingSells = tradingService.GetTrailingSells(),
                    TrailingSignals = signalsService.GetTrailingSignals(),
                    TradingSuspended = tradingService.IsTradingSuspended,
                    HealthChecks = healthCheckService.GetHealthChecks().OrderBy(c => c.Name),
                    LogEntries = loggingService.GetLogEntries().Reverse().Take(5)
                };
                return Results.Json(status);
            })
            .WithName("GetStatus")
            .WithDescription("Get current trading status including balance, ratings, and health checks");

            // POST /api/trading-pairs - Get all trading pairs with their details
            apiGroup.MapPost("/trading-pairs", (
                ITradingService tradingService,
                ISignalsService signalsService,
                ICoreService coreService) =>
            {
                var tradingPairs = from tradingPair in tradingService.Account.GetTradingPairs()
                                   let pairConfig = tradingService.GetPairConfig(tradingPair.Pair)
                                   select new
                                   {
                                       Name = tradingPair.Pair,
                                       DCA = tradingPair.DCALevel,
                                       TradingViewName = $"{tradingService.Config.Exchange.ToUpperInvariant()}:{tradingPair.Pair}",
                                       Margin = tradingPair.CurrentMargin.ToString("0.00"),
                                       Target = pairConfig.SellMargin.ToString("0.00"),
                                       CurrentPrice = tradingPair.CurrentPrice.ToString("0.00000000"),
                                       BoughtPrice = tradingPair.AveragePricePaid.ToString("0.00000000"),
                                       Cost = tradingPair.AverageCostPaid.ToString("0.00000000"),
                                       CurrentCost = tradingPair.CurrentCost.ToString("0.00000000"),
                                       Amount = tradingPair.TotalAmount.ToString("0.########"),
                                       OrderDates = tradingPair.OrderDates.Select(d => d.ToOffset(System.TimeSpan.FromHours(coreService.Config.TimezoneOffset)).ToString("yyyy-MM-dd HH:mm:ss")),
                                       OrderIds = tradingPair.OrderIds,
                                       Age = tradingPair.CurrentAge.ToString("0.00"),
                                       CurrentRating = tradingPair.Metadata.CurrentRating?.ToString("0.000") ?? "N/A",
                                       BoughtRating = tradingPair.Metadata.BoughtRating?.ToString("0.000") ?? "N/A",
                                       SignalRule = tradingPair.Metadata.SignalRule ?? "N/A",
                                       SwapPair = tradingPair.Metadata.SwapPair,
                                       TradingRules = pairConfig.Rules,
                                       IsTrailingSell = tradingService.GetTrailingSells().Contains(tradingPair.Pair),
                                       IsTrailingBuy = tradingService.GetTrailingBuys().Contains(tradingPair.Pair),
                                       LastBuyMargin = tradingPair.Metadata.LastBuyMargin?.ToString("0.00") ?? "N/A",
                                       Config = pairConfig
                                   };

                return Results.Json(tradingPairs);
            })
            .WithName("GetTradingPairs")
            .WithDescription("Get all active trading pairs with their current status and configuration");

            // POST /api/market-pairs - Get all market pairs with signals
            apiGroup.MapPost("/market-pairs", (
                HttpRequest request,
                ITradingService tradingService,
                ISignalsService signalsService) =>
            {
                // Read signals filter from request body
                var signalsFilter = new List<string>();

                var allSignals = signalsService.GetAllSignals();
                if (allSignals != null)
                {
                    if (signalsFilter.Count > 0)
                    {
                        allSignals = allSignals.Where(s => signalsFilter.Contains(s.Name));
                    }

                    var groupedSignals = allSignals.GroupBy(s => s.Pair).ToDictionary(g => g.Key, g => g.AsEnumerable());

                    var marketPairs = from signalGroup in groupedSignals
                                      let pair = signalGroup.Key
                                      let pairConfig = tradingService.GetPairConfig(pair)
                                      select new
                                      {
                                          Name = pair,
                                          TradingViewName = $"{tradingService.Config.Exchange.ToUpperInvariant()}:{pair}",
                                          VolumeList = signalGroup.Value.Select(s => new { s.Name, s.Volume }),
                                          VolumeChangeList = signalGroup.Value.Select(s => new { s.Name, s.VolumeChange }),
                                          PriceList = signalGroup.Value.Select(s => new { s.Name, s.Price }),
                                          PriceChangeList = signalGroup.Value.Select(s => new { s.Name, s.PriceChange }),
                                          RatingList = signalGroup.Value.Select(s => new { s.Name, s.Rating }),
                                          RatingChangeList = signalGroup.Value.Select(s => new { s.Name, s.RatingChange }),
                                          VolatilityList = signalGroup.Value.Select(s => new { s.Name, s.Volatility }),
                                          SignalRules = signalsService.GetTrailingInfo(pair)?.Select(ti => ti.Rule.Name) ?? Enumerable.Empty<string>(),
                                          HasTradingPair = tradingService.Account.HasTradingPair(pair),
                                          Config = pairConfig
                                      };

                    return Results.Json(marketPairs);
                }
                else
                {
                    return Results.Json((object?)null);
                }
            })
            .WithName("GetMarketPairs")
            .WithDescription("Get all market pairs with their signal data and configuration");

            // POST /api/market-pairs/filtered - Get market pairs with signals filter in body
            apiGroup.MapPost("/market-pairs/filtered", async (
                HttpRequest request,
                ITradingService tradingService,
                ISignalsService signalsService) =>
            {
                List<string>? signalsFilter = null;
                try
                {
                    signalsFilter = await request.ReadFromJsonAsync<List<string>>();
                }
                catch
                {
                    signalsFilter = new List<string>();
                }

                signalsFilter ??= new List<string>();

                var allSignals = signalsService.GetAllSignals();
                if (allSignals != null)
                {
                    if (signalsFilter.Count > 0)
                    {
                        allSignals = allSignals.Where(s => signalsFilter.Contains(s.Name));
                    }

                    var groupedSignals = allSignals.GroupBy(s => s.Pair).ToDictionary(g => g.Key, g => g.AsEnumerable());

                    var marketPairs = from signalGroup in groupedSignals
                                      let pair = signalGroup.Key
                                      let pairConfig = tradingService.GetPairConfig(pair)
                                      select new
                                      {
                                          Name = pair,
                                          TradingViewName = $"{tradingService.Config.Exchange.ToUpperInvariant()}:{pair}",
                                          VolumeList = signalGroup.Value.Select(s => new { s.Name, s.Volume }),
                                          VolumeChangeList = signalGroup.Value.Select(s => new { s.Name, s.VolumeChange }),
                                          PriceList = signalGroup.Value.Select(s => new { s.Name, s.Price }),
                                          PriceChangeList = signalGroup.Value.Select(s => new { s.Name, s.PriceChange }),
                                          RatingList = signalGroup.Value.Select(s => new { s.Name, s.Rating }),
                                          RatingChangeList = signalGroup.Value.Select(s => new { s.Name, s.RatingChange }),
                                          VolatilityList = signalGroup.Value.Select(s => new { s.Name, s.Volatility }),
                                          SignalRules = signalsService.GetTrailingInfo(pair)?.Select(ti => ti.Rule.Name) ?? Enumerable.Empty<string>(),
                                          HasTradingPair = tradingService.Account.HasTradingPair(pair),
                                          Config = pairConfig
                                      };

                    return Results.Json(marketPairs);
                }
                else
                {
                    return Results.Json((object?)null);
                }
            })
            .WithName("GetMarketPairsFiltered")
            .WithDescription("Get market pairs filtered by specific signal names");

            // GET /api/signal-names - Get all available signal names
            apiGroup.MapGet("/signal-names", (ISignalsService signalsService) =>
            {
                return Results.Json(signalsService.GetSignalNames());
            })
            .WithName("GetSignalNames")
            .WithDescription("Get all available signal names");

            return endpoints;
        }
    }
}
