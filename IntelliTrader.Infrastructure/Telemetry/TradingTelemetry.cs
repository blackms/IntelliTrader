using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IntelliTrader.Infrastructure.Telemetry;

/// <summary>
/// Provides OpenTelemetry instrumentation for trading operations.
/// Contains ActivitySource for distributed tracing and Meter for custom metrics.
/// </summary>
public static class TradingTelemetry
{
    /// <summary>
    /// The service name used for telemetry.
    /// </summary>
    public const string ServiceName = "IntelliTrader";

    /// <summary>
    /// The version of the telemetry instrumentation.
    /// </summary>
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// ActivitySource for distributed tracing of trading operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(
        name: $"{ServiceName}.Trading",
        version: ServiceVersion);

    /// <summary>
    /// Meter for custom trading metrics.
    /// </summary>
    public static readonly Meter Meter = new(
        name: $"{ServiceName}.Trading",
        version: ServiceVersion);

    // -------------------------------------------------------------------------
    // Counters
    // -------------------------------------------------------------------------

    /// <summary>
    /// Counter for the total number of trades executed.
    /// </summary>
    public static readonly Counter<long> TradesExecutedCounter = Meter.CreateCounter<long>(
        name: "trades.executed",
        unit: "{trade}",
        description: "Total number of trades executed");

    /// <summary>
    /// Counter for the number of buy orders executed.
    /// </summary>
    public static readonly Counter<long> BuyOrdersCounter = Meter.CreateCounter<long>(
        name: "trades.buy_orders",
        unit: "{order}",
        description: "Total number of buy orders executed");

    /// <summary>
    /// Counter for the number of sell orders executed.
    /// </summary>
    public static readonly Counter<long> SellOrdersCounter = Meter.CreateCounter<long>(
        name: "trades.sell_orders",
        unit: "{order}",
        description: "Total number of sell orders executed");

    /// <summary>
    /// Counter for failed trade attempts.
    /// </summary>
    public static readonly Counter<long> TradesFailedCounter = Meter.CreateCounter<long>(
        name: "trades.failed",
        unit: "{trade}",
        description: "Total number of failed trade attempts");

    /// <summary>
    /// Counter for DCA (Dollar Cost Averaging) orders executed.
    /// </summary>
    public static readonly Counter<long> DCAOrdersCounter = Meter.CreateCounter<long>(
        name: "trades.dca_orders",
        unit: "{order}",
        description: "Total number of DCA orders executed");

    /// <summary>
    /// Counter for stop loss orders triggered.
    /// </summary>
    public static readonly Counter<long> StopLossTriggeredCounter = Meter.CreateCounter<long>(
        name: "trades.stop_loss_triggered",
        unit: "{order}",
        description: "Total number of stop loss orders triggered");

    /// <summary>
    /// Counter for trailing stops triggered.
    /// </summary>
    public static readonly Counter<long> TrailingTriggeredCounter = Meter.CreateCounter<long>(
        name: "trades.trailing_triggered",
        unit: "{order}",
        description: "Total number of trailing stops triggered");

    /// <summary>
    /// Counter for swap orders executed.
    /// </summary>
    public static readonly Counter<long> SwapOrdersCounter = Meter.CreateCounter<long>(
        name: "trades.swap_orders",
        unit: "{order}",
        description: "Total number of swap orders executed");

    /// <summary>
    /// Counter for signals received from signal providers.
    /// </summary>
    public static readonly Counter<long> SignalsReceivedCounter = Meter.CreateCounter<long>(
        name: "signals.received",
        unit: "{signal}",
        description: "Total number of signals received");

    /// <summary>
    /// Counter for exchange API calls made.
    /// </summary>
    public static readonly Counter<long> ExchangeApiCallsCounter = Meter.CreateCounter<long>(
        name: "exchange.api_calls",
        unit: "{call}",
        description: "Total number of exchange API calls");

    // -------------------------------------------------------------------------
    // Gauges (via ObservableGauge)
    // -------------------------------------------------------------------------

    private static int _openPositionsCount;
    private static decimal _totalPortfolioValue;
    private static int _activeBuyTrailingsCount;
    private static int _activeSellTrailingsCount;
    private static int _webSocketConnected;
    private static int _activeSignalCount;

    /// <summary>
    /// Observable gauge for the number of open positions.
    /// </summary>
    public static readonly ObservableGauge<int> OpenPositionsGauge = Meter.CreateObservableGauge(
        name: "positions.open",
        observeValue: () => _openPositionsCount,
        unit: "{position}",
        description: "Current number of open trading positions");

    /// <summary>
    /// Observable gauge for the total portfolio value.
    /// </summary>
    public static readonly ObservableGauge<decimal> PortfolioValueGauge = Meter.CreateObservableGauge(
        name: "portfolio.value",
        observeValue: () => _totalPortfolioValue,
        unit: "{currency}",
        description: "Current total portfolio value in quote currency");

    /// <summary>
    /// Observable gauge for active buy trailing stops.
    /// </summary>
    public static readonly ObservableGauge<int> ActiveBuyTrailingsGauge = Meter.CreateObservableGauge(
        name: "trailing.buy_active",
        observeValue: () => _activeBuyTrailingsCount,
        unit: "{trailing}",
        description: "Current number of active buy trailing stops");

    /// <summary>
    /// Observable gauge for active sell trailing stops.
    /// </summary>
    public static readonly ObservableGauge<int> ActiveSellTrailingsGauge = Meter.CreateObservableGauge(
        name: "trailing.sell_active",
        observeValue: () => _activeSellTrailingsCount,
        unit: "{trailing}",
        description: "Current number of active sell trailing stops");

    /// <summary>
    /// Observable gauge for WebSocket connection status (1 = connected, 0 = disconnected).
    /// </summary>
    public static readonly ObservableGauge<int> WebSocketStatusGauge = Meter.CreateObservableGauge(
        name: "exchange.websocket_connected",
        observeValue: () => _webSocketConnected,
        unit: "{status}",
        description: "WebSocket connection status (1=connected, 0=disconnected)");

    /// <summary>
    /// Observable gauge for the number of active signals being tracked.
    /// </summary>
    public static readonly ObservableGauge<int> ActiveSignalsGauge = Meter.CreateObservableGauge(
        name: "signals.active",
        observeValue: () => _activeSignalCount,
        unit: "{signal}",
        description: "Current number of active signals");

    /// <summary>
    /// Updates the open positions count for the gauge.
    /// </summary>
    public static void SetOpenPositionsCount(int count) => _openPositionsCount = count;

    /// <summary>
    /// Updates the total portfolio value for the gauge.
    /// </summary>
    public static void SetPortfolioValue(decimal value) => _totalPortfolioValue = value;

    /// <summary>
    /// Updates the active buy trailings count for the gauge.
    /// </summary>
    public static void SetActiveBuyTrailingsCount(int count) => _activeBuyTrailingsCount = count;

    /// <summary>
    /// Updates the active sell trailings count for the gauge.
    /// </summary>
    public static void SetActiveSellTrailingsCount(int count) => _activeSellTrailingsCount = count;

    /// <summary>
    /// Updates the WebSocket connection status (1 = connected, 0 = disconnected).
    /// </summary>
    public static void SetWebSocketConnected(bool connected) => _webSocketConnected = connected ? 1 : 0;

    /// <summary>
    /// Updates the active signal count for the gauge.
    /// </summary>
    public static void SetActiveSignalCount(int count) => _activeSignalCount = count;

    // -------------------------------------------------------------------------
    // Histograms
    // -------------------------------------------------------------------------

    /// <summary>
    /// Histogram for trade profit/loss percentage distribution.
    /// </summary>
    public static readonly Histogram<decimal> TradeProfitHistogram = Meter.CreateHistogram<decimal>(
        name: "trades.profit",
        unit: "%",
        description: "Distribution of trade profit/loss percentages");

    /// <summary>
    /// Histogram for order execution latency.
    /// </summary>
    public static readonly Histogram<double> OrderLatencyHistogram = Meter.CreateHistogram<double>(
        name: "order.latency",
        unit: "ms",
        description: "Order execution latency in milliseconds");

    /// <summary>
    /// Histogram for position hold duration.
    /// </summary>
    public static readonly Histogram<double> PositionDurationHistogram = Meter.CreateHistogram<double>(
        name: "position.duration",
        unit: "h",
        description: "Position hold duration in hours");

    /// <summary>
    /// Histogram for trade cost distribution.
    /// </summary>
    public static readonly Histogram<decimal> TradeCostHistogram = Meter.CreateHistogram<decimal>(
        name: "trades.cost",
        unit: "{currency}",
        description: "Distribution of trade costs in quote currency");

    /// <summary>
    /// Histogram for exchange API call latency.
    /// </summary>
    public static readonly Histogram<double> ExchangeApiLatencyHistogram = Meter.CreateHistogram<double>(
        name: "exchange.api_latency",
        unit: "ms",
        description: "Exchange API call latency in milliseconds");

    /// <summary>
    /// Histogram for signal processing time.
    /// </summary>
    public static readonly Histogram<double> SignalProcessingTimeHistogram = Meter.CreateHistogram<double>(
        name: "signals.processing_time",
        unit: "ms",
        description: "Signal processing time in milliseconds");

    // -------------------------------------------------------------------------
    // Activity (Span) Creation Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts a new activity for a buy order operation.
    /// </summary>
    public static Activity? StartBuyOrderActivity(string pair, decimal price, decimal quantity)
    {
        var activity = ActivitySource.StartActivity(
            name: "BuyOrder",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair", pair);
        activity?.SetTag("trading.price", price);
        activity?.SetTag("trading.quantity", quantity);
        activity?.SetTag("trading.order_type", "buy");

        return activity;
    }

    /// <summary>
    /// Starts a new activity for a sell order operation.
    /// </summary>
    public static Activity? StartSellOrderActivity(string pair, decimal price, decimal quantity)
    {
        var activity = ActivitySource.StartActivity(
            name: "SellOrder",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair", pair);
        activity?.SetTag("trading.price", price);
        activity?.SetTag("trading.quantity", quantity);
        activity?.SetTag("trading.order_type", "sell");

        return activity;
    }

    /// <summary>
    /// Starts a new activity for processing trading rules.
    /// </summary>
    public static Activity? StartRuleProcessingActivity(int pairCount)
    {
        var activity = ActivitySource.StartActivity(
            name: "ProcessTradingRules",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair_count", pairCount);

        return activity;
    }

    /// <summary>
    /// Starts a new activity for processing a single position.
    /// </summary>
    public static Activity? StartPositionProcessingActivity(string pair, decimal currentMargin)
    {
        var activity = ActivitySource.StartActivity(
            name: "ProcessPosition",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair", pair);
        activity?.SetTag("trading.margin", currentMargin);

        return activity;
    }

    /// <summary>
    /// Starts a new activity for trailing stop processing.
    /// </summary>
    public static Activity? StartTrailingProcessingActivity(string pair, string trailingType)
    {
        var activity = ActivitySource.StartActivity(
            name: "ProcessTrailing",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair", pair);
        activity?.SetTag("trading.trailing_type", trailingType);

        return activity;
    }

    /// <summary>
    /// Starts a new activity for DCA order processing.
    /// </summary>
    public static Activity? StartDCAActivity(string pair, int dcaLevel)
    {
        var activity = ActivitySource.StartActivity(
            name: "DCAOrder",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair", pair);
        activity?.SetTag("trading.dca_level", dcaLevel);
        activity?.SetTag("trading.order_type", "dca");

        return activity;
    }

    /// <summary>
    /// Starts a new activity for stop loss processing.
    /// </summary>
    public static Activity? StartStopLossActivity(string pair, decimal margin)
    {
        var activity = ActivitySource.StartActivity(
            name: "StopLoss",
            kind: ActivityKind.Internal);

        activity?.SetTag("trading.pair", pair);
        activity?.SetTag("trading.margin", margin);
        activity?.SetTag("trading.order_type", "stop_loss");

        return activity;
    }

    // -------------------------------------------------------------------------
    // Metric Recording Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records a successful trade execution.
    /// </summary>
    public static void RecordTradeExecuted(
        string pair,
        string orderType,
        bool isVirtual,
        decimal? profitPercentage = null,
        double? latencyMs = null,
        decimal? cost = null,
        double? durationHours = null)
    {
        var tags = new TagList
        {
            { "pair", pair },
            { "order_type", orderType },
            { "virtual", isVirtual }
        };

        TradesExecutedCounter.Add(1, tags);

        if (orderType == "buy")
        {
            BuyOrdersCounter.Add(1, tags);
        }
        else if (orderType == "sell")
        {
            SellOrdersCounter.Add(1, tags);
        }

        if (profitPercentage.HasValue)
        {
            TradeProfitHistogram.Record(profitPercentage.Value, tags);
        }

        if (latencyMs.HasValue)
        {
            OrderLatencyHistogram.Record(latencyMs.Value, tags);
        }

        if (cost.HasValue)
        {
            TradeCostHistogram.Record(cost.Value, tags);
        }

        if (durationHours.HasValue)
        {
            PositionDurationHistogram.Record(durationHours.Value, tags);
        }
    }

    /// <summary>
    /// Records a failed trade attempt.
    /// </summary>
    public static void RecordTradeFailed(string pair, string orderType, string reason)
    {
        var tags = new TagList
        {
            { "pair", pair },
            { "order_type", orderType },
            { "reason", reason }
        };

        TradesFailedCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a DCA order execution.
    /// </summary>
    public static void RecordDCAOrder(string pair, int dcaLevel, bool isVirtual)
    {
        var tags = new TagList
        {
            { "pair", pair },
            { "dca_level", dcaLevel },
            { "virtual", isVirtual }
        };

        DCAOrdersCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a stop loss trigger.
    /// </summary>
    public static void RecordStopLossTriggered(string pair, decimal margin)
    {
        var tags = new TagList
        {
            { "pair", pair },
            { "margin", margin }
        };

        StopLossTriggeredCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a trailing stop trigger.
    /// </summary>
    public static void RecordTrailingTriggered(string pair, string trailingType)
    {
        var tags = new TagList
        {
            { "pair", pair },
            { "trailing_type", trailingType }
        };

        TrailingTriggeredCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a swap order execution.
    /// </summary>
    public static void RecordSwapOrder(string oldPair, string newPair, bool isVirtual)
    {
        var tags = new TagList
        {
            { "old_pair", oldPair },
            { "new_pair", newPair },
            { "virtual", isVirtual }
        };

        SwapOrdersCounter.Add(1, tags);
        TradesExecutedCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a signal received from a signal provider.
    /// </summary>
    public static void RecordSignalReceived(string signalName, string pair)
    {
        var tags = new TagList
        {
            { "signal_name", signalName },
            { "pair", pair }
        };

        SignalsReceivedCounter.Add(1, tags);
    }

    /// <summary>
    /// Records signal processing time.
    /// </summary>
    public static void RecordSignalProcessingTime(string signalName, double elapsedMs)
    {
        var tags = new TagList
        {
            { "signal_name", signalName }
        };

        SignalProcessingTimeHistogram.Record(elapsedMs, tags);
    }

    /// <summary>
    /// Records an exchange API call with latency.
    /// </summary>
    public static void RecordExchangeApiCall(string operation, double latencyMs, bool success = true)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "success", success }
        };

        ExchangeApiCallsCounter.Add(1, tags);
        ExchangeApiLatencyHistogram.Record(latencyMs, tags);
    }
}
