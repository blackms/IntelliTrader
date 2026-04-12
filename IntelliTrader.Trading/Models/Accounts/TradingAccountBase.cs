using IntelliTrader.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Trading
{
    internal abstract class TradingAccountBase : ITradingAccount, IDisposable
    {
        private readonly object _syncRoot = new object();
        public object SyncRoot => _syncRoot;

        protected readonly ILoggingService loggingService;
        protected readonly INotificationService notificationService;
        protected readonly IHealthCheckService healthCheckService;
        protected readonly ISignalsService signalsService;
        protected readonly ITradingService tradingService;

        protected bool isInitialRefresh = true;
        protected decimal balance;
        protected ConcurrentDictionary<string, TradingPair> tradingPairs = new ConcurrentDictionary<string, TradingPair>();

        public TradingAccountBase(ILoggingService loggingService, INotificationService notificationService, IHealthCheckService healthCheckService, ISignalsService signalsService, ITradingService tradingService)
        {
            this.loggingService = loggingService;
            this.notificationService = notificationService;
            this.healthCheckService = healthCheckService;
            this.signalsService = signalsService;
            this.tradingService = tradingService;
        }

        public abstract void Refresh();

        public abstract Task RefreshAsync(CancellationToken cancellationToken = default);

        public abstract void Save();

        public virtual void AddOrder(IOrderDetails order)
        {
            if (order.Side == OrderSide.Buy)
            {
                AddBuyOrder(order);
            }
            else
            {
                AddSellOrder(order);
            }
        }

        public virtual void AddBuyOrder(IOrderDetails order)
        {
            lock (SyncRoot)
            {
                if (order.Side == OrderSide.Buy && (order.Result == OrderResult.Filled || order.Result == OrderResult.FilledPartially))
                {
                    decimal balanceDifference = -order.AverageCost;
                    decimal feesPairCurrency = 0;
                    decimal feesMarketCurrency = 0;
                    decimal amountAfterFees = order.AmountFilled;

                    if (order.Fees != 0 && order.FeesCurrency != null)
                    {
                        if (order.FeesCurrency == tradingService.Config.Market)
                        {
                            feesMarketCurrency = order.Fees;
                            balanceDifference -= order.Fees;
                        }
                        else
                        {
                            string feesPair = order.FeesCurrency + tradingService.Config.Market;
                            if (feesPair == order.Pair)
                            {
                                feesPairCurrency = order.Fees;
                                amountAfterFees -= order.Fees;
                            }
                            else
                            {
                                feesMarketCurrency = tradingService.GetCurrentPrice(feesPair) * order.Fees;
                            }
                        }
                    }
                    balance += balanceDifference;

                    if (tradingPairs.TryGetValue(order.Pair, out TradingPair tradingPair))
                    {
                        if (!tradingPair.OrderIds.Contains(order.OrderId))
                        {
                            tradingPair.OrderIds.Add(order.OrderId);
                            tradingPair.OrderDates.Add(order.Date);
                        }
                        // Fix: denominator must use amountAfterFees to match TotalAmount accumulation.
                        // Example: existing 50 units at cost 100, new order 10 units with 0.1 fee =>
                        //   amountAfterFees = 9.9, cost = 110 => avgPrice = 210 / 59.9 = 3.506
                        //   Using order.AmountFilled (10) would give 210 / 60 = 3.500 (wrong)
                        tradingPair.AveragePricePaid = (tradingPair.AverageCostPaid + order.AverageCost) / (tradingPair.TotalAmount + amountAfterFees);
                        tradingPair.FeesPairCurrency += feesPairCurrency;
                        tradingPair.FeesMarketCurrency += feesMarketCurrency;
                        tradingPair.TotalAmount += amountAfterFees;
                        tradingPair.Metadata = tradingPair.Metadata.MergeWith(order.Metadata);
                    }
                    else
                    {
                        tradingPair = new TradingPair
                        {
                            Pair = order.Pair,
                            OrderIds = new List<string> { order.OrderId },
                            OrderDates = new List<DateTimeOffset> { order.Date },
                            AveragePricePaid = order.AveragePrice,
                            FeesPairCurrency = feesPairCurrency,
                            FeesMarketCurrency = feesMarketCurrency,
                            TotalAmount = amountAfterFees,
                            Metadata = order.Metadata
                        };
                        tradingPairs.TryAdd(order.Pair, tradingPair);
                        tradingPair.SetCurrentPrice(tradingService.GetCurrentPrice(tradingPair.Pair));
                        tradingPair.Metadata.CurrentRating = tradingPair.Metadata.Signals != null ? signalsService.GetRating(tradingPair.Pair, tradingPair.Metadata.Signals) : null;
                        tradingPair.Metadata.CurrentGlobalRating = signalsService.GetGlobalRating();
                    }
                }
            }
        }

        public virtual ITradeResult AddSellOrder(IOrderDetails order)
        {
            lock (SyncRoot)
            {
                if (tradingPairs.TryGetValue(order.Pair, out TradingPair tradingPair))
                {
                    if (order.Side == OrderSide.Sell && (order.Result == OrderResult.Filled || order.Result == OrderResult.FilledPartially))
                    {
                        decimal balanceDifference = order.AverageCost;

                        decimal sellFeesMarketCurrency = 0;
                        if (order.Fees != 0 && order.FeesCurrency != null)
                        {
                            if (order.FeesCurrency == tradingService.Config.Market)
                            {
                                sellFeesMarketCurrency = order.Fees;
                                tradingPair.FeesMarketCurrency += order.Fees;
                                balanceDifference -= order.Fees;
                            }
                            else
                            {
                                string feesPair = order.FeesCurrency + tradingService.Config.Market;
                                if (feesPair == order.Pair)
                                {
                                    // Fees in pair currency reduce the effective sell proceeds
                                    balanceDifference -= tradingService.GetCurrentPrice(feesPair) * order.Fees;
                                }
                                sellFeesMarketCurrency = tradingService.GetCurrentPrice(feesPair) * order.Fees;
                                tradingPair.FeesMarketCurrency += sellFeesMarketCurrency;
                            }
                        }
                        balance += balanceDifference;

                        decimal additionalCosts = tradingPair.Metadata.AdditionalCosts ?? 0;
                        decimal sellRatio = order.AmountFilled / tradingPair.TotalAmount;

                        // Correct partial sell profit formula:
                        // profit = sell proceeds - proportional cost basis - sell fees
                        // Example: bought 100 units for 1000 total, additional costs 10, selling 25 units for 300:
                        //   proportional cost = (1000 + 10) * (25 / 100) = 252.5
                        //   profit = 300 - 252.5 - sellFees
                        decimal proportionalCostBasis = (tradingPair.AverageCostPaid + additionalCosts) * sellRatio;
                        decimal profit = order.AverageCost - proportionalCostBasis - sellFeesMarketCurrency;

                        var tradeResult = new TradeResult
                        {
                            IsSuccessful = true,
                            Metadata = order.Metadata,
                            Pair = order.Pair,
                            Amount = order.AmountFilled,
                            OrderDates = tradingPair.OrderDates,
                            AveragePricePaid = tradingPair.AveragePricePaid,
                            FeesPairCurrency = tradingPair.FeesPairCurrency,
                            FeesMarketCurrency = tradingPair.FeesMarketCurrency,
                            SellDate = order.Date,
                            SellPrice = order.AveragePrice,
                            BalanceDifference = balanceDifference,
                            Profit = profit
                        };

                        if (tradingPair.TotalAmount > order.AmountFilled)
                        {
                            tradingPair.TotalAmount -= order.AmountFilled;

                            if (!isInitialRefresh && tradingPair.AverageCostPaid <= tradingService.Config.MinCost)
                            {
                                tradingPairs.TryRemove(order.Pair, out tradingPair);
                            }
                        }
                        else
                        {
                            tradingPairs.TryRemove(order.Pair, out tradingPair);
                        }

                        return tradeResult;
                    }
                    else
                    {
                        return new TradeResult { IsSuccessful = false };
                    }
                }
                else
                {
                    return new TradeResult { IsSuccessful = false };
                }
            }
        }

        public decimal GetBalance()
        {
            lock (SyncRoot)
            {
                return balance;
            }
        }

        public bool HasTradingPair(string pair)
        {
            lock (SyncRoot)
            {
                return tradingPairs.ContainsKey(pair);
            }
        }

        public ITradingPair GetTradingPair(string pair)
        {
            lock (SyncRoot)
            {
                if (tradingPairs.TryGetValue(pair, out TradingPair tradingPair))
                {
                    return tradingPair;
                }
                else
                {
                    return null;
                }
            }
        }

        public IEnumerable<ITradingPair> GetTradingPairs()
        {
            lock (SyncRoot)
            {
                // Return a snapshot to allow safe iteration outside the lock
                return tradingPairs.Values.ToList<ITradingPair>();
            }
        }

        public virtual IOrderDetails PlaceOrder(IOrder order, OrderMetadata metadata)
        {
            lock (SyncRoot)
            {
                var orderDetails = tradingService.PlaceOrder(order);
                if (orderDetails != null && metadata != null)
                {
                    orderDetails.SetMetadata(metadata);
                }

                if (orderDetails != null && (orderDetails.Result == OrderResult.Filled || orderDetails.Result == OrderResult.FilledPartially))
                {
                    AddOrder(orderDetails);
                    Save();
                }

                return orderDetails;
            }
        }

        public virtual async Task<IOrderDetails> PlaceOrderAsync(IOrder order, OrderMetadata metadata, CancellationToken cancellationToken = default)
        {
            // Use async exchange method for non-blocking I/O
            var orderDetails = await tradingService.PlaceOrderAsync(order).ConfigureAwait(false);

            lock (SyncRoot)
            {
                if (orderDetails != null && metadata != null)
                {
                    orderDetails.SetMetadata(metadata);
                }

                if (orderDetails != null && (orderDetails.Result == OrderResult.Filled || orderDetails.Result == OrderResult.FilledPartially))
                {
                    AddOrder(orderDetails);
                    Save();
                }

                return orderDetails;
            }
        }

        private bool disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    lock (SyncRoot)
                    {
                        Save();
                        tradingPairs.Clear();
                    }
                }
                disposed = true;
            }
        }
    }
}
