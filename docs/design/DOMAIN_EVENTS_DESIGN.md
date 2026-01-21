# Domain Events Design Document

## Executive Summary

This document outlines the design for implementing a comprehensive domain events system in IntelliTrader. The codebase already has a solid foundation with `IDomainEvent`, `AggregateRoot<T>`, and `IDomainEventDispatcher` interfaces. This design extends and completes that foundation.

---

## 1. Current State Analysis

### 1.1 Existing Infrastructure

The codebase already implements core domain event patterns:

**SharedKernel (IntelliTrader.Domain)**
- `IDomainEvent` - Marker interface with `OccurredAt` timestamp
- `DomainEvent` - Abstract base record
- `AggregateRoot<TId>` - Base class managing event collection

**Application Layer Ports**
- `IDomainEventDispatcher` - Dispatch single or multiple events
- `IDomainEventHandler<TEvent>` - Handler interface for specific event types

**Existing Events (8 total)**
| Event | Location | Raised By |
|-------|----------|-----------|
| `PositionOpened` | Trading/Events | Position.Open() |
| `PositionClosed` | Trading/Events | Position.Close() |
| `DCAExecuted` | Trading/Events | Position.AddDCAEntry() |
| `PositionAddedToPortfolio` | Trading/Events | Portfolio.RecordPositionOpened() |
| `PositionRemovedFromPortfolio` | Trading/Events | Portfolio.RecordPositionClosed() |
| `PortfolioBalanceChanged` | Trading/Events | Portfolio (multiple methods) |
| `MaxPositionsReached` | Trading/Events | Portfolio.RecordPositionOpened() |

### 1.2 Integration Patterns

The `OpenPositionHandler` and `ClosePositionHandler` demonstrate the current pattern:
1. Execute business logic on aggregates
2. Save aggregates via repositories
3. Commit unit of work
4. Dispatch domain events post-commit

---

## 2. Gap Analysis

### 2.1 Missing Event Categories

| Category | Missing Events | Business Need |
|----------|---------------|---------------|
| **Order Lifecycle** | OrderPlaced, OrderFilled, OrderCancelled, OrderPartiallyFilled, OrderExpired | Track exchange interactions |
| **Stop Loss / Take Profit** | StopLossTriggered, TakeProfitTriggered | React to protective orders |
| **Signals** | SignalReceived, SignalExpired, SignalRatingChanged | Trigger analysis and actions |
| **Risk Management** | RiskLimitBreached, CircuitBreakerTripped, DailyLossLimitReached | Emergency responses |
| **Trading Rules** | TradingRuleTriggered, TradingRuleViolated | Audit and analytics |
| **System** | TradingSuspended, TradingResumed | System state changes |

### 2.2 Missing Integration Points

1. **Legacy TradingService** - Does not raise domain events (uses notification service directly)
2. **PortfolioRiskManager** - Circuit breaker/limits don't emit events
3. **SignalsService** - No events for signal lifecycle
4. **TrailingOrderManager** - Trailing state changes not captured

---

## 3. Proposed Domain Events

### 3.1 Order Lifecycle Events

