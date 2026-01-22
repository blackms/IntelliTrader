# IntelliTrader Onboarding Guide

Welcome to IntelliTrader! This guide will help you understand the codebase and get productive quickly.

---

## Project Overview in 5 Minutes

### What is IntelliTrader?

IntelliTrader is a **cryptocurrency trading bot** built with .NET 9 that automates trading on Binance exchange. It operates 24/7, making buy/sell decisions based on signals from TradingView and configurable trading rules.

### Core Value Proposition

- **Autonomous Trading**: Signal-driven execution without constant manual intervention
- **Risk Management**: Built-in DCA (Dollar Cost Averaging), trailing stops, and portfolio risk controls
- **Safe Experimentation**: Virtual trading mode for strategy testing without real funds
- **Real-time Monitoring**: Web dashboard with SignalR for live updates

### Key Features Summary

| Feature | Description |
|---------|-------------|
| **Virtual/Live Trading** | Toggle between paper trading and real exchange execution |
| **TradingView Signals** | Multi-timeframe signal integration (5m, 15m, 60m, 240m) |
| **DCA Support** | 4+ configurable levels with customizable margins and multipliers |
| **Trailing Orders** | Trailing buy/sell with configurable stop margins |
| **Rules Engine** | Signal rules (buy triggers) and trading rules (sell/DCA conditions) |
| **Portfolio Risk** | Circuit breakers, daily loss limits, portfolio heat management |
| **Web Dashboard** | Real-time UI on port 7000 with manual controls |
| **Telegram Alerts** | Notifications for order fills and trading events |

---

## Architecture Tour

### Solution Structure

```
IntelliTrader.sln
|
+-- IntelliTrader/              # Main executable & configuration
|   +-- Program.cs              # Entry point
|   +-- config/                 # JSON configuration files
|
+-- IntelliTrader.Core/         # Core services, interfaces, models
|   +-- Services/               # CoreService, LoggingService, etc.
|   +-- Interfaces/             # Service contracts (ITradingService, etc.)
|   +-- Models/                 # Shared models, timed tasks
|
+-- IntelliTrader.Domain/       # Domain layer (Clean Architecture)
|   +-- Trading/                # Value objects, aggregates
|   +-- Signals/                # Signal domain models
|   +-- SharedKernel/           # Base classes, domain events
|
+-- IntelliTrader.Application/  # Application layer (CQRS)
|   +-- Commands/               # Buy, Sell, Swap commands
|   +-- Queries/                # Portfolio, position queries
|   +-- Ports/                  # Driven/driving port interfaces
|
+-- IntelliTrader.Trading/      # Trading service implementation
|   +-- Services/               # TradingService, orchestrators
|   +-- Models/                 # Trading-specific models
|
+-- IntelliTrader.Signals.Base/       # Signal abstraction
+-- IntelliTrader.Signals.TradingView/ # TradingView integration
|
+-- IntelliTrader.Exchange.Base/    # Exchange abstraction
+-- IntelliTrader.Exchange.Binance/ # Binance API implementation
|
+-- IntelliTrader.Rules/        # Rule engine implementation
+-- IntelliTrader.Web/          # ASP.NET Core dashboard
+-- IntelliTrader.Backtesting/  # Historical snapshot replay
|
+-- tests/                      # Test projects
    +-- IntelliTrader.Domain.Tests/
    +-- IntelliTrader.Application.Tests/
    +-- IntelliTrader.Trading.Tests/
    +-- IntelliTrader.Web.Tests/
    +-- ...
```

### How Pieces Connect

