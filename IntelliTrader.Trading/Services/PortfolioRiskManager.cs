using IntelliTrader.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelliTrader.Trading
{
    /// <summary>
    /// Manages portfolio-level risk including heat tracking, circuit breakers, and daily loss limits.
    /// </summary>
    internal class PortfolioRiskManager : IPortfolioRiskManager
    {
        private readonly ILoggingService _loggingService;
        private readonly Lazy<ITradingService> _lazyTradingService;

        private readonly ConcurrentDictionary<string, decimal> _positionRisks = new ConcurrentDictionary<string, decimal>();
        private readonly object _syncRoot = new object();

        private decimal _peakEquity;
        private decimal _dailyStartingBalance;
        private decimal _dailyRealizedProfitLoss;
        private DateTimeOffset _dailyResetDate;
        private bool _circuitBreakerTriggered;
        private bool _initialized;

        private const string LogScope = "RiskManager";

        // Lazy accessor for trading service to avoid circular dependency at construction time
        private ITradingService TradingService => _lazyTradingService.Value;

        // Config accessor through trading service
        private IRiskManagementConfig Config => TradingService?.Config?.RiskManagement;

        public PortfolioRiskManager(
            ILoggingService loggingService,
            Lazy<ITradingService> tradingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _lazyTradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        }

        private void EnsureInitialized()
        {
            if (!_initialized && TradingService?.Account != null)
            {
                InitializeDailyTracking();
                _initialized = true;
            }
        }

        /// <inheritdoc />
        public decimal GetCurrentHeat()
        {
            EnsureInitialized();

            if (Config == null || !Config.Enabled)
            {
                return 0m;
            }

            return _positionRisks.Values.Sum();
        }

        /// <inheritdoc />
        public bool CanOpenPosition(decimal additionalRisk)
        {
            EnsureInitialized();

            if (Config == null || !Config.Enabled)
            {
                return true;
            }

            // Check circuit breaker first
            if (IsCircuitBreakerTriggered())
            {
                _loggingService.Debug($"[{LogScope}] Position blocked: circuit breaker triggered");
                return false;
            }

            // Check daily loss limit
            if (IsDailyLossLimitReached())
            {
                _loggingService.Debug($"[{LogScope}] Position blocked: daily loss limit reached");
                return false;
            }

            // Check portfolio heat
            decimal currentHeat = GetCurrentHeat();
            decimal projectedHeat = currentHeat + additionalRisk;

            if (projectedHeat > Config.MaxPortfolioHeat)
            {
                _loggingService.Debug($"[{LogScope}] Position blocked: portfolio heat would exceed limit " +
                    $"(current: {currentHeat:0.00}%, additional: {additionalRisk:0.00}%, max: {Config.MaxPortfolioHeat:0.00}%)");
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public void RegisterPosition(string pair, decimal riskAmount)
        {
            if (Config == null || !Config.Enabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(pair))
            {
                throw new ArgumentException("Pair cannot be null or empty", nameof(pair));
            }

            _positionRisks.AddOrUpdate(pair, riskAmount, (key, oldValue) => oldValue + riskAmount);
            _loggingService.Debug($"[{LogScope}] Registered position risk for {pair}: {riskAmount:0.00}%, " +
                $"total heat: {GetCurrentHeat():0.00}%");
        }

        /// <inheritdoc />
        public void ClosePosition(string pair)
        {
            if (Config == null || !Config.Enabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(pair))
            {
                return;
            }

            if (_positionRisks.TryRemove(pair, out decimal removedRisk))
            {
                _loggingService.Debug($"[{LogScope}] Closed position risk for {pair}: {removedRisk:0.00}%, " +
                    $"remaining heat: {GetCurrentHeat():0.00}%");
            }
        }

        /// <inheritdoc />
        public bool IsCircuitBreakerTriggered()
        {
            EnsureInitialized();

            if (Config == null || !Config.Enabled || !Config.CircuitBreakerEnabled)
            {
                return false;
            }

            // Check if already triggered
            if (_circuitBreakerTriggered)
            {
                return true;
            }

            // Check current drawdown
            decimal currentDrawdown = GetCurrentDrawdown();
            if (currentDrawdown >= Config.MaxDrawdownPercent)
            {
                lock (_syncRoot)
                {
                    if (!_circuitBreakerTriggered)
                    {
                        _circuitBreakerTriggered = true;
                        _loggingService.Info($"[{LogScope}] CIRCUIT BREAKER TRIGGERED - " +
                            $"Drawdown {currentDrawdown:0.00}% exceeds limit {Config.MaxDrawdownPercent:0.00}%");
                    }
                }
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public decimal GetCurrentDrawdown()
        {
            EnsureInitialized();

            if (Config == null || !Config.Enabled)
            {
                return 0m;
            }

            decimal currentEquity = GetCurrentEquity();

            lock (_syncRoot)
            {
                if (_peakEquity <= 0)
                {
                    _peakEquity = currentEquity;
                    return 0m;
                }

                if (currentEquity >= _peakEquity)
                {
                    _peakEquity = currentEquity;
                    return 0m;
                }

                return ((_peakEquity - currentEquity) / _peakEquity) * 100m;
            }
        }

        /// <inheritdoc />
        public decimal GetDailyProfitLoss()
        {
            EnsureInitialized();

            if (Config == null || !Config.Enabled)
            {
                return 0m;
            }

            EnsureDailyReset();

            if (_dailyStartingBalance <= 0)
            {
                return 0m;
            }

            decimal currentEquity = GetCurrentEquity();
            decimal unrealizedProfitLoss = currentEquity - _dailyStartingBalance;
            decimal totalProfitLoss = _dailyRealizedProfitLoss + unrealizedProfitLoss;

            return (totalProfitLoss / _dailyStartingBalance) * 100m;
        }

        /// <inheritdoc />
        public bool IsDailyLossLimitReached()
        {
            EnsureInitialized();

            if (Config == null || !Config.Enabled)
            {
                return false;
            }

            decimal dailyPL = GetDailyProfitLoss();
            return dailyPL <= -Config.DailyLossLimitPercent;
        }

        /// <inheritdoc />
        public IDictionary<string, decimal> GetRiskMetrics()
        {
            EnsureInitialized();

            return new Dictionary<string, decimal>
            {
                ["CurrentHeat"] = GetCurrentHeat(),
                ["MaxPortfolioHeat"] = Config?.MaxPortfolioHeat ?? 0m,
                ["CurrentDrawdown"] = GetCurrentDrawdown(),
                ["MaxDrawdownPercent"] = Config?.MaxDrawdownPercent ?? 0m,
                ["DailyProfitLoss"] = GetDailyProfitLoss(),
                ["DailyLossLimit"] = Config?.DailyLossLimitPercent ?? 0m,
                ["OpenPositions"] = _positionRisks.Count,
                ["CircuitBreakerTriggered"] = _circuitBreakerTriggered ? 1m : 0m,
                ["PeakEquity"] = _peakEquity,
                ["DailyStartingBalance"] = _dailyStartingBalance
            };
        }

        /// <inheritdoc />
        public void ResetCircuitBreaker()
        {
            lock (_syncRoot)
            {
                if (_circuitBreakerTriggered)
                {
                    _circuitBreakerTriggered = false;
                    _loggingService.Info($"[{LogScope}] Circuit breaker manually reset");
                }
            }
        }

        /// <inheritdoc />
        public void UpdatePeakEquity(decimal currentEquity)
        {
            if (Config == null || !Config.Enabled)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (currentEquity > _peakEquity)
                {
                    _peakEquity = currentEquity;
                }
            }
        }

        /// <inheritdoc />
        public void RecordTrade(decimal profitLoss)
        {
            if (Config == null || !Config.Enabled)
            {
                return;
            }

            EnsureDailyReset();

            lock (_syncRoot)
            {
                _dailyRealizedProfitLoss += profitLoss;
            }

            _loggingService.Debug($"[{LogScope}] Recorded trade P/L: {profitLoss:0.00000000}, " +
                $"daily realized: {_dailyRealizedProfitLoss:0.00000000}");
        }

        /// <summary>
        /// Calculates the risk amount for a position based on the cost and stop loss.
        /// </summary>
        /// <param name="positionCost">The cost of the position.</param>
        /// <param name="stopLossPercent">The stop loss percentage (as positive number).</param>
        /// <returns>The risk as a percentage of total capital.</returns>
        public decimal CalculatePositionRisk(decimal positionCost, decimal? stopLossPercent = null)
        {
            decimal totalCapital = GetCurrentEquity();

            if (totalCapital <= 0)
            {
                return 0m;
            }

            // If no stop loss provided, use the position cost percentage as risk
            // Otherwise, calculate risk based on stop loss distance
            decimal risk;
            if (stopLossPercent.HasValue && stopLossPercent.Value > 0)
            {
                // Risk = (Position Size / Total Capital) * Stop Loss %
                risk = (positionCost / totalCapital) * stopLossPercent.Value;
            }
            else
            {
                // Default: use configured default position risk or calculate from position size
                decimal positionPercent = (positionCost / totalCapital) * 100m;
                risk = Math.Min(positionPercent, Config?.DefaultPositionRiskPercent ?? 1.0m);
            }

            return risk;
        }

        /// <summary>
        /// Synchronizes position risks with current trading pairs.
        /// Call this periodically to ensure risk tracking is accurate.
        /// </summary>
        public void SyncPositionsWithAccount()
        {
            if (Config == null || !Config.Enabled || TradingService?.Account == null)
            {
                return;
            }

            var currentPairs = TradingService.Account.GetTradingPairs()
                .Select(p => p.Pair)
                .ToHashSet();

            // Remove positions that no longer exist
            var positionsToRemove = _positionRisks.Keys
                .Where(pair => !currentPairs.Contains(pair))
                .ToList();

            foreach (var pair in positionsToRemove)
            {
                ClosePosition(pair);
            }

            // Add positions that exist but aren't tracked
            foreach (var tradingPair in TradingService.Account.GetTradingPairs())
            {
                if (!_positionRisks.ContainsKey(tradingPair.Pair))
                {
                    decimal risk = CalculatePositionRisk(tradingPair.CurrentCost);
                    RegisterPosition(tradingPair.Pair, risk);
                }
            }
        }

        private decimal GetCurrentEquity()
        {
            if (TradingService?.Account == null)
            {
                return 0m;
            }

            decimal balance = TradingService.Account.GetBalance();
            decimal positionsValue = 0m;

            foreach (var pair in TradingService.Account.GetTradingPairs())
            {
                positionsValue += pair.CurrentCost;
            }

            return balance + positionsValue;
        }

        private void InitializeDailyTracking()
        {
            _dailyResetDate = DateTimeOffset.UtcNow.Date;
            _dailyStartingBalance = GetCurrentEquity();
            _dailyRealizedProfitLoss = 0m;
            _peakEquity = _dailyStartingBalance;
        }

        private void EnsureDailyReset()
        {
            DateTimeOffset today = DateTimeOffset.UtcNow.Date;

            if (_dailyResetDate < today)
            {
                lock (_syncRoot)
                {
                    if (_dailyResetDate < today)
                    {
                        _dailyResetDate = today;
                        _dailyStartingBalance = GetCurrentEquity();
                        _dailyRealizedProfitLoss = 0m;
                        _loggingService.Debug($"[{LogScope}] Daily tracking reset. Starting balance: {_dailyStartingBalance:0.00000000}");
                    }
                }
            }
        }
    }
}
