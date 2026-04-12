using IntelliTrader.Core;
using IntelliTrader.Signals.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

#pragma warning disable CS0612 // Type or member is obsolete
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelliTrader.Signals.TradingView
{
    internal class TradingViewCryptoSignalPollingTimedTask : HighResolutionTimedTask
    {
        private const int HISTORICAL_SIGNALS_SNAPSHOT_MIN_INTERVAL_SECONDS = 45;
        private const int HISTORICAL_SIGNALS_ADDITIONAL_SAVE_MINUTES = 5;
        private const int HISTORICAL_SIGNALS_MAX_ADDITIONAL_ELAPSED_MINUTES = 1;

        private readonly ILoggingService loggingService;
        private readonly IHealthCheckService healthCheckService;
        private readonly ITradingService tradingService;
        private readonly TradingViewCryptoSignalReceiver signalReceiver;
        private readonly HttpClient httpClient;

        private readonly ConcurrentDictionary<DateTimeOffset, List<Signal>> signalsHistory = new ConcurrentDictionary<DateTimeOffset, List<Signal>>();
        private DateTimeOffset lastSnapshotDate;
        private List<Signal>? signals;
        private double? averageRating;

        private readonly object syncRoot = new object();

        public TradingViewCryptoSignalPollingTimedTask(ILoggingService loggingService, IHealthCheckService healthCheckService,
            ITradingService tradingService, TradingViewCryptoSignalReceiver signalReceiver)
        {
            this.loggingService = loggingService;
            this.healthCheckService = healthCheckService;
            this.tradingService = tradingService;
            this.signalReceiver = signalReceiver;
            this.httpClient = CreateHttpClient();
        }

        public override void Run()
        {
            // Run async implementation synchronously since base class requires sync Run()
            // Using GetAwaiter().GetResult() instead of .Result to preserve exception stack traces
            RunAsync().GetAwaiter().GetResult();
        }

        private async Task RunAsync()
        {
            var requestData = signalReceiver.Config.RequestData
                .Replace("%EXCHANGE%", tradingService.Config.Exchange.ToUpper())
                .Replace("%MARKET%", tradingService.Config.Market)
                .Replace("%PERIOD%", signalReceiver.Config.SignalPeriod <= 240 ? $"|{signalReceiver.Config.SignalPeriod}" : "")
                .Replace("%VOLATILITY%", $".{signalReceiver.Config.VolatilityPeriod?[0] ?? 'W'}");

            var requestContent = new StringContent(requestData, Encoding.UTF8, "application/json");
            try
            {
                using (var response = await httpClient.PostAsync(signalReceiver.Config.RequestUrl, requestContent).ConfigureAwait(false))
                {
                    var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // Parse using System.Text.Json
                    using var document = JsonDocument.Parse(responseContent);
                    var dataArray = document.RootElement.GetProperty("data");

                    lock (syncRoot)
                    {
                        List<Signal>? historicalSignals = GetHistoricalSignals();
                        var parsedSignals = new List<Signal?>();

                        foreach (var item in dataArray.EnumerateArray())
                        {
                            try
                            {
                                if (!item.TryGetProperty("d", out var signalArray) || signalArray.ValueKind != JsonValueKind.Array)
                                {
                                    continue;
                                }

                                var signal = ParseSignalFromArray(signalArray);
                                if (signal != null && signal.Pair != null && signal.Pair.EndsWith(tradingService.Config.Market))
                                {
                                    signal.Name = signalReceiver.SignalName;

                                    var historicalSignal = historicalSignals?.FirstOrDefault(s => s.Pair == signal.Pair);
                                    if (historicalSignal != null)
                                    {
                                        signal.VolumeChange = CalculatePercentageChange(historicalSignal.Volume, signal.Volume);
                                        signal.RatingChange = CalculatePercentageChange(historicalSignal.Rating, signal.Rating);
                                    }
                                    parsedSignals.Add(signal);
                                }
                            }
                            catch (Exception ex)
                            {
                                loggingService.Debug("Unable to parse Trading View Crypto Signal", ex);
                            }
                        }

                        signals = parsedSignals.Where(s => s != null && s.Pair != null).Cast<Signal>().ToList();

                        if (signals.Count > 0)
                        {
                            if ((DateTimeOffset.Now - lastSnapshotDate).TotalSeconds > HISTORICAL_SIGNALS_SNAPSHOT_MIN_INTERVAL_SECONDS)
                            {
                                signalsHistory.TryAdd(DateTimeOffset.Now, signals);
                                lastSnapshotDate = DateTimeOffset.Now;
                                CleanUpSignalsHistory();
                            }
                            averageRating = signals.Any(s => s.Rating.HasValue) ? signals.Where(s => s.Rating.HasValue).Average(s => s.Rating) : null;
                            healthCheckService.UpdateHealthCheck($"{Constants.HealthChecks.TradingViewCryptoSignalsReceived} [{signalReceiver.SignalName}]", $"Total: {signals.Count}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                loggingService.Debug("Unable to retrieve TV Signals", ex);
            }
        }

        /// <summary>
        /// Parses a Signal from a JSON array in the format: [pair, price, priceChange, volume, rating, volatility]
        /// </summary>
        private static Signal? ParseSignalFromArray(JsonElement array)
        {
            var signal = new Signal();

            var index = 0;
            foreach (var element in array.EnumerateArray())
            {
                switch (index)
                {
                    case 0:
                        signal.Pair = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                        break;
                    case 1:
                        signal.Price = element.ValueKind == JsonValueKind.Number ? element.GetDecimal() : null;
                        break;
                    case 2:
                        signal.PriceChange = element.ValueKind == JsonValueKind.Number ? element.GetDecimal() : null;
                        break;
                    case 3:
                        signal.Volume = element.ValueKind == JsonValueKind.Number ? element.GetInt64() : null;
                        break;
                    case 4:
                        signal.Rating = element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;
                        break;
                    case 5:
                        signal.Volatility = element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;
                        break;
                }
                index++;
            }

            return signal;
        }

        public IEnumerable<ISignal>? GetSignals()
        {
            lock (syncRoot)
            {
                return signals;
            }
        }

        public double? GetAverageRating()
        {
            lock (syncRoot)
            {
                return averageRating;
            }
        }

        private List<Signal>? GetHistoricalSignals()
        {
            lock (syncRoot)
            {
                foreach (var date in signalsHistory.Keys.OrderByDescending(d => d))
                {
                    double elapsedMinutes = (DateTimeOffset.Now - date).TotalMinutes;
                    if (elapsedMinutes >= signalReceiver.Config.SignalPeriod && (elapsedMinutes - signalReceiver.Config.SignalPeriod) <= HISTORICAL_SIGNALS_MAX_ADDITIONAL_ELAPSED_MINUTES)
                    {
                        return signalsHistory[date];
                    }
                }
                return null;
            }
        }

        private void CleanUpSignalsHistory()
        {
            lock (syncRoot)
            {
                // Create snapshot of keys to avoid modifying collection while iterating
                var keysSnapshot = signalsHistory.Keys.ToList();
                foreach (var date in keysSnapshot)
                {
                    if ((DateTimeOffset.Now - date).TotalMinutes > signalReceiver.Config.SignalPeriod + HISTORICAL_SIGNALS_ADDITIONAL_SAVE_MINUTES)
                    {
                        signalsHistory.TryRemove(date, out List<Signal>? _);
                    }
                }
            }
        }

        private double? CalculatePercentageChange(double? a, double? b)
        {
            if (a != null && b != null)
            {
                if (a == 0 && b == 0 || a == b)
                {
                    return 0;
                }
                else if (a == 0)
                {
                    return 100 * Math.Sign((double)b);
                }
                else if (b == 0)
                {
                    return -100 * Math.Sign((double)a);
                }
                else
                {
                    var change = Math.Abs((double)((b - a) / a * 100));
                    return (a < b) ? change : change * -1;
                }
            }
            else
            {
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                httpClient?.Dispose();
            }
            base.Dispose(disposing);
        }

        private HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache, no-store, max-age=0, must-revalidate");
            return httpClient;
        }
    }
}
