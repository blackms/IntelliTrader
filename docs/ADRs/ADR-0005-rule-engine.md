# ADR-0005: Condition-Based Trading Rule Engine

## Status
Accepted

## Context
IntelliTrader requires a flexible rule engine to:

1. Define buy conditions based on signal ratings, volume, price changes
2. Define sell conditions including profit targets, stop-loss, DCA triggers
3. Adapt trading behavior based on market conditions (bear/bull markets)
4. Support trailing orders with start/stop conditions
5. Enable non-programmers to tune strategies via configuration

Hard-coded trading logic would require developer involvement for every strategy adjustment. Market conditions change rapidly, and profitable strategies need frequent tweaking.

## Decision
We implemented a declarative rule engine where rules are defined in JSON configuration. The `RulesService` evaluates conditions using the Specification pattern, and matched rules apply modifiers to trading parameters.

Rule structure from `rules.json`:
```json
{
  "Rules": {
    "Modules": [
      {
        "Module": "Signals",
        "Configuration": { "ProcessingMode": "AllMatches", "CheckInterval": 3 },
        "Entries": [
          {
            "Enabled": true,
            "Name": "Default",
            "Modifiers": { "CostMultiplier": 1 },
            "Conditions": [
              { "Signal": "TV-15mins", "MinVolume": 150000, "MinGlobalRating": 0.1, "MinRating": 0.35 },
              { "Signal": "TV-60mins", "MinRating": 0.2 }
            ],
            "Trailing": {
              "Enabled": true,
              "MinDuration": 25,
              "MaxDuration": 240,
              "StartConditions": [...]
            }
          }
        ]
      },
      {
        "Module": "Trading",
        "Entries": [
          {
            "Name": "Bear",
            "Modifiers": {
              "BuyEnabled": false,
              "SellMargin": 1.3,
              "SellTrailing": 0.5
            },
            "Conditions": [{ "MinGlobalRating": -0.2, "MaxGlobalRating": -0.1 }]
          }
        ]
      }
    ]
  }
}
```

Condition interface from `IRuleCondition.cs`:
```csharp
public interface IRuleCondition
{
    string Signal { get; }
    long? MinVolume { get; }
    long? MaxVolume { get; }
    double? MinRating { get; }
    double? MaxRating { get; }
    double? MinGlobalRating { get; }
    double? MaxGlobalRating { get; }
    decimal? MinMargin { get; }
    decimal? MaxMargin { get; }
    int? MinDCALevel { get; }
    int? MaxDCALevel { get; }
    // ... 20+ condition properties
}
```

Specification-based evaluation from `RulesService.cs`:
```csharp
public bool CheckConditions(
    IEnumerable<IRuleCondition> conditions,
    Dictionary<string, ISignal> signals,
    double? globalRating,
    string? pair,
    ITradingPair? tradingPair)
{
    foreach (var condition in conditions)
    {
        var context = ConditionContext.Create(
            signals, condition.Signal, globalRating, pair, tradingPair, _applicationContext.Speed);

        var specification = ConditionSpecificationBuilder.Build(condition);

        if (!specification.IsSatisfiedBy(context))
            return false;
    }
    return true;
}
```

## Alternatives Considered

1. **Hard-Coded Trading Strategies**
   - Pros: Type-safe, IDE support, compile-time checks
   - Cons: Requires code changes and deployment for adjustments
   - Rejected: Too slow iteration cycle for trading strategy optimization

2. **Scripting Language (Lua/C# Script)**
   - Pros: Full programming power, complex logic
   - Cons: Security risks, learning curve for non-programmers
   - Rejected: Configuration approach covers 95% of use cases

3. **Visual Rule Builder UI**
   - Pros: User-friendly, no JSON editing
   - Cons: Significant development effort
   - Rejected: JSON editing is acceptable for power users; UI can be added later

4. **Event-Driven Rules (Event Sourcing)**
   - Pros: Audit trail, replay capability
   - Cons: More complex infrastructure
   - Rejected: Polling-based evaluation is simpler and sufficient

## Consequences

### Positive
- **Non-Developer Strategy Tuning**: Rules modified without code changes
- **Market-Adaptive Behavior**: Rules can disable buying in bear markets (GlobalRating < -0.2)
- **Composable Conditions**: Multiple conditions AND'd together per rule
- **Trailing Support**: Rules define when trailing orders start/stop
- **Hot-Reload**: Rule changes apply without restart (via ConfigProvider)

### Negative
- **Limited Logic**: No OR conditions, no complex expressions
- **JSON Complexity**: Large rule files are hard to maintain
- **Validation Gaps**: Invalid conditions (e.g., MinRating > MaxRating) caught at runtime
- **Performance**: All rules evaluated every check interval (3 seconds)

## How to Validate

1. **Condition Evaluation Test**:
   ```csharp
   var condition = new RuleCondition { MinGlobalRating = 0.1, MaxGlobalRating = 0.3 };
   var signals = new Dictionary<string, ISignal>();
   Assert.True(rulesService.CheckConditions([condition], signals, globalRating: 0.2, null, null));
   Assert.False(rulesService.CheckConditions([condition], signals, globalRating: 0.5, null, null));
   ```

2. **Rule Matching Verification**:
   ```bash
   # Set GlobalRating to -0.15 (Bear market)
   # Verify logs show "Bear" rule matched, BuyEnabled=false
   ```

3. **Modifier Application**:
   ```csharp
   // Match "Super Bull" rule
   var pairConfig = tradingService.GetPairConfig("BTCUSDT");
   Assert.Equal(2.3m, pairConfig.SellMargin);  // From Super Bull modifiers
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Interfaces/Rules/IRule.cs` - Rule interface
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Interfaces/Rules/IRuleCondition.cs` - Condition properties
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Rules/Services/RulesService.cs` - Specification-based evaluation
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/rules.json` - Rule definitions
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Rules/Specifications/ConditionSpecificationBuilder.cs` - Specification pattern
