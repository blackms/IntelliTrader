# ADR-0007: Event-Driven Domain State Changes

## Status
Accepted

## Context
IntelliTrader's trading operations generate state changes that multiple components need to react to:

1. Order placed/filled events for logging and notifications
2. Trading suspended/resumed events for dashboard updates
3. Risk limit breached events for circuit breaker coordination
4. Signal received events for cross-component communication

Directly coupling TradingService to NotificationService, WebService, and LoggingService creates tight dependencies and makes testing difficult. Adding new observers (e.g., metrics collection) would require modifying the trading layer.

## Decision
We implemented an in-memory domain event dispatcher following DDD principles. Events are raised by domain operations and dispatched asynchronously to registered handlers.

Event definition from `OrderPlacedEvent.cs`:
```csharp
public sealed record OrderPlacedEvent : IDomainEvent
{
    public Guid EventId { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? CorrelationId { get; }

    public string OrderId { get; }
    public string Pair { get; }
    public OrderSide Side { get; }
    public decimal Amount { get; }
    public decimal Price { get; }
    public OrderType OrderType { get; }
    public bool IsManual { get; }
    public string? SignalRule { get; }

    public OrderPlacedEvent(string orderId, string pair, OrderSide side, ...) { ... }
}
```

Event dispatcher from `InMemoryDomainEventDispatcher.cs`:
```csharp
public class InMemoryDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    private readonly IServiceProvider _serviceProvider;

    public void RegisterHandler<TEvent>(IDomainEventHandler<TEvent> handler)
        where TEvent : IDomainEvent
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<object>());
        lock (handlers) { handlers.Add(handler); }
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        var handlers = GetHandlersForEvent(domainEvent.GetType());
        foreach (var handler in handlers)
        {
            await InvokeHandlerAsync(handler, domainEvent, ct);
        }
    }

    private List<object> GetHandlersForEvent(Type eventType)
    {
        var allHandlers = new List<object>();
        // Get directly registered handlers
        if (_handlers.TryGetValue(eventType, out var registered))
            allHandlers.AddRange(registered);
        // Resolve handlers from DI container
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var serviceHandlers = _serviceProvider.GetService(typeof(IEnumerable<>).MakeGenericType(handlerType));
        // ... add to allHandlers
        return allHandlers;
    }
}
```

Event raising in TradingService (fire-and-forget pattern):
```csharp
private void RaiseEventSafe(IDomainEvent domainEvent)
{
    if (_domainEventDispatcher == null) return;

    _ = Task.Run(async () =>
    {
        try
        {
            await _domainEventDispatcher.DispatchAsync(domainEvent);
        }
        catch (Exception ex)
        {
            loggingService.Error($"Failed to dispatch event {domainEvent.GetType().Name}: {ex.Message}");
        }
    });
}

private IOrderDetails PlaceBuyOrder(BuyOptions options)
{
    // ... order placement logic
    if (orderDetails != null)
    {
        RaiseEventSafe(new OrderPlacedEvent(
            orderId: orderDetails.OrderId,
            pair: options.Pair,
            side: DomainOrderSide.Buy,
            amount: amount,
            price: currentPrice,
            ...));
    }
}
```

## Alternatives Considered

1. **Direct Method Calls (No Events)**
   - Pros: Simple, explicit dependencies
   - Cons: Tight coupling, hard to extend, difficult testing
   - Rejected: Would require TradingService to know about all observers

2. **Message Queue (RabbitMQ/Kafka)**
   - Pros: Durability, cross-process communication, replay
   - Cons: Infrastructure overhead, complexity for single-instance bot
   - Rejected: Overkill for in-process event handling

3. **C# Events (MulticastDelegate)**
   - Pros: Native C# feature, no dependency
   - Cons: No async support, memory leaks if not unsubscribed
   - Rejected: Async handlers are essential for non-blocking operations

4. **MediatR Library**
   - Pros: Popular, well-documented, request/response pattern
   - Cons: Heavier than needed, request/response semantics don't fit events
   - Rejected: Custom dispatcher is simpler for pure event broadcasting

## Consequences

### Positive
- **Loose Coupling**: TradingService doesn't depend on notification/logging implementations
- **Extensibility**: New handlers registered without modifying event sources
- **Testability**: Events can be captured in tests without side effects
- **Async Support**: Handlers can perform I/O without blocking trading operations
- **DI Integration**: Handlers resolved from container or registered directly
- **Correlation IDs**: Events can be traced across operations

### Negative
- **Fire-and-Forget Risks**: Handler failures don't affect source operation (by design)
- **No Guaranteed Delivery**: In-memory dispatch means events lost on crash
- **Handler Order**: No guaranteed execution order for multiple handlers
- **Debugging Complexity**: Tracing event flow requires understanding dispatcher

## How to Validate

1. **Event Dispatch Verification**:
   ```csharp
   var capturedEvents = new List<IDomainEvent>();
   dispatcher.RegisterHandler<OrderPlacedEvent>(e => capturedEvents.Add(e));
   tradingService.Buy(new BuyOptions("BTCUSDT") { MaxCost = 100 });
   Assert.Single(capturedEvents.OfType<OrderPlacedEvent>());
   ```

2. **Handler Registration**:
   ```csharp
   // DI-registered handlers
   services.AddTransient<IDomainEventHandler<OrderFilledEvent>, MetricsHandler>();
   // Verify handler invoked on OrderFilledEvent
   ```

3. **Correlation ID Propagation**:
   ```csharp
   var event1 = new OrderPlacedEvent(..., correlationId: "tx-123");
   // Subsequent OrderFilledEvent should carry same correlationId
   ```

4. **Error Isolation**:
   ```csharp
   // Register failing handler
   dispatcher.RegisterHandler<OrderPlacedEvent>(e => throw new Exception("Fail"));
   // Verify order still completes (event failure logged but not propagated)
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Infrastructure/Events/InMemoryDomainEventDispatcher.cs` - Dispatcher implementation
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Domain/Events/OrderPlacedEvent.cs` - Event definition
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Domain/Events/TradingSuspendedEvent.cs` - Trading state event
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Trading/Services/TradingService.cs` - Event raising (RaiseEventSafe method)
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Application/Ports/Driven/IDomainEventDispatcher.cs` - Dispatcher interface
