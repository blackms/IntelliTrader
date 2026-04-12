# ADR-0015: Remove Service Locator Pattern

## Status

Accepted

## Date

2026-04-11

## Context

The codebase historically used a static `Application` class as a service locator, exposing
`Application.Resolve<T>()`, `Application.ConfigProvider`, `Application.Speed`, and
`Application.BuildContainer()`. A prior refactor (commit 5f44984) migrated most services to
constructor injection, and PR #41 resolved resulting DI cycles with `Lazy<T>`. However,
several residual usages of the static facade remained:

- `Application.ConfigProvider` in `ConfigurableServiceBase` (fallback for a parameterless constructor)
- `Application.ConfigProvider` in `IntelliTrader.Backtesting.AppModule` (registration-time config access)
- `Application.Speed` in `TestableRulesService` (test double)
- `Application.Initialize()` called from `ApplicationBootstrapper` to seed the static fields

These usages kept the service locator pattern alive, making the dependency graph implicit and
harder to test.

## Decision

Remove all static service-locator members from the `Application` class and replace every
remaining usage with explicit dependency injection:

1. **`ConfigurableServiceBase`** -- Remove the parameterless constructor and the
   `Application.ConfigProvider` fallback. All concrete services already use the
   `ConfigurableServiceBase(IConfigProvider)` constructor, so the fallback was dead code.

2. **`Backtesting.AppModule`** -- Replace `Application.ConfigProvider` with
   `builder.Properties[nameof(IConfigProvider)]`, a dictionary entry set by
   `ApplicationBootstrapper` before module scanning. This is the idiomatic Autofac mechanism
   for passing context to modules during registration.

3. **Test doubles** -- Replace `Application.Speed` with an injected `IApplicationContext`
   mock, matching how the production `RulesService` already works.

4. **`ApplicationBootstrapper`** -- Remove the `Application.Initialize()` call. The static
   facade is no longer seeded because nothing reads from it.

5. **`Application` class** -- Strip all members. Retained as an empty static class / assembly
   marker with a doc-comment pointing to this ADR.

## Consequences

### Positive

- All dependencies are explicit and visible in constructor signatures.
- No static mutable state; services are fully testable in isolation.
- The Autofac container is the single source of truth for the object graph.
- Removes the `[Obsolete]` suppression pragmas that were scattered across the codebase.

### Negative

- Autofac modules that need configuration at registration time must read from
  `builder.Properties` instead of a convenient static accessor. This is a minor ergonomic
  trade-off.

### Neutral

- `Program.cs` and `Web/Startup.cs` still call `container.Resolve<T>()` at the composition
  root, which is standard and expected -- the service locator anti-pattern only applies when
  resolution happens inside application services.