```csharp
namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Raised when an order is placed on the exchange.
/// </summary>
public sealed record OrderPlaced : IDomainEvent
{
    public OrderId OrderId { get; }
    public TradingPair Pair { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public Price Price { get; }
    public Quantity Quantity { get; }
    public Money EstimatedCost { get; }
    public string? SignalRule { get; }
    public bool IsManual { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when an order is fully filled.
/// </summary>
public sealed record OrderFilled : IDomainEvent
{
    public OrderId OrderId { get; }
    public TradingPair Pair { get; }
    public OrderSide Side { get; }
    public Price AveragePrice { get; }
    public Quantity FilledQuantity { get; }
    public Money Cost { get; }
    public Money Fees { get; }
    public TimeSpan FillDuration { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when an order is partially filled.
/// </summary>
public sealed record OrderPartiallyFilled : IDomainEvent
{
    public OrderId OrderId { get; }
    public TradingPair Pair { get; }
    public OrderSide Side { get; }
    public Quantity FilledQuantity { get; }
    public Quantity RemainingQuantity { get; }
    public Price PartialPrice { get; }
    public decimal FillPercentage { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when an order is cancelled.
/// </summary>
public sealed record OrderCancelled : IDomainEvent
{
    public OrderId OrderId { get; }
    public TradingPair Pair { get; }
    public OrderSide Side { get; }
    public CancellationReason Reason { get; }
    public Quantity? PartiallyFilledQuantity { get; }
    public DateTimeOffset OccurredAt { get; }
}

public enum CancellationReason
{
    Manual,
    Timeout,
    InsufficientFunds,
    InvalidPrice,
    ExchangeError,
    TrailingStopAction,
    SystemShutdown
}

/// <summary>
/// Raised when an order expires without being filled.
/// </summary>
public sealed record OrderExpired : IDomainEvent
{
    public OrderId OrderId { get; }
    public TradingPair Pair { get; }
    public OrderSide Side { get; }
    public TimeSpan TimeToLive { get; }
    public DateTimeOffset OccurredAt { get; }
}
```

### 3.2 Stop Loss / Take Profit Events

```csharp
namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Raised when a stop loss is triggered.
/// </summary>
public sealed record StopLossTriggered : IDomainEvent
{
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public Price TriggerPrice { get; }
    public Price StopLossPrice { get; }
    public Margin MarginAtTrigger { get; }
    public Money EstimatedLoss { get; }
    public StopLossType StopLossType { get; }
    public int DCALevel { get; }
    public TimeSpan PositionAge { get; }
    public DateTimeOffset OccurredAt { get; }
}

public enum StopLossType
{
    Fixed,
    Trailing,
    ATRBased,
    TimeDecay
}

/// <summary>
/// Raised when a take profit target is reached.
/// </summary>
public sealed record TakeProfitTriggered : IDomainEvent
{
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public Price TriggerPrice { get; }
    public Price TakeProfitPrice { get; }
    public Margin MarginAtTrigger { get; }
    public Money EstimatedProfit { get; }
    public bool IsPartialTakeProfit { get; }
    public decimal TakeProfitPercentage { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when a trailing stop is updated.
/// </summary>
public sealed record TrailingStopUpdated : IDomainEvent
{
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public Price PreviousStopPrice { get; }
    public Price NewStopPrice { get; }
    public Price CurrentPrice { get; }
    public decimal TrailingPercentage { get; }
    public DateTimeOffset OccurredAt { get; }
}
```

### 3.3 Signal Events

```csharp
namespace IntelliTrader.Domain.Signals.Events;

/// <summary>
/// Raised when a new signal is received.
/// </summary>
public sealed record SignalReceived : IDomainEvent
{
    public string SignalName { get; }
    public TradingPair Pair { get; }
    public SignalRating Rating { get; }
    public SignalRating? PreviousRating { get; }
    public decimal? Price { get; }
    public decimal? PriceChange { get; }
    public long? Volume { get; }
    public double? Volatility { get; }
    public string Source { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when a signal expires or becomes stale.
/// </summary>
public sealed record SignalExpired : IDomainEvent
{
    public string SignalName { get; }
    public TradingPair Pair { get; }
    public SignalRating LastRating { get; }
    public TimeSpan SignalAge { get; }
    public TimeSpan MaxAge { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when a signal's rating changes significantly.
/// </summary>
public sealed record SignalRatingChanged : IDomainEvent
{
    public string SignalName { get; }
    public TradingPair Pair { get; }
    public SignalRating OldRating { get; }
    public SignalRating NewRating { get; }
    public double RatingChange { get; }
    public bool CrossedBuyThreshold { get; }
    public bool CrossedSellThreshold { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when a signal rule condition is met.
/// </summary>
public sealed record SignalRuleMatched : IDomainEvent
{
    public string RuleName { get; }
    public TradingPair Pair { get; }
    public string Action { get; }
    public IReadOnlyList<string> MatchedConditions { get; }
    public SignalRating? AggregateRating { get; }
    public DateTimeOffset OccurredAt { get; }
}
```