```
                    +------------------+
                    |   Program.cs     |
                    | (Entry Point)    |
                    +--------+---------+
                             |
                             v
                    +--------+---------+
                    | ApplicationBoot- |
                    | strapper         |
                    | (Builds DI)      |
                    +--------+---------+
                             |
          +------------------+------------------+
          |                  |                  |
          v                  v                  v
   +------+------+   +-------+------+   +------+------+
   | CoreService |   |TradingService|   | WebService  |
   | (Orchestr.) |   | (Buy/Sell)   |   | (Dashboard) |
   +------+------+   +-------+------+   +------+------+
          |                  |                  |
          |                  v                  |
          |         +--------+--------+         |
          |         | SignalsService  |         |
          |         | (TradingView)   |         |
          |         +--------+--------+         |
          |                  |                  |
          |                  v                  |
          |         +--------+--------+         |
          +-------->| ExchangeService |<--------+
                    | (Binance API)   |
                    +-----------------+
```

### Data Flow Summary

1. **Signal Acquisition** (every 7 seconds)
   - `TradingViewCryptoSignalReceiver` polls TradingView API
   - Signals stored in `SignalsService` for rating aggregation

2. **Price Updates** (every 1 second)
   - `BinanceTickersMonitorTimedTask` fetches current prices
   - WebSocket streaming for real-time updates

3. **Signal Rules Evaluation** (every 3 seconds)
   - `SignalRulesTimedTask` evaluates buy conditions
   - Triggers `Buy()` when rules match

4. **Trading Rules Evaluation** (every 3 seconds)
   - `TradingRulesTimedTask` evaluates sell/DCA conditions
   - Triggers `Sell()` or DCA buy when rules match

5. **Order Execution** (every 1 second)
   - `TradingTimedTask` processes pending orders
   - Trailing orders managed by `TrailingOrderManager`

---

## Where to Start Reading Code

### 1. Entry Point: `IntelliTrader/Program.cs`

**What it does**: Bootstraps the application, builds the DI container, starts `CoreService`.

**Key lines**:
```csharp
// Builds Autofac container with all modules
var container = BuildAndConfigureContainer();

// Resolves and starts the main orchestrator
var coreService = Container.Resolve<ICoreService>();
coreService.Start();
```

### 2. Core Orchestration: `IntelliTrader.Core/Services/CoreService.cs`

**What it does**: Coordinates all services, manages timed tasks lifecycle.

**Key concepts**:
- Starts/stops all services (Trading, Web, Notification, etc.)
- Manages `HighResolutionTimedTask` collection
- Handles unhandled exceptions globally

**Key methods**:
- `Start()` - Initializes and starts all services
- `Stop()` - Gracefully shuts down everything
- `AddTask()` / `StartTask()` - Task lifecycle management

### 3. Trading Logic: `IntelliTrader.Trading/Services/TradingService.cs`

**What it does**: Core trading facade - buy, sell, swap, DCA, risk management.

**Key methods**:
- `Buy(BuyOptions)` / `BuyAsync()` - Execute buy orders
- `Sell(SellOptions)` / `SellAsync()` - Execute sell orders
- `Swap(SwapOptions)` - Swap one position for another
- `CanBuy()` / `CanSell()` - Validation before execution
- `GetPairConfig()` - Per-pair trading configuration

**Important patterns**:
- Uses orchestrators (`BuyOrchestrator`, `SellOrchestrator`, `SwapOrchestrator`)
- Trailing orders via `TrailingOrderManager`
- Domain events raised for `OrderPlaced`, `OrderFilled`

### 4. Configuration: `IntelliTrader/config/*.json`

| File | Purpose |
|------|---------|
| `core.json` | Health checks, debug mode, password |
| `trading.json` | Market, exchange, buy/sell parameters, DCA levels |
| `signals.json` | TradingView signal definitions |
| `rules.json` | Signal rules (buy triggers), trading rules (sell/DCA) |
| `web.json` | Web interface port and settings |
| `notification.json` | Telegram bot configuration |

**Example `trading.json` structure**:
```json
{
  "Trading": {
    "VirtualTrading": true,      // Paper trading mode
    "Market": "BTC",             // Quote currency
    "Exchange": "Binance",
    "MaxPairs": 10,              // Maximum concurrent positions
    "BuyMaxCost": 0.0012,        // Default buy size
    "DCALevels": [               // Dollar Cost Averaging levels
      { "Margin": -1.5 },
      { "Margin": -2.5 }
    ]
  }
}
```

