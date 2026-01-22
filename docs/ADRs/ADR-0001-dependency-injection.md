# ADR-0001: Autofac Dependency Injection with Module Discovery

## Status
Accepted

## Context
IntelliTrader is a modular cryptocurrency trading bot composed of multiple assemblies (Core, Trading, Signals, Exchange, Web, Rules, etc.). Each module contains services that need to be wired together, and the system requires:

1. Loose coupling between components for testability
2. Singleton lifetime for stateful services (TradingService, SignalsService, etc.)
3. Named service resolution for multiple exchange implementations (Binance, future exchanges)
4. Automatic discovery of new modules without modifying the main application

The .NET Core built-in DI container lacks advanced features like named services, property injection, and assembly scanning that would simplify module registration.

## Decision
We adopted Autofac as the dependency injection container with automatic module discovery based on assembly naming conventions. Key implementation details:

1. **Module Pattern**: Each assembly exposes an `AppModule : Module` class that registers its services
2. **Assembly Discovery**: `ApplicationBootstrapper` scans for `IntelliTrader.*.dll` assemblies using regex matching
3. **Named Services**: Exchange implementations are registered with names (e.g., `Named<IExchangeService>("Binance")`)
4. **Singleton Scope**: All core services use `SingleInstance()` for proper state management
5. **Factory Delegates**: Services can inject `Func<string, IService>` for lazy named resolution

Example module registration from `IntelliTrader.Core/AppModule.cs`:
```csharp
builder.RegisterType<CoreService>()
    .As<ICoreService>()
    .As<IConfigurableService>()
    .Named<IConfigurableService>(Constants.ServiceNames.CoreService)
    .SingleInstance();
```

Example assembly discovery from `ApplicationBootstrapper.cs`:
```csharp
var assemblyPattern = new Regex($"{nameof(IntelliTrader)}.*.dll");
var dynamicAssemblies = Directory.EnumerateFiles(dynamicAssembliesPath, "*.dll")
    .Where(filename => assemblyPattern.IsMatch(Path.GetFileName(filename)));
builder.RegisterAssemblyModules(allAssemblies.ToArray());
```

## Alternatives Considered

1. **Microsoft.Extensions.DependencyInjection Only**
   - Pros: No additional dependency, familiar to .NET Core developers
   - Cons: No named services, no assembly scanning, verbose registration code
   - Rejected: Would require significant boilerplate for our multi-module architecture

2. **Manual Registration Without Module Discovery**
   - Pros: Explicit, no reflection overhead
   - Cons: Adding new modules requires editing the main program
   - Rejected: Would slow down development when adding new signal sources or exchanges

3. **MEF (Managed Extensibility Framework)**
   - Pros: .NET standard, supports plugins
   - Cons: More complex, attribute-based, less control over lifetime
   - Rejected: Overkill for our needs, Autofac is simpler

## Consequences

### Positive
- **Extensibility**: New modules (exchanges, signal sources) are automatically discovered
- **Testability**: Services can be mocked via interface injection; `ApplicationBootstrapper` accepts custom `IConfigProvider`
- **Single Responsibility**: Each module owns its registrations
- **Named Resolution**: TradingService resolves exchange by config value: `exchangeServiceFactory(Config.Exchange)`
- **Flexibility**: Factory delegates enable runtime service selection

### Negative
- **Learning Curve**: Team members must understand Autofac conventions
- **Startup Cost**: Assembly scanning adds ~50-100ms to startup time
- **Debugging**: DI errors can be cryptic (mitigated by early resolution in `BuildAndConfigureContainer`)
- **Binary Coupling**: All modules must follow the naming convention

## How to Validate

1. **Module Discovery Test**:
   ```bash
   dotnet build IntelliTrader.sln
   # Verify all AppModule classes are discovered in debug output
   ```

2. **Named Service Resolution**:
   ```csharp
   var exchange = Container.ResolveNamed<IExchangeService>("Binance");
   Assert.NotNull(exchange);
   ```

3. **Singleton Verification**:
   ```csharp
   var service1 = Container.Resolve<ITradingService>();
   var service2 = Container.Resolve<ITradingService>();
   Assert.Same(service1, service2);
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Services/ApplicationBootstrapper.cs` - Main bootstrapper with assembly discovery
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/AppModule.cs` - Core services registration
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/AppModule.cs` - Named exchange registration
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/Program.cs` - Container initialization