### 3.4 Risk Management Events

```csharp
namespace IntelliTrader.Domain.Risk.Events;

/// <summary>
/// Raised when a risk limit is breached.
/// </summary>
public sealed record RiskLimitBreached : IDomainEvent
{
    public RiskLimitType LimitType { get; }
    public decimal CurrentValue { get; }
    public decimal LimitValue { get; }
    public string Description { get; }
    public RiskSeverity Severity { get; }
    public DateTimeOffset OccurredAt { get; }
}

public enum RiskLimitType
{
    PortfolioHeat,
    MaxPositions,
    PositionSize,
    DailyLoss,
    Drawdown,
    Exposure
}

public enum RiskSeverity
{
    Warning,
    Critical,
    Emergency
}

/// <summary>
/// Raised when the circuit breaker is triggered.
/// </summary>
public sealed record CircuitBreakerTripped : IDomainEvent
{
    public decimal CurrentDrawdown { get; }
    public decimal MaxAllowedDrawdown { get; }
    public int OpenPositions { get; }
    public Money TotalExposure { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when the circuit breaker is reset.
/// </summary>
public sealed record CircuitBreakerReset : IDomainEvent
{
    public bool IsManualReset { get; }
    public decimal DrawdownAtReset { get; }
    public DateTimeOffset TrippedAt { get; }
    public TimeSpan TripDuration { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when daily loss limit is reached.
/// </summary>
public sealed record DailyLossLimitReached : IDomainEvent
{
    public decimal DailyLoss { get; }
    public decimal DailyLossLimit { get; }
    public int TradesExecuted { get; }
    public int WinningTrades { get; }
    public int LosingTrades { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when portfolio heat changes significantly.
/// </summary>
public sealed record PortfolioHeatChanged : IDomainEvent
{
    public decimal PreviousHeat { get; }
    public decimal CurrentHeat { get; }
    public decimal MaxHeat { get; }
    public int ActivePositions { get; }
    public string ChangeReason { get; }
    public DateTimeOffset OccurredAt { get; }
}
```

### 3.5 Trading Rule Events

```csharp
namespace IntelliTrader.Domain.Rules.Events;

/// <summary>
/// Raised when a trading rule is triggered.
/// </summary>
public sealed record TradingRuleTriggered : IDomainEvent
{
    public string RuleName { get; }
    public TradingPair Pair { get; }
    public string RuleAction { get; }
    public IReadOnlyDictionary<string, object> RuleContext { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when a trading action is blocked by rule violation.
/// </summary>
public sealed record TradingRuleViolated : IDomainEvent
{
    public string RuleName { get; }
    public TradingPair Pair { get; }
    public string AttemptedAction { get; }
    public string ViolationReason { get; }
    public DateTimeOffset OccurredAt { get; }
}
```

### 3.6 System Events

```csharp
namespace IntelliTrader.Domain.System.Events;

/// <summary>
/// Raised when trading is suspended.
/// </summary>
public sealed record TradingSuspended : IDomainEvent
{
    public SuspensionReason Reason { get; }
    public bool IsForced { get; }
    public int OpenPositions { get; }
    public int PendingOrders { get; }
    public DateTimeOffset OccurredAt { get; }
}

public enum SuspensionReason
{
    Manual,
    CircuitBreaker,
    DailyLossLimit,
    SystemError,
    ExchangeError,
    MaintenanceWindow
}

/// <summary>
/// Raised when trading is resumed.
/// </summary>
public sealed record TradingResumed : IDomainEvent
{
    public bool WasForced { get; }
    public TimeSpan SuspensionDuration { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Raised when exchange connection status changes.
/// </summary>
public sealed record ExchangeConnectionChanged : IDomainEvent
{
    public string ExchangeName { get; }
    public bool IsConnected { get; }
    public string? DisconnectReason { get; }
    public int ReconnectAttempts { get; }
    public DateTimeOffset OccurredAt { get; }
}
```

