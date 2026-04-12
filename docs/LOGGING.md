# Logging Conventions

This document describes the structured logging conventions used in IntelliTrader.

## Property Names

Use constants from `LogProperties` (in `IntelliTrader.Core/Models/LogProperties.cs`) for all structured log properties:

| Constant | Value | Usage |
|---|---|---|
| `CorrelationId` | `"CorrelationId"` | Links related log entries across an operation |
| `Pair` | `"Pair"` | Trading pair symbol (e.g., `"BTCUSDT"`) |
| `Operation` | `"Operation"` | Name of the operation being performed |
| `Duration` | `"DurationMs"` | Elapsed time in milliseconds |
| `ServiceName` | `"ServiceName"` | Service emitting the log entry |
| `Signal` | `"Signal"` | Signal name |
| `OrderSide` | `"OrderSide"` | Buy or Sell |
| `OrderType` | `"OrderType"` | Market, Limit, etc. |
| `Amount` | `"Amount"` | Trading quantity |
| `Price` | `"Price"` | Price value |
| `Rule` | `"Rule"` | Rule name that triggered an action |
| `Success` | `"Success"` | Whether the operation succeeded |
| `ErrorCode` | `"ErrorCode"` | Error code for failures |

## Correlation IDs

Use `BeginCorrelationScope()` to group related log entries:

```csharp
using (loggingService.BeginCorrelationScope())
{
    loggingService.Info("Starting signal evaluation for {Pair}", pair);
    // All log entries within this block share the same CorrelationId
    loggingService.Info("Signal evaluation complete for {Pair}", pair);
}
```

You can also pass an explicit correlation ID:

```csharp
using (loggingService.BeginCorrelationScope("order-12345"))
{
    // ...
}
```

## Operation Timing

Use `TimeOperation()` to automatically log start/end with duration:

```csharp
using (loggingService.TimeOperation("BuyOrder"))
{
    // Logs: "Operation BuyOrder started"
    await ExecuteBuyAsync();
    // On dispose, logs: "Operation BuyOrder completed in 142ms"
}
```

## Log Sampling

For high-volume events (ticker updates, signal polls), use sampled logging to reduce noise:

```csharp
// Logs only every 100th ticker update
loggingService.InfoSampled("TickerUpdate", 100,
    "Ticker updated: {Pair} price={Price}", pair, price);

// Logs only every 50th signal poll
loggingService.DebugSampled("SignalPoll", 50,
    "Signal polled: {Signal}", signalName);
```

## JSON Output (Production)

Enable JSON-formatted log output in `config/logging.json` for production environments:

```json
{
  "Logging": {
    "Enabled": true,
    "JsonOutputEnabled": true,
    "JsonOutputPath": "log/structured-.json"
  }
}
```

When enabled, a Serilog `JsonFormatter` sink writes structured JSON logs to the specified path with daily rolling and 31-day retention. This format is suitable for ingestion by ELK, Datadog, Splunk, or similar systems.

## Scoped Context

Use `BeginScope` to add contextual properties to all log entries within a block:

```csharp
using (loggingService.BeginScope(LogProperties.Pair, "BTCUSDT"))
using (loggingService.BeginScope(LogProperties.ServiceName, "TradingService"))
{
    loggingService.Info("Processing pair");
    // Log entry includes: Pair=BTCUSDT, ServiceName=TradingService
}
```

## Log Levels

| Level | Usage |
|---|---|
| `Verbose` | Detailed diagnostic information, only for development |
| `Debug` | Internal state useful during debugging |
| `Info` | Normal operation milestones (order placed, signal received) |
| `Warning` | Unexpected but recoverable situations |
| `Error` | Operation failures that need attention |
| `Fatal` | Application-level failures requiring immediate action |
