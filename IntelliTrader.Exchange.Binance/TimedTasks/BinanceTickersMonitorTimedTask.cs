using IntelliTrader.Core;

#pragma warning disable CS0612 // Type or member is obsolete

namespace IntelliTrader.Exchange.Binance
{
    /// <summary>
    /// Monitors the Binance WebSocket ticker connection health.
    /// Triggers reconnection if ticker data becomes stale or connection is lost.
    /// </summary>
    internal class BinanceTickersMonitorTimedTask : HighResolutionTimedTask
    {
        private readonly ILoggingService _loggingService;
        private readonly BinanceExchangeService _binanceExchangeService;

        public BinanceTickersMonitorTimedTask(
            ILoggingService loggingService,
            BinanceExchangeService binanceExchangeService)
        {
            _loggingService = loggingService;
            _binanceExchangeService = binanceExchangeService;
        }

        public override void Run()
        {
            var timeSinceLastUpdate = _binanceExchangeService.GetTimeElapsedSinceLastTickersUpdate();

            // Check if ticker data is stale
            if (timeSinceLastUpdate.TotalSeconds > BinanceExchangeService.MaxTickersAgeToReconnectSeconds)
            {
                // If REST fallback is active, it means WebSocket already failed and reconnect attempts are in progress
                if (_binanceExchangeService.IsRestFallbackActive)
                {
                    _loggingService.Debug($"Tickers stale ({timeSinceLastUpdate.TotalSeconds:0}s), REST fallback is active - WebSocket reconnect will be attempted automatically");
                    return;
                }

                _loggingService.Info($"Binance Exchange max tickers age reached ({timeSinceLastUpdate.TotalSeconds:0}s), triggering reconnect...");

                // Disconnect and reconnect the WebSocket
                _binanceExchangeService.DisconnectTickersWebsocket();
                _binanceExchangeService.ConnectTickersWebsocket();
            }
            else if (_loggingService != null)
            {
                // Log connection health status periodically (debug level)
                var status = _binanceExchangeService.IsWebSocketConnected ? "WebSocket" :
                             _binanceExchangeService.IsRestFallbackActive ? "REST fallback" : "Unknown";
                _loggingService.Debug($"Ticker connection healthy via {status} (last update: {timeSinceLastUpdate.TotalSeconds:0.0}s ago)");
            }
        }
    }
}
