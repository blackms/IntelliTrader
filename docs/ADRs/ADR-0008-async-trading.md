# ADR-0008: Async/Await Throughout Trading Layer

## Status
Accepted

## Context
Trading operations involve I/O-bound calls to external systems:

1. Exchange API calls for order placement (network latency: 50-500ms)
2. Balance queries and trade history retrieval
3. WebSocket operations for price streaming
4. Notification delivery (Telegram, email)

The original implementation used synchronous wrappers around async APIs (`GetAwaiter().GetResult()`), which:
- Blocks thread pool threads during I/O waits
- Can cause thread pool exhaustion under load
- Increases latency by preventing concurrent operations

## Decision
We implemented async/await throughout the trading layer, providing both sync and async versions of methods for backward compatibility. New code should use async methods exclusively.

Async trading methods from `TradingService.cs`:
```csharp
// Preferred async methods
public async Task BuyAsync(BuyOptions options, CancellationToken cancellationToken = default)
{
    if (CanBuy(options, out string message))
    {
        await InitiateBuyAsync(options, cancellationToken).ConfigureAwait(false);
    }
    else
    {
        loggingService.Debug(message);
    }
}

private async Task<IOrderDetails> PlaceBuyOrderAsync(BuyOptions options, CancellationToken ct = default)
{
    IPairConfig pairConfig = GetPairConfig(options.Pair);
    decimal currentPrice = await GetCurrentPriceAsync(options.Pair).ConfigureAwait(false);

    var orderRequest = new BuyOrder { ... };

    // Non-blocking I/O
    orderDetails = await Account.PlaceOrderAsync(orderRequest, options.Metadata, ct)
        .ConfigureAwait(false);

    return orderDetails;
}

// Sync wrappers for backward compatibility
public void Buy(BuyOptions options)
{
    lock (SyncRoot) { /* ... sync implementation ... */ }
}

// Exchange operations - async preferred
public async Task<decimal> GetCurrentPriceAsync(string pair)
{
    return await exchangeService.GetLastPrice(pair).ConfigureAwait(false);
}

// Sync wrapper (legacy)
public decimal GetCurrentPrice(string pair)
{
    return exchangeService.GetLastPrice(pair).GetAwaiter().GetResult();
}
```

Background services using async from `Infrastructure` layer:
```csharp
public class TradingRuleProcessorService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessTradingRulesAsync(stoppingToken);
            await Task.Delay(_config.CheckInterval * 1000, stoppingToken);
        }
    }
}
```

Notification service async pattern:
```csharp
public async Task StartAsync()
{
    // Fire-and-forget notification - doesn't block core service startup
    _ = notificationService.StartAsync();
}

public void Start()
{
    // ... other services ...
    if (notificationService.Config.Enabled)
    {
        // Non-blocking startup
        _ = notificationService.StartAsync();
    }
}
```

## Alternatives Considered

1. **Fully Synchronous Implementation**
   - Pros: Simpler mental model, no async virus
   - Cons: Thread pool exhaustion, poor scalability
   - Rejected: I/O-bound operations demand async for performance

2. **Sync-Over-Async Everywhere**
   - Pros: No code changes to callers
   - Cons: Deadlock risks, thread pool blocking
   - Rejected: Anti-pattern that defeats async benefits

3. **Async-Only (No Sync Wrappers)**
   - Pros: Clean API, no confusion
   - Cons: Breaking change for existing code
   - Rejected: Backward compatibility needed during transition

4. **Task.Run for Everything**
   - Pros: Easy to wrap sync code
   - Cons: Unnecessary thread pool usage for I/O
   - Rejected: ConfigureAwait(false) is more appropriate for library code

## Consequences

### Positive
- **Non-Blocking I/O**: Thread pool threads released during network waits
- **Scalability**: Can handle more concurrent operations
- **Cancellation Support**: CancellationToken propagated through call chain
- **Responsiveness**: WebSocket events and notifications don't block trading
- **Background Services**: Clean shutdown via CancellationToken

### Negative
- **Async Virus**: Async propagates up the call stack
- **Sync/Async Duality**: Maintaining both versions increases code surface
- **ConfigureAwait Noise**: `.ConfigureAwait(false)` on every await
- **Testing Complexity**: Async tests require await or GetAwaiter()

## How to Validate

1. **No Thread Pool Exhaustion**:
   ```bash
   # Run under load, monitor ThreadPool.GetAvailableThreads()
   # Verify available threads remain > 0
   ```

2. **Cancellation Propagation**:
   ```csharp
   var cts = new CancellationTokenSource();
   var buyTask = tradingService.BuyAsync(options, cts.Token);
   cts.Cancel();
   await Assert.ThrowsAsync<OperationCanceledException>(() => buyTask);
   ```

3. **Concurrent Operations**:
   ```csharp
   var tasks = Enumerable.Range(0, 10)
       .Select(_ => tradingService.GetCurrentPriceAsync("BTCUSDT"));
   var results = await Task.WhenAll(tasks);  // Should complete faster than sequential
   ```

4. **Background Service Shutdown**:
   ```csharp
   // Stop host
   await host.StopAsync();
   // Verify no hanging operations, clean exit
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Trading/Services/TradingService.cs` - Async trading methods (BuyAsync, SellAsync, SwapAsync)
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/Services/BinanceExchangeService.cs` - Async exchange operations
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Infrastructure/BackgroundServices/TradingRuleProcessorService.cs` - Async background service
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Services/CoreService.cs` - Fire-and-forget notification startup
