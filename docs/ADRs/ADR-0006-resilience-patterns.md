# ADR-0006: Polly Resilience Patterns for Exchange Operations

## Status
Accepted

## Context
Cryptocurrency exchange APIs are unreliable:

1. Rate limits (HTTP 429) during high market volatility
2. Transient failures (502, 503) during exchange maintenance
3. Network issues causing connection resets (ECONNRESET)
4. WebSocket disconnections requiring fallback to REST

Order operations are particularly sensitive - retrying a buy order on timeout risks placing duplicate orders, potentially losing significant funds. Read operations (price queries, balance checks) are idempotent and can be retried aggressively.

## Decision
We implemented Polly resilience pipelines with separate strategies for read and order operations. The `ExchangeResiliencePipelines` class creates pipelines combining timeout, circuit breaker, concurrency limiting, and retry policies.

Pipeline creation from `ExchangeResiliencePipelines.cs`:
```csharp
public class ExchangeResiliencePipelines
{
    public ResiliencePipeline ReadPipeline { get; }    // Aggressive retry
    public ResiliencePipeline OrderPipeline { get; }   // Conservative (max 1 retry)
    public ResiliencePipeline WebSocketPipeline { get; }

    private ResiliencePipeline CreateReadPipeline()
    {
        return new ResiliencePipelineBuilder()
            // 1. Timeout - prevents indefinite waits
            .AddTimeout(new TimeoutStrategyOptions {
                Timeout = TimeSpan.FromSeconds(_config.ReadTimeoutSeconds)
            })
            // 2. Concurrency limiter (bulkhead)
            .AddConcurrencyLimiter(new ConcurrencyLimiterOptions {
                PermitLimit = _config.MaxConcurrentReads,
                QueueLimit = _config.ReadQueueLimit
            })
            // 3. Circuit breaker - fails fast when service is unhealthy
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions {
                FailureRatio = _config.CircuitBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 10,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            // 4. Retry with exponential backoff and jitter
            .AddRetry(new RetryStrategyOptions {
                MaxRetryAttempts = _config.ReadMaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true  // Prevents thundering herd
            })
            .Build();
    }

    private ResiliencePipeline CreateOrderPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddTimeout(...) // 15s - shorter for orders
            .AddConcurrencyLimiter(...) // Strict limit on concurrent orders
            .AddCircuitBreaker(...) // 30% failure ratio (stricter)
            .AddRetry(new RetryStrategyOptions {
                MaxRetryAttempts = 1,  // CRITICAL: Only 1 retry
                // ONLY retry on connection errors, NOT on HTTP responses
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => IsConnectionError(ex))
            })
            .Build();
    }
}
```

Pipeline usage in `BinanceExchangeService.cs`:
```csharp
public override async Task<Dictionary<string, decimal>> GetAvailableAmounts()
{
    return await _resiliencePipelines.ReadPipeline.ExecuteAsync(async cancellationToken =>
    {
        return await _binanceApi.GetAmountsAvailableToTradeAsync();
    });
}

public override async Task<IOrderDetails> PlaceOrder(IOrder order)
{
    // CRITICAL: Use OrderPipeline for non-idempotent operations
    return await _resiliencePipelines.OrderPipeline.ExecuteAsync(async cancellationToken =>
    {
        var result = await _binanceApi.PlaceOrderAsync(...);
        return new OrderDetails { ... };
    });
}
```

## Alternatives Considered

1. **Simple Try-Catch with Fixed Retries**
   - Pros: Easy to understand, no dependencies
   - Cons: No exponential backoff, no circuit breaker, same policy for all operations
   - Rejected: Too simplistic for production trading

2. **Custom Resilience Implementation**
   - Pros: Full control, no external dependency
   - Cons: Reinventing the wheel, likely bugs
   - Rejected: Polly is battle-tested and widely used

3. **No Resilience (Fail Fast)**
   - Pros: Simpler code, predictable behavior
   - Cons: Single failures cascade, poor user experience
   - Rejected: Exchange API flakiness makes this unacceptable

4. **Queue-Based Order Handling**
   - Pros: Guaranteed delivery, at-least-once semantics
   - Cons: Complexity, still need duplicate detection
   - Rejected: Polly's conservative retry is simpler for our use case

## Consequences

### Positive
- **Differentiated Policies**: Read operations retry aggressively; orders retry once only
- **Circuit Breaker**: Prevents hammering failed exchanges, allows recovery
- **Jitter**: Exponential backoff with jitter prevents thundering herd on recovery
- **Observability**: Polly callbacks log retry attempts, circuit state changes
- **Bulkhead**: Concurrency limits prevent overwhelming exchange APIs

### Negative
- **Complexity**: Pipeline configuration requires understanding Polly internals
- **Order Timeout Risk**: 15s timeout on orders may still result in filled-but-unknown state
- **Circuit Breaker Tuning**: Wrong thresholds can block legitimate operations
- **Dependency**: Polly library must be kept up-to-date

## How to Validate

1. **Retry Verification** (Read Operations):
   ```bash
   # Simulate 502 responses from exchange
   # Verify logs show: "[Resilience] Retrying exchange read (attempt 2/3)"
   ```

2. **Circuit Breaker Trigger**:
   ```bash
   # Simulate 50%+ failure rate for 60 seconds
   # Verify: "[Resilience] Circuit breaker OPENED for exchange reads"
   ```

3. **Order Retry Safety**:
   ```csharp
   // Simulate timeout on PlaceOrder
   // Verify only 1 retry attempted (check logs)
   // Verify warning: "Order operation timed out - CHECK ORDER STATUS MANUALLY!"
   ```

4. **Connection Error Detection**:
   ```csharp
   // Only ECONNRESET, ETIMEDOUT, etc. should trigger order retry
   // HTTP 400 (bad request) should NOT trigger retry
   Assert.True(IsConnectionError(new HttpRequestException("ECONNRESET")));
   Assert.False(IsConnectionError(new HttpRequestException("400 Bad Request")));
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/Resilience/ExchangeResiliencePipelines.cs` - Pipeline definitions
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/Config/ResilienceConfig.cs` - Configuration values
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/Services/BinanceExchangeService.cs` - Pipeline usage
- https://github.com/App-vNext/Polly - Polly library documentation
