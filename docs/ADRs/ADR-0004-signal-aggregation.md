# ADR-0004: Multi-Source Signal Rating Aggregation System

## Status
Accepted

## Context
Automated trading decisions require market signals from multiple sources and timeframes to avoid acting on noise. IntelliTrader needs to:

1. Poll external signal sources (TradingView) at different intervals
2. Aggregate ratings across multiple timeframes (5min, 15min, 60min, 240min)
3. Calculate a global market sentiment rating
4. Provide per-pair signal ratings for rule evaluation
5. Support adding new signal sources without code changes

Single-source signals are unreliable; a "strong buy" on 5-minute charts may contradict 1-hour trends. Cross-timeframe confirmation reduces false positives.

## Decision
We implemented a pluggable signal receiver system with a `SignalsService` that aggregates ratings from multiple sources. Signals are defined in configuration and polled by registered `ISignalReceiver` implementations.

Signal definition configuration from `signals.json`:
```json
{
  "Signals": {
    "Enabled": true,
    "GlobalRatingSignals": ["TV-5mins", "TV-15mins", "TV-60mins"],
    "Definitions": [
      {
        "Name": "TV-15mins",
        "Receiver": "TradingViewCryptoSignalReceiver",
        "Configuration": {
          "PollingInterval": 7,
          "SignalPeriod": 15,
          "RequestUrl": "https://scanner.tradingview.com/crypto/scan"
        }
      }
    ]
  }
}
```

Signal aggregation from `SignalsService.cs`:
```csharp
public double? GetRating(string pair, IEnumerable<string> signalNames)
{
    if (signalNames != null && signalNames.Count() > 0)
    {
        double ratingSum = 0;
        foreach (var signalName in signalNames)
        {
            var rating = GetSignalsByName(signalName)?
                .FirstOrDefault(s => s.Pair == pair)?.Rating;
            if (rating != null)
                ratingSum += rating.Value;
            else
                return null;  // Missing data invalidates aggregate
        }
        return Math.Round(ratingSum / signalNames.Count(), 8);
    }
    return null;
}

public double? GetGlobalRating()
{
    double ratingSum = 0;
    double ratingCount = 0;

    foreach (var kvp in signalReceivers)
    {
        if (Config.GlobalRatingSignals.Contains(kvp.Key))
        {
            double? averageRating = kvp.Value.GetAverageRating();
            if (averageRating != null)
            {
                ratingSum += averageRating.Value;
                ratingCount++;
            }
        }
    }
    return ratingCount > 0 ? Math.Round(ratingSum / ratingCount, 8) : null;
}
```

Receiver factory pattern for pluggable sources:
```csharp
public class SignalsService(
    Func<string, string, IConfigurationSection, ISignalReceiver> signalReceiverFactory,
    ...) : ISignalsService
{
    public void Start()
    {
        foreach (var definition in Config.Definitions)
        {
            var receiver = signalReceiverFactory(
                definition.Receiver, definition.Name, definition.Configuration);
            if (signalReceivers.TryAdd(definition.Name, receiver))
                receiver.Start();
        }
    }
}
```

## Alternatives Considered

1. **Single Timeframe Signals**
   - Pros: Simpler implementation, faster response
   - Cons: High false positive rate, noise trading
   - Rejected: Multi-timeframe confirmation is essential for profitable strategies

2. **Weighted Signal Aggregation**
   - Pros: Longer timeframes could have higher influence
   - Cons: More complex tuning, harder to debug
   - Rejected: Simple averaging is sufficient; weighting can be added later if needed

3. **Machine Learning Signal Fusion**
   - Pros: Could discover non-obvious correlations
   - Cons: Requires training data, black box decisions
   - Rejected: Overcomplicated for rule-based trading; may add later

4. **Real-Time WebSocket Signals**
   - Pros: Lower latency than polling
   - Cons: TradingView doesn't offer WebSocket API for free
   - Rejected: Polling at 7-second intervals is sufficient for our timeframes

## Consequences

### Positive
- **Multi-Timeframe Confirmation**: Global rating combines 5min, 15min, 60min signals
- **Extensible**: New signal sources added via configuration, not code
- **Null Safety**: Missing signals return null rather than corrupt aggregates
- **Per-Pair Ratings**: Rules can check signal ratings for specific trading pairs
- **Factory Pattern**: Signal receivers are created at runtime from config definitions

### Negative
- **Polling Latency**: 7-second intervals mean up to 7 seconds of signal staleness
- **External Dependency**: TradingView API changes could break signal acquisition
- **Rating Interpretation**: -1 to +1 scale requires understanding of indicator semantics
- **No Signal History**: Only current ratings stored; historical analysis requires external tools

## How to Validate

1. **Signal Availability Check**:
   ```csharp
   var signals = signalsService.GetSignalsByName("TV-15mins");
   Assert.NotEmpty(signals);
   Assert.All(signals, s => Assert.InRange(s.Rating.Value, -1.0, 1.0));
   ```

2. **Global Rating Calculation**:
   ```csharp
   var globalRating = signalsService.GetGlobalRating();
   Assert.NotNull(globalRating);
   // With 3 signals, should be average of their averages
   ```

3. **Configuration-Driven Sources**:
   ```bash
   # Add new signal definition to signals.json
   # Restart and verify new receiver is started (check logs)
   ```

4. **Missing Signal Handling**:
   ```csharp
   // Request rating for non-existent pair
   var rating = signalsService.GetRating("INVALIDPAIR", "TV-15mins");
   Assert.Null(rating);
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Interfaces/Services/ISignalsService.cs` - Signal service interface
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Signals.Base/Services/SignalsService.cs` - Aggregation implementation
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/signals.json` - Signal source definitions
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Signals.TradingView/Receivers/TradingViewCryptoSignalReceiver.cs` - TradingView polling implementation
