using ExchangeSharp;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace IntelliTrader.Exchange.Binance
{
    internal class BinanceExchangeService : ExchangeService
    {
        public const int MAX_TICKERS_AGE_TO_RECONNECT_SECONDS = 60;

        // Retry policy configuration
        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);

        private ExchangeBinanceAPI binanceApi;
        private IDisposable socket;
        private ConcurrentDictionary<string, Ticker> tickers;
        private BinanceTickersMonitorTimedTask tickersMonitorTimedTask;
        private DateTimeOffset lastTickersUpdate;
        private bool tickersChecked;

        // Polly retry pipeline for exchange operations
        private readonly ResiliencePipeline _resiliencePipeline;

        public BinanceExchangeService(ILoggingService loggingService, IHealthCheckService healthCheckService, ICoreService coreService) :
            base(loggingService, healthCheckService, coreService)
        {
            // Build resilience pipeline with exponential backoff retry
            _resiliencePipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = MaxRetryAttempts,
                    Delay = InitialRetryDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>()
                        .Handle<TimeoutException>()
                        .Handle<Exception>(ex =>
                            // Retry on rate limit or server errors
                            ex.Message.Contains("429") ||
                            ex.Message.Contains("503") ||
                            ex.Message.Contains("502") ||
                            ex.Message.Contains("500")),
                    OnRetry = args =>
                    {
                        loggingService.Info($"Retrying exchange operation (attempt {args.AttemptNumber + 1}/{MaxRetryAttempts}) after {args.RetryDelay.TotalSeconds:0.0}s delay. Reason: {args.Outcome.Exception?.Message ?? "Unknown"}");
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        public override void Start(bool virtualTrading)
        {
            loggingService.Info("Start Binance Exchange service...");

            binanceApi = new ExchangeBinanceAPI();
            binanceApi.RateLimit = new RateGate(Config.RateLimitOccurences, TimeSpan.FromSeconds(Config.RateLimitTimeframe));

            if (!virtualTrading && !String.IsNullOrWhiteSpace(Config.KeysPath))
            {
                if (File.Exists(Config.KeysPath))
                {
                    loggingService.Info("Load keys from encrypted file...");
                    binanceApi.LoadAPIKeys(Config.KeysPath);
                }
                else
                {
                    throw new FileNotFoundException("Keys file not found");
                }
            }

            loggingService.Info("Get initial ticker values...");
            var initialTickers = binanceApi.GetTickersAsync().GetAwaiter().GetResult();
            tickers = new ConcurrentDictionary<string, Ticker>(initialTickers.Select(t => new KeyValuePair<string, Ticker>(t.Key, new Ticker
            {
                Pair = t.Key,
                AskPrice = t.Value.Ask,
                BidPrice = t.Value.Bid,
                LastPrice = t.Value.Last
            })));
            lastTickersUpdate = DateTimeOffset.Now;
            healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, $"Updates: {tickers.Count}");
            ConnectTickersWebsocket();

            loggingService.Info("Binance Exchange service started");
        }

        public override void Stop()
        {
            loggingService.Info("Stop Binance Exchange service...");

            DisconnectTickersWebsocket();
            lastTickersUpdate = DateTimeOffset.MinValue;
            tickers.Clear();
            healthCheckService.RemoveHealthCheck(Constants.HealthChecks.TickersUpdated);

            loggingService.Info("Binance Exchange service stopped");
        }

        public void ConnectTickersWebsocket()
        {
            try
            {
                loggingService.Info("Connect to Binance Exchange tickers...");
                socket = binanceApi.GetTickersWebSocketAsync(OnTickersUpdated).GetAwaiter().GetResult();
                loggingService.Info("Connected to Binance Exchange tickers");

                tickersMonitorTimedTask = new BinanceTickersMonitorTimedTask(loggingService, this);
                tickersMonitorTimedTask.RunInterval = MAX_TICKERS_AGE_TO_RECONNECT_SECONDS / 2;
                coreService.AddTask(nameof(BinanceTickersMonitorTimedTask), tickersMonitorTimedTask);
            }
            catch (Exception ex)
            {
                loggingService.Error("Unable to connect to Binance Exchange tickers", ex);
            }
        }

        public void DisconnectTickersWebsocket()
        {
            try
            {
                coreService.StopTask(nameof(BinanceTickersMonitorTimedTask));
                coreService.RemoveTask(nameof(BinanceTickersMonitorTimedTask));

                loggingService.Info("Disconnect from Binance Exchange tickers...");
                // Give Dispose 10 seconds to complete and then time out if not
                Task.Run(() => socket.Dispose()).Wait(TimeSpan.FromSeconds(10));
                socket = null;
                loggingService.Info("Disconnected from Binance Exchange tickers");
            }
            catch (Exception ex)
            {
                loggingService.Error("Unable to disconnect from Binance Exchange tickers", ex);
            }
        }

        public override Task<IEnumerable<ITicker>> GetTickers(string market)
        {
            return Task.FromResult(tickers.Values.Where(t => t.Pair.EndsWith(market)).Select(t => (ITicker)t));
        }

        public override Task<IEnumerable<string>> GetMarketPairs(string market)
        {
            return Task.FromResult(tickers.Keys.Where(t => t.EndsWith(market)));
        }

        public override async Task<Dictionary<string, decimal>> GetAvailableAmounts()
        {
            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var results = await binanceApi.GetAmountsAvailableToTradeAsync();
                return results;
            });
        }

        public override async Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair)
        {
            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var myTrades = new List<OrderDetails>();
                var results = await binanceApi.GetMyTradesAsync(pair);

                foreach (var result in results)
                {
                    myTrades.Add(new OrderDetails
                    {
                        Side = result.IsBuy ? OrderSide.Buy : OrderSide.Sell,
                        Result = (OrderResult)(int)result.Result,
                        Date = result.OrderDate,
                        OrderId = result.OrderId,
                        Pair = result.MarketSymbol,
                        Message = result.Message,
                        Amount = result.Amount,
                        AmountFilled = result.AmountFilled ?? 0m,
                        Price = result.Price ?? 0m,
                        AveragePrice = result.AveragePrice ?? 0m,
                        Fees = result.Fees ?? 0m,
                        FeesCurrency = result.FeesCurrency
                    });
                }

                return (IEnumerable<IOrderDetails>)myTrades;
            });
        }

        public override async Task<decimal> GetLastPrice(string pair)
        {
            if (tickers.TryGetValue(pair, out Ticker ticker))
            {
                return ticker.LastPrice;
            }
            else
            {
                return 0;
            }
        }

        public override async Task<IOrderDetails> PlaceOrder(IOrder order)
        {
            // Note: Order placement uses retry policy carefully.
            // Retries only on connection/timeout errors, not on order rejection.
            return await _resiliencePipeline.ExecuteAsync(async cancellationToken =>
            {
                var result = await binanceApi.PlaceOrderAsync(new ExchangeOrderRequest
                {
                    OrderType = (ExchangeSharp.OrderType)(int)order.Type,
                    IsBuy = order.Side == OrderSide.Buy,
                    Amount = order.Amount,
                    Price = order.Price,
                    MarketSymbol = order.Pair
                });

                return (IOrderDetails)new OrderDetails
                {
                    Side = result.IsBuy ? OrderSide.Buy : OrderSide.Sell,
                    Result = (OrderResult)(int)result.Result,
                    Date = result.OrderDate,
                    OrderId = result.OrderId,
                    Pair = result.MarketSymbol,
                    Message = result.Message,
                    Amount = result.Amount,
                    AmountFilled = result.AmountFilled ?? 0m,
                    Price = result.Price ?? 0m,
                    AveragePrice = result.AveragePrice ?? 0m,
                    Fees = result.Fees ?? 0m,
                    FeesCurrency = result.FeesCurrency
                };
            });
        }

        public TimeSpan GetTimeElapsedSinceLastTickersUpdate()
        {
            return DateTimeOffset.Now - lastTickersUpdate;
        }

        private void OnTickersUpdated(IReadOnlyCollection<KeyValuePair<string, ExchangeTicker>> updatedTickers)
        {
            if (!tickersChecked)
            {
                loggingService.Info("Ticker updates are working, good!");
                tickersChecked = true;
            }

            healthCheckService.UpdateHealthCheck(Constants.HealthChecks.TickersUpdated, $"Updates: {updatedTickers.Count}");

            lastTickersUpdate = DateTimeOffset.Now;

            foreach (var update in updatedTickers)
            {
                if (tickers.TryGetValue(update.Key, out Ticker ticker))
                {
                    ticker.AskPrice = update.Value.Ask;
                    ticker.BidPrice = update.Value.Bid;
                    ticker.LastPrice = update.Value.Last;
                }
                else
                {
                    tickers.TryAdd(update.Key, new Ticker
                    {
                        Pair = update.Key,
                        AskPrice = update.Value.Ask,
                        BidPrice = update.Value.Bid,
                        LastPrice = update.Value.Last
                    });
                }
            }
        }
    }
}
