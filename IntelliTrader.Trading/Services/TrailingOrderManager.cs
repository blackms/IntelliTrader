using IntelliTrader.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Manages tracking of trailing buy and sell orders for UI display.
    /// Actual trailing logic is handled by TrailingManager in the Application layer.
    /// </summary>
    internal class TrailingOrderManager : ITrailingOrderManager
    {
        private readonly ILoggingService loggingService;
        private readonly ConcurrentDictionary<string, bool> trailingBuys;
        private readonly ConcurrentDictionary<string, bool> trailingSells;

        public TrailingOrderManager(ILoggingService loggingService)
        {
            this.loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            this.trailingBuys = new ConcurrentDictionary<string, bool>();
            this.trailingSells = new ConcurrentDictionary<string, bool>();
        }

        public bool InitiateTrailingBuy(string pair)
        {
            if (string.IsNullOrEmpty(pair))
            {
                return false;
            }

            if (trailingBuys.ContainsKey(pair))
            {
                return false;
            }

            // Cancel any active trailing sell for this pair
            trailingSells.TryRemove(pair, out _);

            if (trailingBuys.TryAdd(pair, true))
            {
                loggingService.Info($"Trailing buy initiated for {pair}");
                return true;
            }

            return false;
        }

        public bool InitiateTrailingSell(string pair)
        {
            if (string.IsNullOrEmpty(pair))
            {
                return false;
            }

            if (trailingSells.ContainsKey(pair))
            {
                return false;
            }

            // Cancel any active trailing buy for this pair
            trailingBuys.TryRemove(pair, out _);

            if (trailingSells.TryAdd(pair, true))
            {
                loggingService.Info($"Trailing sell initiated for {pair}");
                return true;
            }

            return false;
        }

        public void CancelTrailingBuy(string pair)
        {
            if (!string.IsNullOrEmpty(pair))
            {
                trailingBuys.TryRemove(pair, out _);
            }
        }

        public void CancelTrailingSell(string pair)
        {
            if (!string.IsNullOrEmpty(pair))
            {
                trailingSells.TryRemove(pair, out _);
            }
        }

        public void ClearAll()
        {
            trailingBuys.Clear();
            trailingSells.Clear();
        }

        public List<string> GetTrailingBuys()
        {
            return trailingBuys.Keys.ToList();
        }

        public List<string> GetTrailingSells()
        {
            return trailingSells.Keys.ToList();
        }

        public bool HasTrailingBuy(string pair)
        {
            return !string.IsNullOrEmpty(pair) && trailingBuys.ContainsKey(pair);
        }

        public bool HasTrailingSell(string pair)
        {
            return !string.IsNullOrEmpty(pair) && trailingSells.ContainsKey(pair);
        }
    }
}