---

## 4. Interface Definitions

### 4.1 Enhanced Event Interfaces

```csharp
namespace IntelliTrader.Domain.SharedKernel;

/// <summary>
/// Marker interface for domain events.
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// When the event occurred.
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Unique identifier for event deduplication.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Correlation ID for tracking related events.
    /// </summary>
    string? CorrelationId { get; }
}

/// <summary>
/// Base record providing common properties and behavior.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Marker for events that should be persisted to an event store.
/// </summary>
public interface IPersistableEvent : IDomainEvent
{
    /// <summary>
    /// The aggregate ID this event belongs to.
    /// </summary>
    string AggregateId { get; }

    /// <summary>
    /// The aggregate type name.
    /// </summary>
    string AggregateType { get; }

    /// <summary>
    /// The event sequence number within the aggregate.
    /// </summary>
    long SequenceNumber { get; }
}

/// <summary>
/// Marker for events that require immediate handling.
/// </summary>
public interface IUrgentEvent : IDomainEvent { }

/// <summary>
/// Marker for events that can be processed asynchronously.
/// </summary>
public interface IAsyncEvent : IDomainEvent { }
```

### 4.2 Enhanced Dispatcher Interface

```csharp
namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Port for dispatching domain events.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches an event synchronously to all handlers.
    /// </summary>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches multiple events in order.
    /// </summary>
    Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an event for background processing.
    /// </summary>
    Task QueueAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches with specified delivery mode.
    /// </summary>
    Task DispatchAsync(IDomainEvent domainEvent, EventDeliveryMode mode, CancellationToken cancellationToken = default);
}

public enum EventDeliveryMode
{
    /// <summary>
    /// Block until all handlers complete.
    /// </summary>
    Synchronous,

    /// <summary>
    /// Fire and forget to background queue.
    /// </summary>
    Asynchronous,

    /// <summary>
    /// Persist to outbox for reliable delivery.
    /// </summary>
    Transactional
}
```

### 4.3 Handler Interfaces

```csharp
namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Handler for a specific domain event type.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the event.
    /// </summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handler execution order (lower = earlier).
    /// </summary>
    int Order => 0;
}

/// <summary>
/// Handler that processes events asynchronously in background.
/// </summary>
public interface IAsyncDomainEventHandler<in TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Maximum retry attempts on failure.
    /// </summary>
    int MaxRetries => 3;

    /// <summary>
    /// Delay between retries.
    /// </summary>
    TimeSpan RetryDelay => TimeSpan.FromSeconds(1);
}
```

---

## 5. Implementation Patterns

### 5.1 MediatR vs Custom Implementation

#### Option A: MediatR Integration

**Pros:**
- Battle-tested, widely adopted
- Rich pipeline behavior (validation, logging, caching)
- Built-in support for notifications (pub/sub)
- Strong community and documentation

**Cons:**
- Additional dependency
- Performance overhead for high-frequency events
- Reflection-based handler discovery
- May be overkill for simple use cases

**Sample MediatR Integration:**
```csharp
// Event as notification
public sealed record PositionOpenedNotification(PositionOpened Event) : INotification;

// Handler
public class PositionOpenedHandler : INotificationHandler<PositionOpenedNotification>
{
    public async Task Handle(PositionOpenedNotification notification, CancellationToken cancellationToken)
    {
        // Handle event
    }
}

// Dispatcher adapter
public class MediatREventDispatcher : IDomainEventDispatcher
{
    private readonly IMediator _mediator;

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        var notification = Activator.CreateInstance(notificationType, domainEvent);
        await _mediator.Publish(notification!, cancellationToken);
    }
}
```