---

## Coding Conventions

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Interfaces | `I` prefix | `ITradingService`, `IConfigProvider` |
| Services | `Service` suffix | `TradingService`, `CoreService` |
| Timed Tasks | `TimedTask` suffix | `HealthCheckTimedTask` |
| Config classes | `Config` suffix | `TradingConfig`, `CoreConfig` |
| Value Objects | Descriptive name | `Price`, `Quantity`, `Margin` |
| Test classes | `Tests` suffix | `TradingServiceTests`, `PriceTests` |

### File Organization

- **One class per file** (except small related classes)
- **Folder structure mirrors namespaces**: `IntelliTrader.Core/Services/` -> `IntelliTrader.Core.Services`
- **Models in `Models/` subfolder**: Config, DTOs, value objects
- **Interfaces in `Interfaces/` subfolder**: Service contracts grouped by domain

### Interface Patterns

**Service interfaces define contracts**:
```csharp
public interface ITradingService : IConfigurableService
{
    ITradingConfig Config { get; }
    void Start();
    void Stop();
    void Buy(BuyOptions options);
    Task BuyAsync(BuyOptions options, CancellationToken ct = default);
}
```

**Configurable services implement `IConfigurableService`**:
```csharp
internal class TradingService : ConfigrableServiceBase<TradingConfig>, ITradingService
{
    public override string ServiceName => Constants.ServiceNames.TradingService;
    protected override ILoggingService LoggingService => _loggingService;
}
```

### DI Registration Pattern (Autofac Modules)

Each project has an `AppModule.cs`:
```csharp
public class AppModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<TradingService>()
            .As<ITradingService>()
            .As<IConfigurableService>()
            .Named<IConfigurableService>(Constants.ServiceNames.TradingService)
            .SingleInstance();
    }
}
```

### Test Patterns

**Test class structure**:
```csharp
public class PriceTests
{
    #region Create

    [Theory]
    [InlineData(0)]
    [InlineData(100.50)]
    public void Create_WithValidValue_CreatesPrice(decimal value)
    {
        // Arrange - nothing needed for simple cases

        // Act
        var price = Price.Create(value);

        // Assert
        price.Value.Should().Be(value);
    }

    #endregion
}
```

**Test naming convention**: `MethodName_Scenario_ExpectedResult`

**Common test libraries**:
- xUnit for test framework
- FluentAssertions for assertions
- Moq for mocking

**Running tests**:
```bash
dotnet test                                 # Run all tests
dotnet test --filter "FullyQualifiedName~Trading"  # Filter by name
dotnet test --collect:"XPlat Code Coverage"        # With coverage
```

---

## Quick Reference

### Build & Run

```bash
# Build
dotnet build IntelliTrader.sln

# Run (virtual trading mode by default)
dotnet run --project IntelliTrader

# Access dashboard
open http://localhost:7000
```

### Encrypt API Keys for Live Trading

```bash
dotnet run --project IntelliTrader -- \
  --encrypt --path keys.bin \
  --publickey YOUR_API_KEY \
  --privatekey YOUR_API_SECRET
```

### Key Constants (`IntelliTrader.Core/Models/Constants.cs`)

```csharp
Constants.ServiceNames.TradingService  // "Trading"
Constants.Trading.DefaultMaxOrderHistorySize  // 10000
Constants.Resilience.DefaultReadTimeoutSeconds  // 30
Constants.WebSocket.BinanceStreamUrl  // WebSocket endpoint
```

---

## Next Steps

1. **Explore the tests** - Best way to understand expected behavior
2. **Read the rules config** - `config/rules.json` shows how trading decisions are made
3. **Check domain events** - `docs/design/DOMAIN_EVENTS_DESIGN.md` for event-driven architecture
4. **Review Polly resilience** - `docs/design/POLLY_RESILIENCE_DESIGN.md` for error handling
5. **Run in virtual mode** - Safe way to see the system in action

Welcome to the team!
