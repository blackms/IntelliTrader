# ADR-0003: Exchange Abstraction via IExchangeService Interface

## Status
Accepted

## Context
IntelliTrader needs to interact with cryptocurrency exchanges for:

- Real-time price data (tickers)
- Account balance queries
- Order placement (buy/sell)
- Trade history retrieval

Different exchanges (Binance, Coinbase, Kraken) have varying APIs, authentication methods, rate limits, and data formats. The trading logic should not be coupled to any specific exchange implementation. Additionally, testing requires mocking exchange interactions without hitting live APIs.

## Decision
We defined an `IExchangeService` interface that abstracts all exchange operations, with concrete implementations registered as named services in the DI container. The interface exposes async methods and includes connectivity state for WebSocket streaming.

Interface definition from `IExchangeService.cs`:
```csharp
public interface IExchangeService : IConfigurableService
{
    event Action<IReadOnlyCollection<ITicker>> TickersUpdated;

    bool IsWebSocketConnected { get; }
    bool IsRestFallbackActive { get; }
    TimeSpan TimeSinceLastTickerUpdate { get; }

    void Start(bool virtualTrading);
    void Stop();
    Task<IEnumerable<ITicker>> GetTickers(string market);
    Task<IEnumerable<string>> GetMarketPairs(string market);
    Task<Dictionary<string, decimal>> GetAvailableAmounts();
    Task<IEnumerable<IOrderDetails>> GetMyTrades(string pair);
    Task<decimal> GetLastPrice(string pair);
    Task<IOrderDetails> PlaceOrder(IOrder order);
    Task ReconnectWebSocketAsync();
}
```

Named registration from `IntelliTrader.Exchange.Binance/AppModule.cs`:
```csharp
builder.RegisterType<BinanceExchangeService>()
    .Named<IExchangeService>("Binance")
    .As<IConfigurableService>()
    .Named<IConfigurableService>("ExchangeBinance")
    .SingleInstance()
    .PreserveExistingDefaults();
```

Resolution in TradingService via factory delegate:
```csharp
internal class TradingService(
    Func<string, IExchangeService> exchangeServiceFactory,
    ...) : ITradingService
{
    private IExchangeService ResolveExchangeService()
    {
        var serviceName = isReplayingSnapshots
            ? Constants.ServiceNames.BacktestingExchangeService
            : Config.Exchange;
        return exchangeServiceFactory(serviceName);
    }
}
```

## Alternatives Considered

1. **Direct Exchange Library Usage**
   - Pros: No abstraction overhead, full access to exchange features
   - Cons: Tight coupling, difficult testing, exchange-specific code scattered
   - Rejected: Would require extensive refactoring to support additional exchanges

2. **Strategy Pattern with Exchange Factory**
   - Pros: Classic GoF pattern, well understood
   - Cons: More boilerplate than DI-based approach
   - Rejected: Autofac's named services provide the same flexibility with less code

3. **CCXT Universal Library**
   - Pros: 100+ exchanges supported, community maintained
   - Cons: JavaScript/Python focused, .NET port less mature
   - Rejected: ExchangeSharp provides better .NET integration for Binance

4. **Generic Exchange Interface Without State**
   - Pros: Simpler stateless interface
   - Cons: Cannot track WebSocket state, no event-based updates
   - Rejected: Real-time price streaming requires state tracking

## Consequences

### Positive
- **Exchange Agnosticism**: TradingService works with any exchange implementing IExchangeService
- **Testability**: `BacktestingExchangeService` replays historical data for strategy testing
- **WebSocket Abstraction**: Connection state exposed via `IsWebSocketConnected`, `TimeSinceLastTickerUpdate`
- **Event-Driven Updates**: `TickersUpdated` event enables push-based price updates
- **Named Resolution**: Config-driven exchange selection via `Config.Exchange` value

### Negative
- **Lowest Common Denominator**: Interface limited to operations common across exchanges
- **Exchange-Specific Features Hidden**: Advanced order types (OCO, trailing) may not be exposed
- **State Synchronization**: Multiple exchange instances could have conflicting account state
- **Version Coupling**: ExchangeSharp library updates may break implementations

## How to Validate

1. **Interface Implementation Check**:
   ```csharp
   var exchange = Container.ResolveNamed<IExchangeService>("Binance");
   Assert.IsType<BinanceExchangeService>(exchange);
   Assert.True(exchange is IConfigurableService);
   ```

2. **Virtual Trading Mode**:
   ```bash
   # Set VirtualTrading: true in trading.json
   # Verify orders are simulated, not sent to exchange
   ```

3. **Backtesting Exchange Swap**:
   ```csharp
   // Enable backtesting with Replay: true
   // Verify BacktestingExchangeService is resolved instead of Binance
   ```

4. **WebSocket State Monitoring**:
   ```csharp
   var exchange = Container.ResolveNamed<IExchangeService>("Binance");
   exchange.Start(virtualTrading: false);
   Assert.True(exchange.IsWebSocketConnected || exchange.IsRestFallbackActive);
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Core/Interfaces/Services/IExchangeService.cs` - Interface definition
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/Services/BinanceExchangeService.cs` - Binance implementation
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Exchange.Binance/AppModule.cs` - Named service registration
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Trading/Services/TradingService.cs` - Exchange resolution via factory
