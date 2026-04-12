# ADR-0016: IHostedService Migration

## Status

In Progress

## Context

IntelliTrader uses a custom `HighResolutionTimedTask` base class for periodic background work (health checks, ticker monitoring, signal polling, trading rule evaluation, etc.). This class manages its own dedicated `Thread`, a `ManualResetEvent` for stopping, and a `Stopwatch` for precision timing. A companion `LowResolutionTimedTask` uses `System.Timers.Timer` for lower-frequency work.

These tasks are managed by `CoreService` via `AddTask`/`RemoveTask`/`StartTask`/`StopTask` methods and stored in a `ConcurrentDictionary<string, HighResolutionTimedTask>`.

The .NET ecosystem has standardized on `IHostedService` / `BackgroundService` for long-running background work. The Infrastructure layer already uses this pattern for `TradingRuleProcessorService`, `SignalRuleProcessorService`, `OrderExecutionService`, and `TimedBackgroundService`.

## Decision

Incrementally migrate timed tasks from `HighResolutionTimedTask` / `LowResolutionTimedTask` to `BackgroundService`, one task at a time, starting with the least critical.

### Migration strategy

1. **Mark legacy classes as `[Obsolete]`** to prevent new code from inheriting them.
2. **Migrate one task at a time**, starting with the simplest and least critical for trading.
3. **Do NOT remove** `HighResolutionTimedTask` or `LowResolutionTimedTask` until all consumers have been migrated.
4. **Do NOT change** `CoreService.AddTask`/`RemoveTask`/etc. until all tasks using them have been migrated.

### Migration order (recommended)

| Priority | Task | Status |
|----------|------|--------|
| 1 | `HealthCheckTimedTask` | **Migrated** to `HealthCheckBackgroundService` |
| 2 | `AlertingTimedTask` | Pending |
| 3 | `TradingViewCryptoSignalPollingTimedTask` | Pending |
| 4 | `BinanceTickersMonitorTimedTask` | Pending |
| 5 | `BacktestingLoadSnapshotsTimedTask` | Pending |
| 6 | `BacktestingSaveSnapshotsTimedTask` | Pending |

### Already using BackgroundService (Infrastructure layer)

- `TimedBackgroundService` (abstract base)
- `TradingRuleProcessorService`
- `SignalRuleProcessorService`
- `OrderExecutionService`
- `SignalRBroadcasterService` (Web layer)

### Pattern for migration

Since the application uses a custom Autofac container (not the .NET Generic Host), `IHostedService` instances are not auto-started by the framework. Each migrated task's owning service manages the `BackgroundService` lifecycle directly:

```csharp
// In the owning service's Start() method:
_cts = new CancellationTokenSource();
_backgroundService = new MyBackgroundService(/* deps */);
_backgroundService.StartAsync(_cts.Token);

// In the owning service's Stop() method:
_cts.Cancel();
_backgroundService.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
_cts.Dispose();
_backgroundService.Dispose();
```

This preserves the existing service lifecycle management while using the modern `BackgroundService` pattern internally.

## Consequences

### Positive
- New tasks use the standard .NET pattern, improving familiarity for contributors.
- `BackgroundService` uses `Task.Delay` instead of dedicated threads, reducing thread pool pressure.
- Proper `CancellationToken` propagation enables cooperative shutdown.
- Aligns with the Infrastructure layer's existing pattern.

### Negative
- Incremental migration means two patterns coexist temporarily.
- `[Obsolete]` warnings appear in existing code (suppressed with `#pragma warning disable CS0612`).

### Risks
- Trading-critical tasks (`BinanceTickersMonitorTimedTask`, signal/trading rules) must be migrated with extreme care and thorough testing, as timing differences could affect trading behavior.
- `HighResolutionTimedTask` uses a dedicated thread with `ThreadPriority` control; `BackgroundService` uses the thread pool, which may have different scheduling characteristics under load.
