# Thread Synchronization Guidelines

This document establishes consistent patterns for thread synchronization across the IntelliTrader codebase. All services are singletons accessed from multiple concurrent timed tasks (ticker polling every 1s, signal polling every 7s, rule evaluation every 3s, order execution every 1s).

## Primitive Selection Guide

| Scenario | Primitive | Example |
|---|---|---|
| Simple key-value read/write caching | `ConcurrentDictionary<TKey, TValue>` | `pairConfigs`, `_tickers`, `cachedObjects` |
| Atomic counter or flag updates | `Interlocked` | `Interlocked.Increment(ref _count)` |
| Complex multi-step mutations (synchronous) | `lock` on a `private readonly object` | Buy/Sell/Swap in `TradingService` |
| Complex multi-step mutations (async) | `SemaphoreSlim(1, 1)` with `WaitAsync()` | WebSocket connection management |
| Exposing lock for external callers | `public object SyncRoot { get; }` (readonly) | `IBacktestingService.SyncRoot` |

## Detailed Guidelines

### ConcurrentDictionary

Use for simple key-value storage where individual operations (read, write, remove) are independent.

```csharp
// GOOD: Simple caching
private readonly ConcurrentDictionary<string, PairConfig> _pairConfigs = new();

// GOOD: Atomic add-or-update
_pairConfigs.AddOrUpdate(pair, newConfig, (key, old) => newConfig);

// GOOD: Atomic get-or-add
var config = _pairConfigs.GetOrAdd(pair, key => CreateDefault(key));
```

**Warning**: `ConcurrentDictionary` does NOT make multi-step operations atomic. If you need to read-then-write as a single unit, use a `lock`.

### lock (Monitor)

Use for synchronous multi-step mutations where multiple fields must be updated atomically.

```csharp
// GOOD: Private readonly lock object with descriptive name
private readonly object _tradingLock = new();

lock (_tradingLock)
{
    // Multiple steps that must be atomic
    var pair = Account.GetTradingPair(options.Pair);
    Account.RemovePair(pair);
    Account.UpdateBalance(pair.Cost);
}
```

**Rules**:
- Lock object MUST be `private readonly`
- Lock object MUST NOT be `this`, `typeof(T)`, or a string
- Lock object SHOULD use `new object()` (not a domain object)
- Keep lock scope as small as possible
- Never call external/virtual methods inside a lock
- Never use `lock` in `async` methods (use `SemaphoreSlim` instead)

### SemaphoreSlim

Use for async-compatible critical sections. Preferred over `lock` when the code path is or may become async.

```csharp
private readonly SemaphoreSlim _connectionLock = new(1, 1);

public async Task ConnectAsync()
{
    await _connectionLock.WaitAsync();
    try
    {
        // Critical section
    }
    finally
    {
        _connectionLock.Release();
    }
}
```

### Interlocked

Use for atomic operations on single numeric values or references.

```csharp
// GOOD: Atomic counter
private int _count;
Interlocked.Increment(ref _count);
Interlocked.Decrement(ref _count);

// GOOD: Atomic flag
private int _isRunning; // 0 = false, 1 = true
if (Interlocked.CompareExchange(ref _isRunning, 1, 0) == 0)
{
    // Won the race, proceed
}
```

### SyncRoot Pattern

When external callers need to synchronize with a service's internal state, expose a `SyncRoot` property. This is used by timed tasks that need to lock around service operations.

```csharp
// GOOD: Interface declares SyncRoot
public interface IBacktestingService
{
    object SyncRoot { get; }
}

// GOOD: Implementation uses readonly backing field
public class BacktestingService : IBacktestingService
{
    private readonly object _syncRoot = new();
    public object SyncRoot => _syncRoot;
}
```

**Rules**:
- The backing field MUST be `readonly` to prevent accidental reassignment
- Use `=> _field` (expression body) rather than `{ get; private set; } = new object()`
- Only expose `SyncRoot` when external callers genuinely need it

## Anti-Patterns

### Never lock on `this`
```csharp
// BAD: External code can also lock on the same instance
lock (this) { ... }

// GOOD: Use a private lock object
private readonly object _lock = new();
lock (_lock) { ... }
```

### Never use `lock` in async methods
```csharp
// BAD: lock blocks the thread, defeating async
async Task DoWorkAsync()
{
    lock (_lock) { await SomethingAsync(); } // COMPILER ERROR (good!)
}

// GOOD: Use SemaphoreSlim
async Task DoWorkAsync()
{
    await _semaphore.WaitAsync();
    try { await SomethingAsync(); }
    finally { _semaphore.Release(); }
}
```

### Never use mutable lock objects
```csharp
// BAD: Lock object can be reassigned, causing two threads to lock on different objects
private object _lock = new object();        // missing readonly
public object SyncRoot { get; set; }        // settable property

// GOOD
private readonly object _lock = new();
public object SyncRoot => _lock;            // expression-bodied, readonly backing
```

### Avoid nested locks
```csharp
// BAD: Risk of deadlock if lock order varies across call sites
lock (_lockA)
{
    lock (_lockB) { ... }
}

// If unavoidable, document and enforce a strict lock ordering
```

### Avoid locking around I/O
```csharp
// BAD: Holds lock while waiting for network
lock (_lock)
{
    var result = httpClient.GetAsync(url).Result; // blocks thread AND holds lock
}

// GOOD: Fetch outside the lock, then lock for state update
var result = await httpClient.GetAsync(url);
lock (_lock)
{
    _cache = result;
}
```

## Current Codebase Inventory

| Location | Primitive | Notes |
|---|---|---|
| `TradingService` | `lock (SyncRoot)` | Guards Buy/Sell/Swap -- multi-step trading operations |
| `TradingAccountBase` | `lock (SyncRoot)` | Guards account state mutations (balance, pairs) |
| `BacktestingService` | `lock (SyncRoot)` | Exposed for `BacktestingLoadSnapshotsTimedTask` |
| `PortfolioRiskManager` | `lock (_syncRoot)` | Guards equity/P&L state updates |
| `LoggingService` | `lock (syncRoot)` | Guards log writer access |
| `ConfigurableServiceBase` | `lock (syncRoot)` | Guards config reload |
| `TradingViewCryptoSignalPollingTimedTask` | `lock (syncRoot)` | Guards signal state |
| `MemorySink` | `lock (syncRoot)` | Guards Serilog text writer |
| `BoundedConcurrentStack` | `lock (_trimLock)` + `Interlocked` | Trim uses lock, count uses Interlocked |
| `BinanceWebSocketService` | `SemaphoreSlim` + `Interlocked` | Async connection lock, atomic request IDs |
| `SignalsService` | `ConcurrentDictionary` | Signal receivers and trailing signals |
| `CachingService` | `ConcurrentDictionary` | Cached objects store |
| `CoreService` | `ConcurrentDictionary` | Timed task registry |

## Review Checklist

When reviewing PRs that touch concurrent code:

- [ ] Lock objects are `private readonly`
- [ ] No `lock` usage in `async` methods
- [ ] No locking on `this`, `typeof()`, or strings
- [ ] `ConcurrentDictionary` operations that need atomicity are wrapped in a lock
- [ ] Lock scope is minimal (no I/O or external calls inside)
- [ ] `SyncRoot` properties use expression-bodied members backed by readonly fields
- [ ] No nested locks without documented ordering