#### Option B: Custom Implementation

**Pros:**
- Zero additional dependencies
- Full control over behavior
- Optimized for trading domain needs
- Simpler debugging

**Cons:**
- More code to maintain
- Need to implement pipeline behaviors manually
- Less ecosystem support

**Sample Custom Implementation:**
```csharp
public class InProcessEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InProcessEventDispatcher> _logger;

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers.Cast<dynamic>().OrderBy(h => h.Order))
        {
            try
            {
                await handler.HandleAsync((dynamic)domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler {HandlerType} failed for {EventType}",
                    handler.GetType().Name, eventType.Name);
                throw;
            }
        }
    }
}
```

#### Recommendation: Hybrid Approach

Use **custom lightweight dispatcher** for:
- Hot path events (price updates, trailing updates)
- Events requiring guaranteed ordering
- Internal domain events

Use **MediatR** for:
- Cross-cutting concerns (logging, metrics)
- Integration events (notifications, external systems)
- Complex handler pipelines

### 5.2 Event Publishing Patterns

#### Pattern 1: Post-Commit Dispatch (Current)

```csharp
// 1. Business logic
position.Open(...);
portfolio.RecordPositionOpened(...);

// 2. Save
await _unitOfWork.CommitAsync(cancellationToken);

// 3. Dispatch after commit
await DispatchDomainEventsAsync(position, portfolio, cancellationToken);
```

**Pros:** Consistent state before handlers run
**Cons:** Handlers cannot prevent commit; events lost on crash

#### Pattern 2: Transactional Outbox

```csharp
// 1. Business logic
position.Open(...);

// 2. Save entities AND events in same transaction
await _unitOfWork.SaveAsync(position, cancellationToken);
await _eventStore.SaveAsync(position.DomainEvents, cancellationToken);
await _unitOfWork.CommitAsync(cancellationToken);

// 3. Background worker dispatches from outbox
```

**Pros:** Reliable delivery; events persisted atomically
**Cons:** Eventual consistency; more complex infrastructure

#### Recommendation

Start with **Post-Commit Dispatch** (already implemented), plan for **Transactional Outbox** when:
- Event persistence is required
- Distributed systems integration needed
- Reliability requirements increase

---

## 6. Integration with Existing Services

### 6.1 TradingService Integration

The legacy `TradingService` should raise events at key points:

```csharp
// In PlaceBuyOrder
if (orderDetails != null && orderDetails.Result == OrderResult.Filled)
{
    _eventDispatcher.QueueAsync(new OrderFilled(
        OrderId.From(orderDetails.OrderId),
        options.Pair,
        OrderSide.Buy,
        orderDetails.AveragePrice,
        orderDetails.AmountFilled,
        orderDetails.AverageCost,
        orderDetails.Fees,
        fillDuration
    ));
}

// In CanBuy (when blocked)
if (portfolioRiskManager.IsCircuitBreakerTriggered())
{
    _eventDispatcher.QueueAsync(new TradingRuleViolated(
        "CircuitBreaker",
        options.Pair,
        "Buy",
        "Circuit breaker triggered (drawdown limit exceeded)"
    ));
}
```

### 6.2 PortfolioRiskManager Integration

```csharp
// In IsCircuitBreakerTriggered
if (!_circuitBreakerTriggered)
{
    _circuitBreakerTriggered = true;
    _eventDispatcher.QueueAsync(new CircuitBreakerTripped(
        currentDrawdown,
        Config.MaxDrawdownPercent,
        _positionRisks.Count,
        GetTotalExposure()
    ));
}

// In ResetCircuitBreaker
_eventDispatcher.QueueAsync(new CircuitBreakerReset(
    isManual: true,
    GetCurrentDrawdown(),
    _circuitBreakerTrippedAt,
    DateTimeOffset.UtcNow - _circuitBreakerTrippedAt
));
```

