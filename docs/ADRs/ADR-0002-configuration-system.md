# ADR-0002: JSON-Based Hot-Reload Configuration System

## Status
Accepted

## Context
A cryptocurrency trading bot requires extensive runtime configuration for:

- Trading parameters (market, max cost, DCA levels, stop-loss margins)
- Signal source definitions (TradingView endpoints, polling intervals)
- Rule conditions (buy/sell triggers, trailing parameters)
- Exchange credentials and rate limits

Configuration changes are frequent during trading strategy optimization, and restarting the application disrupts active positions and WebSocket connections. Additionally, different configuration sections have varying sensitivity levels (credentials vs. display settings).

## Decision
We implemented a centralized `ConfigProvider` that uses Microsoft.Extensions.Configuration with file watchers for automatic hot-reload. Key design elements:

1. **Indirection Layer**: `paths.json` maps section names to actual JSON files
2. **Hot-Reload**: `reloadOnChange: true` with `ChangeToken.OnChange()` callbacks
3. **Typed Binding**: `IConfigurationSection.Get<T>()` for strongly-typed configs
4. **Validation**: `ConfigValidationService` validates config objects on load
5. **Deferred Logging**: Logging service factory pattern avoids bootstrap circular dependency

Configuration loading from `ConfigProvider.cs`:
```csharp
private IConfigurationRoot GetConfig(string configPath, Action<IConfigurationRoot> onChange)
{
    var configBuilder = new ConfigurationBuilder()
        .SetBasePath(fullConfigPath)
        .AddJsonFile(configPath, optional: false, reloadOnChange: true)
        .AddEnvironmentVariables();

    var configRoot = configBuilder.Build();
    ChangeToken.OnChange(configRoot.GetReloadToken, () => onChange(configRoot));
    return configRoot;
}
```

Service configuration callback pattern from `ConfigrableServiceBase<T>`:
```csharp
public T GetSection<T>(string sectionName, Action<T> onChange = null) where T : class
{
    IConfigurationSection configSection = GetSection(sectionName, changedConfigSection =>
    {
        var changedConfig = changedConfigSection.Get<T>();
        ValidateConfig(changedConfig, sectionName);
        onChange?.Invoke(changedConfig);
    });
    return configSection.Get<T>();
}
```

## Alternatives Considered

1. **Database-Stored Configuration**
   - Pros: Centralized, version history, multi-instance sync
   - Cons: Additional infrastructure, overkill for single-instance bot
   - Rejected: File-based is simpler and sufficient for our use case

2. **Environment Variables Only**
   - Pros: 12-factor app compliant, good for secrets
   - Cons: Poor for complex nested structures (rules, DCA levels)
   - Rejected: Not suitable for our hierarchical configuration needs

3. **YAML Configuration**
   - Pros: More readable for complex structures
   - Cons: Less native .NET support, additional dependency
   - Rejected: JSON has better tooling and validation support in .NET

4. **No Hot-Reload (Restart Required)**
   - Pros: Simpler implementation, no race conditions
   - Cons: Disrupts trading operations, loses WebSocket state
   - Rejected: Critical for production trading where uptime matters

## Consequences

### Positive
- **Zero-Downtime Updates**: Rule changes apply within seconds without restart
- **Separation of Concerns**: Each config file (trading.json, signals.json, rules.json) focuses on one domain
- **Validation on Load**: Invalid configs are rejected early with clear error messages
- **Environment Override**: Environment variables can override file values for secrets
- **Callback Pattern**: Services receive change notifications via `OnConfigReloaded()` override

### Negative
- **File Watching Overhead**: Continuous file system monitoring consumes resources
- **Race Conditions**: Config changes mid-trade could cause inconsistent state
- **No Atomic Updates**: Multi-file config changes apply incrementally
- **Validation Complexity**: Custom validators needed for business rules (e.g., DCA levels)

## How to Validate

1. **Hot-Reload Verification**:
   ```bash
   # Start the bot, then modify IntelliTrader/config/rules.json
   # Observe logs for "Rules configuration reloaded" message
   ```

2. **Validation Test**:
   ```csharp
   // Set invalid value in trading.json (BuyMaxCost: -100)
   // Expect: ConfigValidationException thrown on startup
   ```

3. **Change Callback Test**:
   ```csharp
   var callbackInvoked = false;
   configProvider.GetSection<TradingConfig>("Trading", _ => callbackInvoked = true);
   // Modify trading.json
   Assert.True(callbackInvoked);
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Models/Config/ConfigProvider.cs` - Core configuration provider
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/paths.json` - Section-to-file mapping
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/trading.json` - Trading configuration example
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/rules.json` - Rules configuration with nested conditions
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/signals.json` - Signal source definitions
