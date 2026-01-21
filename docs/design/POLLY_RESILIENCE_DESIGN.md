# Polly Resilience Patterns Design Document

## IntelliTrader Exchange Integration

**Version:** 1.0
**Date:** January 2026
**Status:** Design Phase

---

## Executive Summary

This document outlines the comprehensive resilience strategy for IntelliTrader's Binance exchange integration using Polly v8. The design addresses transient failures, rate limiting, circuit breaking, and proper isolation for a cryptocurrency trading application where reliability and latency are critical.

---

## Table of Contents

1. [Current State Analysis](#1-current-state-analysis)
2. [Binance API Rate Limits](#2-binance-api-rate-limits)
3. [Resilience Strategy Overview](#3-resilience-strategy-overview)
4. [Policy Definitions](#4-policy-definitions)
5. [Implementation Patterns](#5-implementation-patterns)
6. [Configuration Values](#6-configuration-values)
7. [NuGet Packages](#7-nuget-packages)
8. [Testing Strategy](#8-testing-strategy)
9. [Monitoring and Observability](#9-monitoring-and-observability)

---

## 1. Current State Analysis

### Existing Implementation

The codebase already has a basic Polly retry pipeline in `BinanceExchangeService.cs`:

```csharp
_resiliencePipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = Constants.RetryPolicy.MaxRetryAttempts,  // 3
        Delay = TimeSpan.FromMilliseconds(Constants.RetryPolicy.InitialRetryDelayMs),  // 1000ms
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .Handle<TimeoutException>()
            .Handle<Exception>(ex =>
                ex.Message.Contains("429") ||
                ex.Message.Contains("503") ||
                ex.Message.Contains("502") ||
                ex.Message.Contains("500")),
        OnRetry = args => { /* logging */ }
    })
    .Build();
```

### Identified Gaps

| Gap | Risk | Priority |
|-----|------|----------|
| No circuit breaker | Continued calls to failing service | High |
| No bulkhead isolation | Resource exhaustion from concurrent calls | High |
| No timeout policy | Hung connections consume resources | High |
| Single retry pipeline for all operations | Order placement may retry inappropriately | Critical |
| No jitter in exponential backoff | Thundering herd on retry | Medium |
| No rate limiter integration | IP bans from Binance | High |
| WebSocket lacks resilience | Connection failures not handled gracefully | Medium |

### Operations Requiring Resilience

| Operation | Method | Criticality | Idempotent |
|-----------|--------|-------------|------------|
| Get Available Amounts | `GetAvailableAmounts()` | Medium | Yes |
| Get My Trades | `GetMyTrades()` | Medium | Yes |
| Place Order | `PlaceOrder()` | **Critical** | **No** |
| Get Tickers (REST fallback) | `FetchTickersViaRestAsync()` | High | Yes |
| WebSocket Connection | `ConnectAsync()` | High | Yes |

---

## 2. Binance API Rate Limits

### Request Weight System

Binance uses a weight-based rate limiting system where each API endpoint has an assigned weight. The limits are tracked per IP address.

| Limit Type | Default Value | Notes |
|------------|---------------|-------|
| Request Weight | 1200/minute | Most endpoints use 1-10 weight |
| Order Count | 10/second, 100,000/day | Per account |
| Raw Requests | 6000/5 min | Hard limit on raw requests |

### Response Headers to Monitor

```
X-MBX-USED-WEIGHT-1M: <current_weight_used>
X-MBX-ORDER-COUNT-1S: <orders_this_second>
X-MBX-ORDER-COUNT-1D: <orders_today>
Retry-After: <seconds_to_wait>
```

### HTTP Status Codes

| Code | Meaning | Action |
|------|---------|--------|
| 429 | Rate limit exceeded | Backoff using `Retry-After` header |
| 418 | IP banned | Stop all requests, wait ban duration |
| 5xx | Server error | Retry with exponential backoff |
| 4xx (except 429) | Client error | Do NOT retry (except specific cases) |

### Ban Escalation

Repeated 429 violations lead to progressive bans:
- 1st offense: 2 minutes
- Subsequent: Escalates up to 3 days

---

## 3. Resilience Strategy Overview

### Strategy Layering

For exchange operations, we layer resilience strategies in a specific order (outer to inner):

```
[Timeout] -> [Bulkhead] -> [Circuit Breaker] -> [Retry] -> [Rate Limiter] -> Operation
```

This ordering ensures:
1. **Timeout** - Prevents indefinite waits
2. **Bulkhead** - Limits concurrent executions before other strategies
3. **Circuit Breaker** - Fails fast when service is unhealthy
4. **Retry** - Handles transient failures
5. **Rate Limiter** - Prevents exceeding Binance limits

### Separate Pipelines by Operation Type

We define three distinct resilience pipelines:

| Pipeline | Operations | Characteristics |
|----------|------------|-----------------|
| **ReadPipeline** | GetAvailableAmounts, GetMyTrades, GetTickers | Aggressive retry, standard timeout |
| **OrderPipeline** | PlaceOrder | Conservative retry, idempotency checks |
| **WebSocketPipeline** | Connect, Reconnect | Long timeout, circuit breaker only |

---

## 4. Policy Definitions

### 4.1 Read Operations Pipeline

For idempotent read operations (account info, trade history, market data).

```csharp
public static class ExchangeResiliencePipelines
{
    public static ResiliencePipeline CreateReadPipeline(
        ILoggingService loggingService,
        ExchangeResilienceOptions options)
    {
        return new ResiliencePipelineBuilder()
            // 1. Timeout (outer) - 30 seconds total
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.ReadTimeoutSeconds),
                OnTimeout = args =>
                {
                    loggingService.Warning($"Exchange read operation timed out after {args.Timeout.TotalSeconds}s");
                    return default;
                }
            })

            // 2. Bulkhead - limit concurrent reads
            .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = options.MaxConcurrentReads,
                QueueLimit = options.ReadQueueLimit,
                OnRejected = args =>
                {
                    loggingService.Warning("Exchange read rejected: bulkhead full");
                    return default;
                }
            })

            // 3. Circuit Breaker
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingSeconds),
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult<HttpResponseMessage>(r => IsServerError(r)),
                OnOpened = args =>
                {
                    loggingService.Warning($"Circuit breaker OPENED for exchange reads. Duration: {args.BreakDuration}");
                    return default;
                },
                OnClosed = args =>
                {
                    loggingService.Info("Circuit breaker CLOSED for exchange reads");
                    return default;
                },
                OnHalfOpened = args =>
                {
                    loggingService.Info("Circuit breaker HALF-OPEN for exchange reads");
                    return default;
                }
            })

            // 4. Retry with exponential backoff and jitter
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.ReadMaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(options.ReadInitialDelayMs),
                UseJitter = true,  // Adds decorrelated jitter
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested)
                    .Handle<TimeoutException>()
                    .Handle<Exception>(ex => IsTransientError(ex)),
                DelayGenerator = args => CalculateDelayWithRateLimitAwareness(args),
                OnRetry = args =>
                {
                    loggingService.Info($"Retrying exchange read (attempt {args.AttemptNumber + 1}/{options.ReadMaxRetryAttempts}) " +
                        $"after {args.RetryDelay.TotalSeconds:F1}s. Reason: {args.Outcome.Exception?.Message ?? "Unknown"}");
                    return default;
                }
            })
            .Build();
    }

    private static bool IsServerError(HttpResponseMessage response)
    {
        return (int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
    }

    private static bool IsTransientError(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("429") ||
               message.Contains("503") ||
               message.Contains("502") ||
               message.Contains("500") ||
               message.Contains("ECONNRESET") ||
               message.Contains("ETIMEDOUT");
    }

    private static ValueTask<TimeSpan?> CalculateDelayWithRateLimitAwareness(RetryDelayGeneratorArguments args)
    {
        // Check if we have a Retry-After header from Binance
        if (args.Outcome.Exception is HttpRequestException httpEx &&
            httpEx.Data.Contains("RetryAfter"))
        {
            var retryAfter = (int)httpEx.Data["RetryAfter"];
            return new ValueTask<TimeSpan?>(TimeSpan.FromSeconds(retryAfter));
        }

        // Use default exponential backoff with jitter
        return new ValueTask<TimeSpan?>((TimeSpan?)null);
    }
}
```

### 4.2 Order Execution Pipeline

For non-idempotent order operations. **Critical**: Orders require special handling to prevent duplicate orders.

```csharp
public static ResiliencePipeline CreateOrderPipeline(
    ILoggingService loggingService,
    ExchangeResilienceOptions options)
{
    return new ResiliencePipelineBuilder()
        // 1. Timeout (shorter for orders - market moves fast)
        .AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(options.OrderTimeoutSeconds),
            OnTimeout = args =>
            {
                loggingService.Warning($"Order operation timed out after {args.Timeout.TotalSeconds}s - CHECK ORDER STATUS!");
                return default;
            }
        })

        // 2. Bulkhead - strict limit on concurrent orders
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.MaxConcurrentOrders,
            QueueLimit = options.OrderQueueLimit,
            OnRejected = args =>
            {
                loggingService.Warning("Order rejected: too many concurrent orders");
                return default;
            }
        })

        // 3. Circuit Breaker (more sensitive for orders)
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = options.OrderCircuitBreakerFailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(options.OrderCircuitBreakerSamplingSeconds),
            MinimumThroughput = options.OrderCircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(options.OrderCircuitBreakerBreakDurationSeconds),
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
                .Handle<TimeoutRejectedException>(),
            OnOpened = args =>
            {
                loggingService.Error($"ORDER CIRCUIT BREAKER OPENED! Trading suspended for {args.BreakDuration}");
                // Consider triggering trading suspension here
                return default;
            }
        })

        // 4. Retry - VERY CONSERVATIVE for orders
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = options.OrderMaxRetryAttempts,  // Low: 1-2
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(options.OrderInitialDelayMs),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder()
                // ONLY retry on connection errors, NOT on any response
                .Handle<HttpRequestException>(ex => IsConnectionError(ex))
                .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested),
            OnRetry = args =>
            {
                loggingService.Warning($"RETRYING ORDER (attempt {args.AttemptNumber + 1}) - " +
                    $"VERIFY ORDER STATUS BEFORE RETRY! Reason: {args.Outcome.Exception?.Message}");
                return default;
            }
        })
        .Build();
}

private static bool IsConnectionError(HttpRequestException ex)
{
    // Only retry on true connection failures, not on HTTP responses
    return ex.InnerException is System.Net.Sockets.SocketException ||
           ex.Message.Contains("ECONNRESET") ||
           ex.Message.Contains("ETIMEDOUT") ||
           ex.Message.Contains("connection");
}
```

### 4.3 WebSocket Pipeline

For WebSocket connection management.

```csharp
public static ResiliencePipeline CreateWebSocketPipeline(
    ILoggingService loggingService,
    ExchangeResilienceOptions options)
{
    return new ResiliencePipelineBuilder()
        // 1. Timeout for connection establishment
        .AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(options.WebSocketConnectTimeoutSeconds)
        })

        // 2. Circuit Breaker
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(60),
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder()
                .Handle<WebSocketException>()
                .Handle<TimeoutRejectedException>(),
            OnOpened = args =>
            {
                loggingService.Warning("WebSocket circuit breaker opened - falling back to REST");
                return default;
            }
        })

        // 3. Retry for connection
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = options.WebSocketMaxReconnectAttempts,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(options.WebSocketReconnectDelaySeconds),
            MaxDelay = TimeSpan.FromMinutes(2),  // Cap the backoff
            UseJitter = true,
            ShouldHandle = new PredicateBuilder()
                .Handle<WebSocketException>()
                .Handle<TimeoutRejectedException>(),
            OnRetry = args =>
            {
                loggingService.Info($"WebSocket reconnecting (attempt {args.AttemptNumber + 1}) " +
                    $"in {args.RetryDelay.TotalSeconds:F0}s");
                return default;
            }
        })
        .Build();
}
```

### 4.4 Rate Limiter (Shared)

A shared rate limiter to enforce Binance's limits proactively.

```csharp
public static ResiliencePipeline CreateRateLimiter(ExchangeResilienceOptions options)
{
    return new ResiliencePipelineBuilder()
        .AddRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = options.RateLimitPermitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,  // 10-second segments
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = options.RateLimitQueueLimit
        })
        .Build();
}
```

---

## 5. Implementation Patterns

### 5.1 Service Registration Pattern

```csharp
// In ExchangeResilienceExtensions.cs
public static class ExchangeResilienceExtensions
{
    public static IServiceCollection AddExchangeResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options
        services.Configure<ExchangeResilienceOptions>(
            configuration.GetSection("Exchange:Resilience"));

        // Register resilience pipelines as singletons
        services.AddSingleton<IExchangeResiliencePipelines>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ExchangeResilienceOptions>>().Value;
            var logging = sp.GetRequiredService<ILoggingService>();

            return new ExchangeResiliencePipelines(
                readPipeline: ExchangeResiliencePipelines.CreateReadPipeline(logging, options),
                orderPipeline: ExchangeResiliencePipelines.CreateOrderPipeline(logging, options),
                webSocketPipeline: ExchangeResiliencePipelines.CreateWebSocketPipeline(logging, options)
            );
        });

        return services;
    }
}

// Interface for resilience pipelines
public interface IExchangeResiliencePipelines
{
    ResiliencePipeline ReadPipeline { get; }
    ResiliencePipeline OrderPipeline { get; }
    ResiliencePipeline WebSocketPipeline { get; }
}
```

### 5.2 Usage in BinanceExchangeService

```csharp
internal class BinanceExchangeService : ExchangeService
{
    private readonly IExchangeResiliencePipelines _resilience;

    public BinanceExchangeService(
        ILoggingService loggingService,
        IHealthCheckService healthCheckService,
        ICoreService coreService,
        IExchangeResiliencePipelines resilience)
        : base(loggingService, healthCheckService, coreService)
    {
        _resilience = resilience;
    }

    public override async Task<Dictionary<string, decimal>> GetAvailableAmounts()
    {
        if (_binanceApi == null)
            throw new InvalidOperationException("Binance API not initialized");

        return await _resilience.ReadPipeline.ExecuteAsync(async cancellationToken =>
        {
            var results = await _binanceApi.GetAmountsAvailableToTradeAsync();
            return results;
        });
    }

    public override async Task<IOrderDetails> PlaceOrder(IOrder order)
    {
        if (_binanceApi == null)
            throw new InvalidOperationException("Binance API not initialized");

        return await _resilience.OrderPipeline.ExecuteAsync(async cancellationToken =>
        {
            var result = await _binanceApi.PlaceOrderAsync(new ExchangeOrderRequest
            {
                OrderType = (ExchangeSharp.OrderType)(int)order.Type,
                IsBuy = order.Side == OrderSide.Buy,
                Amount = order.Amount,
                Price = order.Price,
                MarketSymbol = order.Pair
            });

            return MapToOrderDetails(result);
        });
    }
}
```

### 5.3 Order Idempotency Pattern

For critical order operations, implement idempotency checking:

```csharp
public class IdempotentOrderService
{
    private readonly ConcurrentDictionary<string, OrderStatus> _pendingOrders = new();
    private readonly IExchangeResiliencePipelines _resilience;
    private readonly ILoggingService _loggingService;

    public async Task<IOrderDetails> PlaceOrderWithIdempotency(IOrder order, string clientOrderId)
    {
        // Check if order is already in flight
        if (_pendingOrders.TryGetValue(clientOrderId, out var status))
        {
            if (status == OrderStatus.Pending)
            {
                _loggingService.Warning($"Order {clientOrderId} already pending, checking status...");
                return await CheckOrderStatus(clientOrderId);
            }
        }

        _pendingOrders[clientOrderId] = OrderStatus.Pending;

        try
        {
            var result = await _resilience.OrderPipeline.ExecuteAsync(async ct =>
            {
                // Include client order ID for Binance's idempotency
                return await PlaceOrderWithClientId(order, clientOrderId, ct);
            });

            _pendingOrders[clientOrderId] = OrderStatus.Completed;
            return result;
        }
        catch (Exception ex)
        {
            _loggingService.Error($"Order {clientOrderId} failed, checking if it was placed...", ex);

            // On timeout/connection error, check if order went through
            try
            {
                var existingOrder = await CheckOrderStatus(clientOrderId);
                if (existingOrder != null)
                {
                    _pendingOrders[clientOrderId] = OrderStatus.Completed;
                    return existingOrder;
                }
            }
            catch
            {
                // Order status check also failed
            }

            _pendingOrders.TryRemove(clientOrderId, out _);
            throw;
        }
    }
}
```

---

## 6. Configuration Values

### 6.1 Recommended Configuration

```csharp
public class ExchangeResilienceOptions
{
    // === Read Operations ===
    public int ReadTimeoutSeconds { get; set; } = 30;
    public int MaxConcurrentReads { get; set; } = 10;
    public int ReadQueueLimit { get; set; } = 20;
    public int ReadMaxRetryAttempts { get; set; } = 3;
    public int ReadInitialDelayMs { get; set; } = 1000;

    // === Order Operations ===
    public int OrderTimeoutSeconds { get; set; } = 15;
    public int MaxConcurrentOrders { get; set; } = 3;
    public int OrderQueueLimit { get; set; } = 5;
    public int OrderMaxRetryAttempts { get; set; } = 1;  // Very conservative!
    public int OrderInitialDelayMs { get; set; } = 500;

    // === Circuit Breaker (Reads) ===
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;
    public int CircuitBreakerSamplingSeconds { get; set; } = 30;
    public int CircuitBreakerMinimumThroughput { get; set; } = 8;
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    // === Circuit Breaker (Orders) ===
    public double OrderCircuitBreakerFailureRatio { get; set; } = 0.3;  // More sensitive
    public int OrderCircuitBreakerSamplingSeconds { get; set; } = 60;
    public int OrderCircuitBreakerMinimumThroughput { get; set; } = 5;
    public int OrderCircuitBreakerBreakDurationSeconds { get; set; } = 60;

    // === WebSocket ===
    public int WebSocketConnectTimeoutSeconds { get; set; } = 30;
    public int WebSocketMaxReconnectAttempts { get; set; } = 5;
    public int WebSocketReconnectDelaySeconds { get; set; } = 5;

    // === Rate Limiting ===
    public int RateLimitPermitsPerMinute { get; set; } = 1000;  // Below Binance's 1200
    public int RateLimitQueueLimit { get; set; } = 50;
}
```

### 6.2 JSON Configuration

Add to `config/exchange.json`:

```json
{
  "Exchange": {
    "KeysPath": "keys.bin",
    "RateLimitOccurences": 10,
    "RateLimitTimeframe": 1,
    "Resilience": {
      "ReadTimeoutSeconds": 30,
      "MaxConcurrentReads": 10,
      "ReadQueueLimit": 20,
      "ReadMaxRetryAttempts": 3,
      "ReadInitialDelayMs": 1000,

      "OrderTimeoutSeconds": 15,
      "MaxConcurrentOrders": 3,
      "OrderQueueLimit": 5,
      "OrderMaxRetryAttempts": 1,
      "OrderInitialDelayMs": 500,

      "CircuitBreakerFailureRatio": 0.5,
      "CircuitBreakerSamplingSeconds": 30,
      "CircuitBreakerMinimumThroughput": 8,
      "CircuitBreakerBreakDurationSeconds": 30,

      "OrderCircuitBreakerFailureRatio": 0.3,
      "OrderCircuitBreakerSamplingSeconds": 60,
      "OrderCircuitBreakerMinimumThroughput": 5,
      "OrderCircuitBreakerBreakDurationSeconds": 60,

      "WebSocketConnectTimeoutSeconds": 30,
      "WebSocketMaxReconnectAttempts": 5,
      "WebSocketReconnectDelaySeconds": 5,

      "RateLimitPermitsPerMinute": 1000,
      "RateLimitQueueLimit": 50
    }
  }
}
```

### 6.3 Update Constants.cs

```csharp
public static class Constants
{
    // ... existing constants ...

    public static class Resilience
    {
        // Read operations
        public const int DefaultReadTimeoutSeconds = 30;
        public const int DefaultMaxConcurrentReads = 10;
        public const int DefaultReadMaxRetryAttempts = 3;
        public const int DefaultReadInitialDelayMs = 1000;

        // Order operations
        public const int DefaultOrderTimeoutSeconds = 15;
        public const int DefaultMaxConcurrentOrders = 3;
        public const int DefaultOrderMaxRetryAttempts = 1;

        // Circuit breaker
        public const double DefaultCircuitBreakerFailureRatio = 0.5;
        public const int DefaultCircuitBreakerSamplingSeconds = 30;
        public const int DefaultCircuitBreakerMinimumThroughput = 8;
        public const int DefaultCircuitBreakerBreakDurationSeconds = 30;

        // Rate limiting (below Binance's 1200/min limit)
        public const int DefaultRateLimitPermitsPerMinute = 1000;
    }
}
```

---

## 7. NuGet Packages

### Required Packages

```xml
<ItemGroup>
  <!-- Core Polly v8 (already referenced) -->
  <PackageReference Include="Polly" Version="8.2.1" />

  <!-- Additional packages for full resilience -->
  <PackageReference Include="Polly.RateLimiting" Version="8.2.1" />

  <!-- For HttpClient integration (optional) -->
  <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.0.0" />

  <!-- For telemetry/metrics -->
  <PackageReference Include="Polly.Extensions" Version="8.2.1" />
</ItemGroup>
```

### Package Descriptions

| Package | Purpose |
|---------|---------|
| `Polly` | Core resilience library (retry, circuit breaker, timeout) |
| `Polly.RateLimiting` | Rate limiter and concurrency limiter strategies |
| `Microsoft.Extensions.Http.Resilience` | HttpClientFactory integration |
| `Polly.Extensions` | Telemetry, dependency injection helpers |

---

## 8. Testing Strategy

### 8.1 Unit Tests

```csharp
[TestClass]
public class ExchangeResilienceTests
{
    [TestMethod]
    public async Task ReadPipeline_RetriesOnTransientError()
    {
        // Arrange
        var attempts = 0;
        var pipeline = CreateTestReadPipeline();

        // Act
        await pipeline.ExecuteAsync(async ct =>
        {
            attempts++;
            if (attempts < 3)
                throw new HttpRequestException("503 Service Unavailable");
            return "success";
        });

        // Assert
        Assert.AreEqual(3, attempts);
    }

    [TestMethod]
    public async Task OrderPipeline_DoesNotRetryOnNonConnectionError()
    {
        // Arrange
        var attempts = 0;
        var pipeline = CreateTestOrderPipeline();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<HttpRequestException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                attempts++;
                throw new HttpRequestException("400 Bad Request");
            });
        });

        Assert.AreEqual(1, attempts);  // No retry
    }

    [TestMethod]
    public async Task CircuitBreaker_OpensAfterFailures()
    {
        // Arrange
        var pipeline = CreateTestReadPipeline();

        // Act - cause failures
        for (int i = 0; i < 10; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(async ct =>
                    throw new HttpRequestException("500"));
            }
            catch { }
        }

        // Assert - circuit should be open
        await Assert.ThrowsExceptionAsync<BrokenCircuitException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct => "success");
        });
    }
}
```

### 8.2 Integration Tests

```csharp
[TestClass]
public class BinanceResilienceIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task RateLimiter_PreventsExceedingBinanceLimits()
    {
        // Arrange
        var service = CreateBinanceServiceWithResilience();
        var tasks = new List<Task>();

        // Act - fire many concurrent requests
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(service.GetAvailableAmounts());
        }

        await Task.WhenAll(tasks);

        // Assert - no 429 errors should occur
        // (Verify via mock/logging)
    }
}
```

---

## 9. Monitoring and Observability

### 9.1 Metrics to Track

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `exchange_requests_total` | Total requests by operation type | - |
| `exchange_requests_failed` | Failed requests by error type | > 10% failure rate |
| `exchange_retry_attempts` | Number of retry attempts | > 50/min |
| `exchange_circuit_breaker_state` | Circuit breaker state (0=closed, 1=open) | Any open state |
| `exchange_request_duration_ms` | Request latency percentiles | p99 > 5000ms |
| `exchange_rate_limit_rejections` | Requests rejected by rate limiter | Any rejections |
| `exchange_bulkhead_rejections` | Requests rejected by bulkhead | Any rejections |

### 9.2 Telemetry Integration

```csharp
public static ResiliencePipeline CreateTelemetryEnabledPipeline(
    ExchangeResilienceOptions options,
    MeterProvider meterProvider)
{
    return new ResiliencePipelineBuilder()
        .AddTelemetry(new TelemetryOptions
        {
            MeterFactory = meterProvider.GetMeter,
            LoggerFactory = loggerFactory
        })
        // ... add strategies ...
        .Build();
}
```

### 9.3 Health Check Integration

```csharp
public class ExchangeResilienceHealthCheck : IHealthCheck
{
    private readonly IExchangeResiliencePipelines _pipelines;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var readState = GetCircuitBreakerState(_pipelines.ReadPipeline);
        var orderState = GetCircuitBreakerState(_pipelines.OrderPipeline);

        if (orderState == CircuitState.Open)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Order circuit breaker is OPEN - trading suspended"));
        }

        if (readState == CircuitState.Open)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Read circuit breaker is OPEN - using cached data"));
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
```

---

## References

### Binance Documentation
- [Binance API Rate Limits](https://developers.binance.com/docs/binance-spot-api-docs/rest-api/limits)
- [Binance WebSocket Limits](https://www.binance.com/en/academy/articles/what-are-binance-websocket-limits)

### Polly Documentation
- [Polly Official Documentation](https://www.pollydocs.org/)
- [Retry Strategy](https://www.pollydocs.org/strategies/retry.html)
- [Circuit Breaker Strategy](https://www.pollydocs.org/strategies/circuit-breaker.html)
- [Timeout Strategy](https://www.pollydocs.org/strategies/timeout.html)
- [Bulkhead Pattern](https://github.com/App-vNext/Polly/wiki/Bulkhead)

### Best Practices
- [Circuit Breaker Policy Fine-tuning Best Practice](https://devblogs.microsoft.com/dotnet/circuit-breaker-policy-finetuning-best-practice/)
- [Exponential Backoff And Jitter - AWS](https://aws.amazon.com/blogs/architecture/exponential-backoff-and-jitter/)
- [Building Resilient .NET Applications](https://antondevtips.com/blog/how-to-implement-retries-and-resilience-patterns-with-polly-and-microsoft-resilience)

---

## Appendix A: Decision Log

| Decision | Rationale |
|----------|-----------|
| Separate pipelines for read vs. order | Orders are non-idempotent and require conservative retry |
| Order retry max = 1 | Prevent duplicate orders; verify status before retry |
| Circuit breaker failure ratio = 0.5 | Conservative default per Microsoft best practices |
| Rate limit at 1000/min | 17% buffer below Binance's 1200/min limit |
| Bulkhead for orders = 3 | Prevent overwhelming exchange during high activity |
| Jitter enabled | Prevent thundering herd effect |
| Order timeout = 15s | Markets move fast; stale orders are problematic |

---

## Appendix B: Migration Checklist

- [ ] Add NuGet package references
- [ ] Create `ExchangeResilienceOptions` class
- [ ] Create `IExchangeResiliencePipelines` interface and implementation
- [ ] Update `BinanceExchangeService` constructor to accept resilience pipelines
- [ ] Replace existing `_resiliencePipeline` with appropriate pipeline per operation
- [ ] Add configuration to `exchange.json`
- [ ] Update `Constants.cs` with resilience defaults
- [ ] Add unit tests for resilience behavior
- [ ] Add integration tests for rate limiting
- [ ] Update health checks to include circuit breaker state
- [ ] Add telemetry/metrics collection
- [ ] Update documentation/README
