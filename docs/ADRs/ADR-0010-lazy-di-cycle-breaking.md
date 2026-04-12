# ADR-0010: Lazy<T> for Dependency Injection Cycle Breaking

## Status
Accepted

## Date
2026-04-12

## Context
IntelliTrader's service graph contains circular dependencies where services need references to each other (e.g., TradingService depends on ExchangeService which depends on TradingService for order callbacks). Autofac detects these cycles at resolution time and throws a `DependencyResolutionException`, preventing the application from starting.

Common solutions include restructuring the dependency graph, introducing mediator services, or using lazy resolution to defer one side of the cycle.

## Decision
We use `Lazy<T>` wrappers on back-edges of the service dependency graph to break circular references. The service that initiates the cycle injects `Lazy<T>` instead of `T` directly, deferring resolution until first access. Autofac natively supports `Lazy<T>` without additional registration.

Example:
```csharp
public class TradingService : ITradingService
{
    private readonly Lazy<IExchangeService> _exchangeService;

    public TradingService(Lazy<IExchangeService> exchangeService)
    {
        _exchangeService = exchangeService;
    }

    private IExchangeService Exchange => _exchangeService.Value;
}
```

The convention is: the service that is "logically secondary" in the relationship receives the `Lazy<T>` wrapper, keeping the primary dependency path eager.

## Consequences

### Positive
- Breaks circular dependencies without restructuring the service graph
- Zero overhead until `.Value` is accessed; no unnecessary early resolution
- Native Autofac support; no custom registrations or interceptors needed
- Minimal code change: only the constructor parameter type changes

### Negative
- Hides architectural coupling; cycles remain in the logical graph
- Runtime failure risk: if `.Value` is accessed during construction of another lazy-resolved service, the cycle reappears
- Developers must understand which edges are lazy and why

### Neutral
- All services are singletons (see [ADR-0001](ADR-0001-dependency-injection.md)), so `Lazy<T>.Value` is resolved at most once per application lifetime

## References
- [ADR-0001: Autofac Dependency Injection](ADR-0001-dependency-injection.md) - Parent DI architecture decision