### 6.3 SignalsService Integration

```csharp
// When signal is updated
if (previousRating != newRating)
{
    _eventDispatcher.QueueAsync(new SignalRatingChanged(
        signal.Name,
        signal.Pair,
        previousRating,
        newRating,
        newRating - previousRating,
        CrossedBuyThreshold(previousRating, newRating),
        CrossedSellThreshold(previousRating, newRating)
    ));
}
```

---

## 7. Event Handlers Examples

### 7.1 Notification Handler

```csharp
public class TradingNotificationHandler :
    IDomainEventHandler<PositionOpened>,
    IDomainEventHandler<PositionClosed>,
    IDomainEventHandler<StopLossTriggered>,
    IDomainEventHandler<CircuitBreakerTripped>
{
    private readonly INotificationPort _notificationPort;

    public async Task HandleAsync(PositionOpened @event, CancellationToken ct)
    {
        await _notificationPort.SendAsync(new Notification
        {
            Title = "Position Opened",
            Message = $"Opened {@event.Pair} at {@event.Price}",
            Type = NotificationType.Trade
        }, ct);
    }

    public async Task HandleAsync(CircuitBreakerTripped @event, CancellationToken ct)
    {
        await _notificationPort.SendAsync(new Notification
        {
            Title = "CIRCUIT BREAKER TRIGGERED",
            Message = $"Drawdown: {@event.CurrentDrawdown:F2}% exceeds {@event.MaxAllowedDrawdown:F2}%",
            Type = NotificationType.Alert,
            Priority = NotificationPriority.Urgent
        }, ct);
    }
}
```

### 7.2 Metrics Handler

```csharp
public class TradingMetricsHandler :
    IDomainEventHandler<PositionOpened>,
    IDomainEventHandler<PositionClosed>,
    IDomainEventHandler<OrderFilled>
{
    private readonly IMetricsCollector _metrics;

    public async Task HandleAsync(PositionOpened @event, CancellationToken ct)
    {
        _metrics.IncrementCounter("positions_opened_total", new[] { @event.Pair.ToString() });
        _metrics.RecordValue("position_cost", @event.Cost.Amount, new[] { @event.Pair.ToString() });
    }

    public async Task HandleAsync(PositionClosed @event, CancellationToken ct)
    {
        _metrics.IncrementCounter("positions_closed_total",
            new[] { @event.Pair.ToString(), @event.FinalMargin.Value >= 0 ? "profit" : "loss" });
        _metrics.RecordValue("realized_pnl", @event.FinalMargin.Value, new[] { @event.Pair.ToString() });
    }
}
```

### 7.3 Audit Log Handler

```csharp
public class TradingAuditHandler : IAsyncDomainEventHandler<IDomainEvent>
{
    private readonly IAuditRepository _auditRepository;

    public int Order => 100; // Run after other handlers

    public async Task HandleAsync(IDomainEvent @event, CancellationToken ct)
    {
        await _auditRepository.LogAsync(new AuditEntry
        {
            EventType = @event.GetType().Name,
            EventData = JsonSerializer.Serialize(@event),
            Timestamp = @event.OccurredAt,
            CorrelationId = @event.CorrelationId
        }, ct);
    }
}
```

---

## 8. In-Process vs Distributed Events

### 8.1 Current Scope: In-Process

For the current IntelliTrader architecture, **in-process events** are sufficient:
- Single process application
- No need for cross-service communication
- Low latency requirements for trading

### 8.2 Future: Distributed Events

When scaling beyond single instance:

| Pattern | Use Case | Technology |
|---------|----------|------------|
| Message Queue | Reliable async delivery | RabbitMQ, Azure Service Bus |
| Event Streaming | High-throughput, replay | Apache Kafka, Azure Event Hubs |
| Pub/Sub | Broadcast to multiple consumers | Redis Pub/Sub, NATS |

