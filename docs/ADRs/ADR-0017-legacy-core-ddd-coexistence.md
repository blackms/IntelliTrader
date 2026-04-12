# ADR-0017: Legacy Core and DDD Layer Coexistence

## Status

Accepted

## Date

2026-04-12

## Context

IntelliTrader currently has two architectural paradigms coexisting in the same codebase:

1. **Legacy Core Hub** (`IntelliTrader.Core/`) -- The original architecture where `IntelliTrader.Core` acts as a central hub containing all interfaces, models, configuration types, and service abstractions. All other projects depend on Core, and Core defines the contracts (e.g., `ITradingService`, `IExchangeService`, `ISignalsService`).

2. **DDD Layers** (`IntelliTrader.Domain/`, `IntelliTrader.Application/`, `IntelliTrader.Infrastructure/`) -- Newer layers following Domain-Driven Design and Clean Architecture patterns. These introduce domain entities, value objects, aggregates, CQRS command/query handlers, domain events, specifications, and infrastructure adapters.

Both layers are wired into the same Autofac DI container and run simultaneously at runtime. This dual architecture emerged from an incremental modernization effort: rather than a risky big-bang rewrite, DDD layers were added alongside the existing Core hub to enable gradual migration of business logic.

### How they interact

- **Core interfaces remain authoritative for runtime orchestration.** The timed task system (`HighResolutionTimedTask`, `ICoreService.AddTask`), trading operations (`ITradingService`), and exchange calls (`IExchangeService`) all flow through Core-defined contracts.
- **Domain layer owns business rules and validation.** Domain entities (e.g., `TradingPair`, `Order`) contain invariant enforcement, value objects handle type safety, and specifications provide composable business rule evaluation.
- **Application layer provides CQRS dispatch.** Commands and queries are dispatched through `ICommandDispatcher` and `IQueryDispatcher`, with handlers that typically call into Core services for actual execution.
- **Infrastructure layer provides adapters.** Telemetry (OpenTelemetry), persistence adapters, and messaging infrastructure live here. The Web project depends on Infrastructure for telemetry setup.

### Validation duplication

Some validation exists in both layers:
- Core services perform runtime guards (null checks, range validation on config values).
- Domain entities enforce invariants in constructors and methods.
- Application command handlers may validate before dispatching to Core services.

This duplication is intentional during the migration period -- Core validation ensures backward compatibility while Domain validation establishes the target architecture.

## Decision

We accept the coexistence of both architectural layers and document the following rules:

1. **New business logic should be implemented in the Domain layer** using entities, value objects, and specifications.
2. **New operations should use CQRS** via Application command/query handlers.
3. **Core interfaces remain the runtime integration points** until all consumers have been migrated.
4. **Do NOT remove Core interfaces or services** until their Domain/Application replacements are fully tested and all callers have been updated.
5. **Domain validation is authoritative** for business rules. Core validation exists for backward compatibility and should not be extended.
6. **Do NOT merge the two validation paths** into a single layer -- let them coexist until migration is complete.

### Migration path

The intended progression is:

1. New features use Domain + Application layers exclusively.
2. Existing features are migrated one service at a time, starting with the least critical.
3. Once all consumers of a Core interface have been migrated to Domain/Application equivalents, the Core interface can be deprecated and eventually removed.
4. `IntelliTrader.Core` will eventually become a thin hosting/bootstrapping layer rather than the central hub.

ADR-0016 (IHostedService Migration) documents a parallel effort to migrate the timed task system, which is one concrete step in this direction.

## Consequences

### Positive

- Enables incremental modernization without risky big-bang rewrites.
- New code benefits from DDD patterns (rich domain model, specification pattern, domain events).
- Existing trading logic remains stable and well-tested.

### Negative

- Developers must understand both architectures and know which layer to modify.
- Some validation logic is duplicated between Core and Domain layers.
- The dependency graph is larger than either architecture would be in isolation.

### Risks

- Without this ADR, future contributors may attempt to merge the layers prematurely, causing regressions in trading-critical code paths.
- If migration stalls, the dual architecture becomes permanent complexity rather than transitional.

## References

- ADR-0015: Remove Service Locator Pattern
- ADR-0016: IHostedService Migration
- `IntelliTrader.Core/` -- Legacy hub with interfaces and models
- `IntelliTrader.Domain/` -- DDD entities, value objects, specifications
- `IntelliTrader.Application/` -- CQRS commands, queries, handlers
- `IntelliTrader.Infrastructure/` -- Adapters, telemetry, persistence