**Integration Events** would be needed:
```csharp
/// <summary>
/// Events that cross bounded context boundaries.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
    string SourceContext { get; }
}

public record TradeCompletedIntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string SourceContext => "Trading";

    public string Pair { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public decimal Profit { get; init; }
}
```

---

## 9. Event Sourcing Considerations

### 9.1 Current State: Not Required

Event sourcing (storing all events as source of truth) is **not recommended** for initial implementation:

**Reasons against:**
- Adds significant complexity
- Current aggregates are simple CRUD
- No regulatory requirement for full audit trail
- Performance overhead for high-frequency updates

### 9.2 Partial Event Sourcing

Consider event sourcing only for:
- **Position aggregate**: Complete trade history valuable
- **Risk events**: Regulatory/compliance audit trail

```csharp
public interface IEventSourcedAggregate<TId>
{
    TId Id { get; }
    int Version { get; }
    IReadOnlyList<IDomainEvent> UncommittedEvents { get; }

    void LoadFromHistory(IEnumerable<IDomainEvent> history);
    void ClearUncommittedEvents();
}
```

---

## 10. Implementation Roadmap

### Phase 1: Foundation (Current Sprint)
- [ ] Enhance `IDomainEvent` with EventId, CorrelationId
- [ ] Implement `InProcessEventDispatcher`
- [ ] Add Order lifecycle events
- [ ] Wire dispatcher into existing handlers

### Phase 2: Risk Events (Next Sprint)
- [ ] Add Risk management events
- [ ] Integrate with PortfolioRiskManager
- [ ] Add notification handlers
- [ ] Add metrics handlers

### Phase 3: Signal Events (Sprint +2)
- [ ] Add Signal events
- [ ] Integrate with SignalsService
- [ ] Add signal analytics handlers

### Phase 4: Advanced Features (Future)
- [ ] Transactional outbox pattern
- [ ] Event persistence/replay
- [ ] Consider MediatR for cross-cutting concerns
- [ ] Distributed events preparation

---

## 11. Testing Strategy

### 11.1 Unit Tests

```csharp
[Fact]
public void Position_Open_Should_Raise_PositionOpened_Event()
{
    // Arrange
    var pair = TradingPair.Create("BTC", "USDT");
    var orderId = OrderId.Create();
    var price = Price.Create(50000m);
    var quantity = Quantity.Create(0.1m);
    var fees = Money.Create(5m, "USDT");

    // Act
    var position = Position.Open(pair, orderId, price, quantity, fees);

    // Assert
    position.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<PositionOpened>()
        .Which.Should().Match<PositionOpened>(e =>
            e.Pair == pair &&
            e.Price == price &&
            e.Quantity == quantity);
}
```

### 11.2 Integration Tests

```csharp
[Fact]
public async Task Handler_Should_Process_PositionOpened_Event()
{
    // Arrange
    var handler = new TradingNotificationHandler(_mockNotificationPort.Object);
    var @event = new PositionOpened(...);

    // Act
    await handler.HandleAsync(@event, CancellationToken.None);

    // Assert
    _mockNotificationPort.Verify(x => x.SendAsync(
        It.Is<Notification>(n => n.Type == NotificationType.Trade),
        It.IsAny<CancellationToken>()));
}
```

---

## 12. Summary

The IntelliTrader codebase has a solid domain events foundation. This design document proposes:

1. **25+ new domain events** covering orders, stop-loss/take-profit, signals, risk management, trading rules, and system state
2. **Enhanced interfaces** with EventId, CorrelationId, and delivery modes
3. **Custom in-process dispatcher** as primary implementation, with MediatR option for complex pipelines
4. **Phased integration** with existing services (TradingService, PortfolioRiskManager, SignalsService)
5. **Clear path to distributed events** when scaling requirements grow

The recommended approach is to start with the custom implementation for simplicity and control, while designing interfaces that allow future migration to MediatR or distributed messaging systems.
