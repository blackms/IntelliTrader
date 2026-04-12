# IntelliTrader Architecture Documentation

> Comprehensive architecture reference for the IntelliTrader cryptocurrency trading bot

**Version:** 0.9.9.2
**Target Framework:** .NET 9.0
**Last Updated:** January 2025

---

## Table of Contents

1. [Solution Overview](#solution-overview)
2. [Component Catalog](#component-catalog)
3. [Dependency Graph](#dependency-graph)
4. [Entrypoints and Runtime Modes](#entrypoints-and-runtime-modes)
5. [Data Flow Overview](#data-flow-overview)
6. [Service Architecture](#service-architecture)
7. [Configuration System](#configuration-system)

---

## Solution Overview

IntelliTrader is a modular cryptocurrency trading bot designed for the Binance exchange. The solution follows a layered architecture with clear separation of concerns:

- **Domain-Driven Design (DDD)** patterns in the Domain layer
- **Hexagonal Architecture (Ports and Adapters)** in the Application layer
- **Plugin Architecture** via Autofac module auto-discovery
- **CQRS** pattern for command/query separation

```
IntelliTrader.sln
|
+-- IntelliTrader/                    # Main executable (entry point)
+-- IntelliTrader.Core/               # Core services, interfaces, configuration
+-- IntelliTrader.Domain/             # Domain entities, value objects, events
+-- IntelliTrader.Application/        # Use cases, commands, queries, ports
+-- IntelliTrader.Infrastructure/     # Adapters, dispatchers, telemetry
+-- IntelliTrader.Trading/            # Trading operations, position sizing
+-- IntelliTrader.Rules/              # Rule engine, specifications
+-- IntelliTrader.Signals.Base/       # Signal service abstraction
+-- IntelliTrader.Signals.TradingView/ # TradingView signal receiver
+-- IntelliTrader.Exchange.Base/      # Exchange abstraction layer
+-- IntelliTrader.Exchange.Binance/   # Binance implementation with Polly resilience
+-- IntelliTrader.Web/                # ASP.NET Core dashboard, SignalR
+-- IntelliTrader.Backtesting/        # Historical replay engine
```

---

## Component Catalog

### IntelliTrader (Main Executable)

**Purpose:** Application entry point and composition root

| File | Description |
|------|-------------|
| `Program.cs` | Entry point, container building, key encryption CLI |

**Key Responsibilities:**
- Parse command-line arguments
- Build Autofac DI container via `ApplicationBootstrapper`
- Start `ICoreService` which orchestrates all services
- Provide key encryption utility for secure API credential storage

---

### IntelliTrader.Core

**Purpose:** Foundation layer with core services, interfaces, and configuration

#### Services

| Service | Interface | Description |
|---------|-----------|-------------|
| `CoreService` | `ICoreService` | Orchestrator for all services and timed tasks |
| `LoggingService` | `ILoggingService` | Serilog-based logging with memory sink |
| `NotificationService` | `INotificationService` | Telegram notification integration |
| `HealthCheckService` | `IHealthCheckService` | Health monitoring and status tracking |
| `CachingService` | `ICachingService` | In-memory caching with refresh |
| `ConfigProvider` | `IConfigProvider` | JSON configuration management with hot-reload |
| `ApplicationBootstrapper` | `IApplicationBootstrapper` | DI container builder with module discovery |
| `ApplicationContext` | `IApplicationContext` | Runtime context (speed multiplier for backtesting) |

#### Timed Tasks

| Task | Interval | Description |
|------|----------|-------------|
| `HealthCheckTimedTask` | Configurable | Monitors service health, triggers trading suspension |
| `HighResolutionTimedTask` | Base class | High-frequency polling task infrastructure |
| `LowResolutionTimedTask` | Base class | Lower-frequency task infrastructure |

#### Key Models

| Model | Description |
|-------|-------------|
| `BuyOptions` / `SellOptions` / `SwapOptions` | Trading operation parameters |
| `TradeResult` | Result of a trading operation |
| `DCALevel` | Dollar-cost averaging level configuration |
| `BoundedConcurrentStack<T>` | Thread-safe collection with automatic pruning |

#### Configuration Models

| Config | File | Description |
|--------|------|-------------|
| `CoreConfig` | `core.json` | Health check settings, instance name |
| `TradingConfig` | `trading.json` | Market, exchange, buy/sell parameters |
| `SignalsConfig` | `signals.json` | Signal definitions and receivers |
| `RulesConfig` | `rules.json` | Signal and trading rules |
| `NotificationConfig` | `notification.json` | Telegram settings |
| `CachingConfig` | `caching.json` | Cache TTL settings |
| `LoggingConfig` | `logging.json` | Serilog configuration |
| `WebConfig` | `web.json` | Dashboard port and authentication |
| `BacktestingConfig` | `backtesting.json` | Replay settings |

#### External Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Autofac | 8.1.1 | Dependency injection container |
| Serilog | 4.1.0 | Structured logging |
| Serilog.Sinks.Console | 6.0.0 | Console logging |
| Serilog.Sinks.File | 6.0.0 | File logging |
| Telegram.Bot | 22.0.0 | Telegram notifications |
| FluentValidation | 11.11.0 | Input validation |
| Microsoft.Extensions.Configuration | 9.0.0 | Configuration binding |

---

### IntelliTrader.Domain

**Purpose:** Domain entities, value objects, and domain events (DDD)

#### Aggregates

| Aggregate | Description |
|-----------|-------------|
| `Position` | Trading position with entries, margin, and P&L tracking |
| `Portfolio` | Collection of positions with balance management |

#### Value Objects

| Value Object | Description |
|--------------|-------------|
| `TradingPair` | Immutable trading pair identifier |
| `Money` | Currency amount with proper decimal handling |
| `Price` | Price point with validation |
| `Quantity` | Order quantity with validation |
| `Margin` | Position margin percentage |
| `SignalRating` | Signal strength rating (-1 to 1) |
| `PositionId` / `OrderId` / `PortfolioId` | Strongly-typed identifiers |

#### Domain Events

| Event | Description |
|-------|-------------|
| `OrderPlacedEvent` | Fired when an order is submitted |
| `OrderFilledEvent` | Fired when an order is filled |
| `PositionOpened` / `PositionClosed` | Position lifecycle events |
| `DCAExecuted` | Dollar-cost averaging executed |
| `StopLossTriggeredEvent` | Stop-loss order triggered |
| `TradingSuspendedEvent` / `TradingResumedEvent` | Trading state changes |
| `RiskLimitBreachedEvent` | Risk management threshold exceeded |
| `SignalReceivedEvent` | New signal received |

#### Domain Services

| Service | Description |
|---------|-------------|
| `RuleEvaluator` | Evaluates trading rules against context |
| `MarginCalculator` | Calculates position margins |
| `TradingConstraintValidator` | Validates trading constraints |

#### Specifications

| Specification | Description |
|---------------|-------------|
| `SignalSpecifications` | Signal-based trading conditions |
| `GlobalSpecifications` | Global market conditions |
| `PositionSpecifications` | Position-based conditions |

---

### IntelliTrader.Application

**Purpose:** Use cases, commands, queries, and ports (Hexagonal Architecture)

#### Commands

| Command | Handler | Description |
|---------|---------|-------------|
| `PlaceBuyOrderCommand` | `PlaceBuyOrderHandler` | Execute buy order |
| `PlaceSellOrderCommand` | `PlaceSellOrderHandler` | Execute sell order |
| `PlaceSwapOrderCommand` | `PlaceSwapOrderHandler` | Execute swap operation |
| `OpenPositionCommand` | `OpenPositionHandler` | Open new position |
| `ClosePositionCommand` | `ClosePositionHandler` | Close existing position |
| `ExecuteDCACommand` | `ExecuteDCAHandler` | Execute DCA operation |

#### Queries

| Query | Description |
|-------|-------------|
| `GetPortfolioStatusQuery` | Portfolio balance and positions |
| `GetTradingPairsQuery` | Active trading pairs |
| `PositionQueries` | Position data queries |
| `PortfolioQueries` | Portfolio data queries |

#### Driving Ports (Primary/Input)

| Port | Description |
|------|-------------|
| `ICommandDispatcher` | Dispatches commands to handlers |
| `IQueryDispatcher` | Dispatches queries to handlers |
| `ITradingUseCase` | Trading operation use case |
| `ISignalUseCase` | Signal processing use case |

#### Driven Ports (Secondary/Output)

| Port | Description |
|------|-------------|
| `IExchangePort` | Exchange operations abstraction |
| `IPositionRepository` | Position persistence |
| `IPortfolioRepository` | Portfolio persistence |
| `ISignalProviderPort` | Signal data provider |
| `INotificationPort` | Notification delivery |
| `IDomainEventDispatcher` | Domain event publishing |

#### Application Services

| Service | Description |
|---------|-------------|
| `TradingRuleProcessor` | Processes trading rules |
| `TrailingManager` | Manages trailing buy/sell states |

---

### IntelliTrader.Infrastructure

**Purpose:** Adapters, dispatchers, and cross-cutting concerns

#### Adapters

| Adapter | Port | Description |
|---------|------|-------------|
| `LegacyTradingServiceAdapter` | `ILegacyTradingServiceAdapter` | Bridges old `ITradingService` to new architecture |
| `BinanceExchangeAdapter` | `IExchangePort` | Binance API adapter |
| `TradingViewSignalAdapter` | `ISignalProviderPort` | TradingView signal adapter |
| `JsonPositionRepository` | `IPositionRepository` | JSON file persistence |

#### Dispatchers

| Dispatcher | Description |
|------------|-------------|
| `InMemoryCommandDispatcher` | Resolves and invokes command handlers |
| `InMemoryQueryDispatcher` | Resolves and invokes query handlers |
| `InMemoryDomainEventDispatcher` | Publishes domain events to handlers |

#### Background Services

| Service | Description |
|---------|-------------|
| `TradingRuleProcessorService` | Continuously evaluates trading rules |
| `OrderExecutionService` | Processes pending order queue |
| `SignalRuleProcessorService` | Evaluates signal rules for buy triggers |

#### Telemetry

| Component | Description |
|-----------|-------------|
| `TradingTelemetry` | OpenTelemetry metrics and traces |
| `TelemetryServiceCollectionExtensions` | DI registration helpers |

#### External Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Autofac | 8.2.0 | Dependency injection |
| System.Reactive | 6.0.0 | Reactive extensions |
| OpenTelemetry | 1.9.0 | Observability |
| OpenTelemetry.Instrumentation.AspNetCore | 1.9.0 | ASP.NET Core tracing |
| OpenTelemetry.Instrumentation.Http | 1.9.0 | HTTP client tracing |

---

### IntelliTrader.Trading

**Purpose:** Trading operations, position sizing, and orchestration

#### Services

| Service | Interface | Description |
|---------|-----------|-------------|
| `TradingService` | `ITradingService` | Facade coordinating all trading operations |
| `BuyOrchestrator` | `IBuyOrchestrator` | Buy order execution with swap detection |
| `SellOrchestrator` | `ISellOrchestrator` | Sell order execution with trailing support |
| `SwapOrchestrator` | `ISwapOrchestrator` | Atomic sell-then-buy swap operations |
| `TrailingOrderManager` | `ITrailingOrderManager` | Manages trailing buy/sell states |
| `ATRCalculator` | `IATRCalculator` | Average True Range calculations |
| `DynamicStopLossManager` | `IDynamicStopLossManager` | ATR-based trailing stop-loss |
| `PortfolioRiskManager` | `IPortfolioRiskManager` | Aggregate risk management |

#### Position Sizing

| Sizer | Description |
|-------|-------------|
| `FixedPercentagePositionSizer` | Fixed percentage of balance |
| `KellyCriterionPositionSizer` | Kelly criterion optimal sizing |
| `PositionSizerFactory` | Factory for position sizers |

#### Account Models

| Model | Description |
|-------|-------------|
| `VirtualAccount` | Paper trading account |
| `ExchangeAccount` | Live exchange account |
| `TradingAccountBase` | Common account operations |
| `TradingPair` | Active position tracking |

---

### IntelliTrader.Rules

**Purpose:** Rule engine with specification pattern

#### Services

| Service | Interface | Description |
|---------|-----------|-------------|
| `RulesService` | `IRulesService` | Rule evaluation and management |

#### Models

| Model | Description |
|-------|-------------|
| `Rule` | Rule definition with conditions |
| `RuleCondition` | Individual rule condition |
| `ModuleRules` | Rules grouped by module |
| `RuleTrailing` | Trailing rule configuration |

#### Specifications

| Specification | Description |
|---------------|-------------|
| `RatingSpecification` | Signal rating conditions |
| `PriceSpecification` | Price-based conditions |
| `VolumeSpecification` | Volume conditions |
| `VolatilitySpecification` | Volatility conditions |
| `MarginSpecification` | Position margin conditions |
| `AgeSpecification` | Position age conditions |
| `DCALevelSpecification` | DCA level conditions |
| `GlobalRatingSpecification` | Global market rating |
| `PairsSpecification` | Pair inclusion/exclusion |
| `SignalRulesSpecification` | Signal rule evaluation |
| `AmountSpecification` | Amount conditions |
| `ConditionSpecificationBuilder` | Builds specifications from conditions |

---

### IntelliTrader.Signals.Base

**Purpose:** Signal service abstraction and base implementation

#### Services

| Service | Interface | Description |
|---------|-----------|-------------|
| `SignalsService` | `ISignalsService` | Aggregates signals from all receivers |

#### Interfaces

| Interface | Description |
|-----------|-------------|
| `ISignalReceiver` | Signal data receiver contract |
| `ISignal` | Signal data contract |
| `ISignalDefinition` | Signal definition contract |
| `ISignalTrailingInfo` | Trailing signal information |

---

### IntelliTrader.Signals.TradingView

**Purpose:** TradingView signal integration

#### Services

| Service | Description |
|---------|-------------|
| `TradingViewCryptoSignalReceiver` | TradingView API client |

#### Timed Tasks

| Task | Interval | Description |
|------|----------|-------------|
| `TradingViewCryptoSignalPollingTimedTask` | ~7s | Polls TradingView for signals |

---

### IntelliTrader.Exchange.Base

**Purpose:** Exchange abstraction layer

#### Models

| Model | Description |
|-------|-------------|
| `Ticker` | Price ticker data |
| `Order` | Order request model |
| `OrderDetails` | Filled order details |

#### External Packages

| Package | Version | Purpose |
|---------|---------|---------|
| DigitalRuby.ExchangeSharp | 1.2.1 | Exchange API library |

---

### IntelliTrader.Exchange.Binance

**Purpose:** Binance exchange implementation with resilience

#### Services

| Service | Description |
|---------|-------------|
| `BinanceExchangeService` | Binance API operations |
| `BinanceWebSocketService` | WebSocket ticker streaming |

#### Timed Tasks

| Task | Interval | Description |
|------|----------|-------------|
| `BinanceTickersMonitorTimedTask` | ~1s | Monitors WebSocket health |

#### Resilience

| Component | Description |
|-----------|-------------|
| `ExchangeResiliencePipelines` | Polly pipelines for retry, circuit breaker, rate limiting |
| `ResilienceConfig` | Resilience policy configuration |

#### External Packages

| Package | Version | Purpose |
|---------|---------|---------|
| Polly | 8.2.1 | Resilience and transient fault handling |
| Polly.RateLimiting | 8.2.1 | Rate limiting |
| Polly.Extensions | 8.2.1 | Polly DI extensions |

---

### IntelliTrader.Web

**Purpose:** ASP.NET Core dashboard with SignalR real-time updates

#### Controllers

| Controller | Description |
|------------|-------------|
| `HomeController` | Dashboard views (Market, Trades, Stats, Settings) |

#### SignalR

| Hub | Description |
|-----|-------------|
| `TradingHub` | Real-time trading updates |

#### Background Services

| Service | Description |
|---------|-------------|
| `SignalRBroadcasterService` | Pushes updates to connected clients |

#### Services

| Service | Interface | Description |
|---------|-----------|-------------|
| `WebService` | `IWebService` | Kestrel web host management |
| `PasswordService` | `IPasswordService` | BCrypt password hashing |
| `TradingHubNotifier` | `ITradingHubNotifier` | SignalR broadcast helper |

#### View Models

| ViewModel | Description |
|-----------|-------------|
| `DashboardViewModel` | Main dashboard data |
| `MarketViewModel` | Market overview |
| `TradesViewModel` | Trade history |
| `StatsViewModel` | Trading statistics |
| `SettingsViewModel` | Configuration display |
| `LogViewModel` | Log viewer |

#### External Packages

| Package | Version | Purpose |
|---------|---------|---------|
| BCrypt.Net-Next | 4.0.3 | Password hashing |
| OpenTelemetry | 1.9.0 | Observability |

---

### IntelliTrader.Backtesting

**Purpose:** Historical data replay engine

#### Services

| Service | Interface | Description |
|---------|-----------|-------------|
| `BacktestingService` | `IBacktestingService` | Backtesting orchestration |
| `BacktestingExchangeService` | `IExchangeService` | Replays historical ticker data |
| `BacktestingSignalsService` | `ISignalsService` | Replays historical signals |

#### Timed Tasks

| Task | Description |
|------|-------------|
| `BacktestingLoadSnapshotsTimedTask` | Loads snapshots during replay |
| `BacktestingSaveSnapshotsTimedTask` | Saves snapshots for future replay |

#### External Packages

| Package | Version | Purpose |
|---------|---------|---------|
| MessagePack | 2.5.187 | Binary serialization for snapshots |

---

## Dependency Graph

### Internal Project Dependencies

```mermaid
graph TB
    subgraph "Executable"
        IT[IntelliTrader]
    end

    subgraph "Core Layer"
        Core[IntelliTrader.Core]
        Domain[IntelliTrader.Domain]
        App[IntelliTrader.Application]
    end

    subgraph "Infrastructure Layer"
        Infra[IntelliTrader.Infrastructure]
    end

    subgraph "Trading Layer"
        Trading[IntelliTrader.Trading]
        Rules[IntelliTrader.Rules]
    end

    subgraph "Signal Layer"
        SigBase[IntelliTrader.Signals.Base]
        SigTV[IntelliTrader.Signals.TradingView]
    end

    subgraph "Exchange Layer"
        ExBase[IntelliTrader.Exchange.Base]
        ExBinance[IntelliTrader.Exchange.Binance]
    end

    subgraph "Presentation Layer"
        Web[IntelliTrader.Web]
    end

    subgraph "Support Layer"
        Backtest[IntelliTrader.Backtesting]
    end

    %% Main executable dependencies
    IT --> Core
    IT --> Backtest
    IT --> ExBase
    IT --> ExBinance
    IT --> Rules
    IT --> SigBase
    IT --> SigTV
    IT --> Trading
    IT --> Web

    %% Core layer
    App --> Domain
    Infra --> App
    Infra --> Core
    Infra --> Domain

    %% Trading layer
    Trading --> App
    Trading --> Core
    Trading --> Domain
    Trading --> ExBase
    Rules --> Core

    %% Signal layer
    SigBase --> Core
    SigTV --> Core
    SigTV --> SigBase

    %% Exchange layer
    ExBase --> Core
    ExBinance --> Core
    ExBinance --> ExBase

    %% Presentation layer
    Web --> Core
    Web --> Infra

    %% Support layer
    Backtest --> Core
    Backtest --> ExBase
    Backtest --> SigBase
```

### External Package Dependencies

```mermaid
graph LR
    subgraph "DI Container"
        Autofac[Autofac 8.x]
    end

    subgraph "Logging"
        Serilog[Serilog 4.x]
    end

    subgraph "Configuration"
        MSConfig[MS.Extensions.Configuration 9.x]
    end

    subgraph "Resilience"
        Polly[Polly 8.x]
    end

    subgraph "Observability"
        OTel[OpenTelemetry 1.9.x]
    end

    subgraph "Web"
        ASPCore[ASP.NET Core 9.0]
        SignalR[SignalR]
        BCrypt[BCrypt.Net-Next]
    end

    subgraph "Exchange"
        ExSharp[ExchangeSharp 1.2.1]
    end

    subgraph "Serialization"
        MsgPack[MessagePack 2.5.x]
    end

    subgraph "Notifications"
        TgBot[Telegram.Bot 22.x]
    end

    subgraph "Validation"
        FluentVal[FluentValidation 11.x]
    end

    Core --> Autofac
    Core --> Serilog
    Core --> MSConfig
    Core --> TgBot
    Core --> FluentVal

    ExBinance --> Polly
    ExBase --> ExSharp

    Infra --> OTel
    Web --> ASPCore
    Web --> SignalR
    Web --> BCrypt
    Web --> OTel

    Backtest --> MsgPack
```

---

## Entrypoints and Runtime Modes

### Entry Point

The application starts in `IntelliTrader/Program.cs`:

```
Program.Main()
  |
  +-- ParseCommandLineArgs()
  |
  +-- [No args] --> StartCoreService()
  |                   |
  |                   +-- BuildAndConfigureContainer()
  |                   |     +-- ApplicationBootstrapper.BuildContainer()
  |                   |     +-- Auto-discover IntelliTrader.*.dll modules
  |                   |     +-- Register all AppModule classes
  |                   |
  |                   +-- ICoreService.Start()
  |                         +-- Start enabled services
  |                         +-- Start timed tasks
  |
  +-- [--encrypt] --> EncryptKeys()
                        +-- Encrypt API credentials to keys.bin
```

### Runtime Modes

#### 1. Live Trading Mode

**Configuration:**
```json
// trading.json
{
  "Trading": {
    "Enabled": true,
    "VirtualTrading": false,
    "Exchange": "Binance"
  }
}

// backtesting.json
{
  "Backtesting": {
    "Enabled": false
  }
}
```

**Behavior:**
- Connects to Binance via WebSocket for real-time ticker data
- Uses `ExchangeAccount` for actual balance and order execution
- Polly resilience policies protect against API failures
- Orders are executed on the live exchange

```mermaid
sequenceDiagram
    participant User
    participant Core as CoreService
    participant Trading as TradingService
    participant Exchange as BinanceExchangeService
    participant Binance as Binance API

    User->>Core: Start()
    Core->>Trading: Start()
    Trading->>Exchange: Start(virtualTrading=false)
    Exchange->>Binance: Connect WebSocket
    Binance-->>Exchange: Ticker stream

    loop Trading Loop
        Exchange->>Trading: TickersUpdated event
        Trading->>Trading: Evaluate rules
        Trading->>Exchange: PlaceOrder()
        Exchange->>Binance: POST /api/v3/order
        Binance-->>Exchange: Order result
        Exchange-->>Trading: IOrderDetails
    end
```

#### 2. Virtual Trading Mode (Paper Trading)

**Configuration:**
```json
// trading.json
{
  "Trading": {
    "Enabled": true,
    "VirtualTrading": true,
    "VirtualAccountInitialBalance": 0.12,
    "VirtualAccountFilePath": "data/virtual-account.json"
  }
}
```

**Behavior:**
- Connects to Binance for real-time ticker data (read-only)
- Uses `VirtualAccount` for simulated balance and orders
- No actual orders are placed on the exchange
- Account state persisted to JSON file

```mermaid
sequenceDiagram
    participant User
    participant Core as CoreService
    participant Trading as TradingService
    participant VAccount as VirtualAccount
    participant Exchange as BinanceExchangeService
    participant Binance as Binance API

    User->>Core: Start()
    Core->>Trading: Start()
    Trading->>VAccount: Load from JSON
    Trading->>Exchange: Start(virtualTrading=true)
    Exchange->>Binance: Connect WebSocket (read-only)

    loop Trading Loop
        Binance-->>Exchange: Ticker stream
        Exchange->>Trading: TickersUpdated event
        Trading->>Trading: Evaluate rules
        Trading->>VAccount: SimulateBuy/Sell
        VAccount->>VAccount: Update balance
        VAccount->>VAccount: Save to JSON
    end
```

#### 3. Backtesting Mode (Snapshot Recording)

**Configuration:**
```json
// backtesting.json
{
  "Backtesting": {
    "Enabled": true,
    "Replay": false,
    "SnapshotsInterval": 1,
    "SnapshotsPath": "data/backtesting"
  }
}
```

**Behavior:**
- Records ticker and signal snapshots to MessagePack files
- Runs normal trading (virtual or live) while recording
- Snapshots stored for later replay

#### 4. Backtesting Mode (Replay)

**Configuration:**
```json
// backtesting.json
{
  "Backtesting": {
    "Enabled": true,
    "Replay": true,
    "ReplaySpeed": 250,
    "ReplayStartIndex": null,
    "ReplayEndIndex": null
  }
}
```

**Behavior:**
- Replaces `IExchangeService` with `BacktestingExchangeService`
- Replaces `ISignalsService` with `BacktestingSignalsService`
- Loads historical snapshots and replays them at configurable speed
- Uses `ApplicationContext.Speed` to accelerate time calculations
- Virtual account tracks simulated performance

```mermaid
sequenceDiagram
    participant Core as CoreService
    participant Backtest as BacktestingService
    participant BTExchange as BacktestingExchangeService
    participant BTSignals as BacktestingSignalsService
    participant Trading as TradingService

    Core->>Backtest: Start()
    Backtest->>Backtest: Load snapshots from disk

    loop Replay Loop
        Backtest->>BTExchange: GetCurrentTickers()
        BTExchange->>Backtest: Historical ticker data

        Backtest->>BTSignals: GetCurrentSignals()
        BTSignals->>Backtest: Historical signal data

        Trading->>Trading: Evaluate rules
        Trading->>Trading: Execute virtual trades

        Backtest->>Backtest: Advance snapshot index
        Note over Backtest: Speed: 250x real-time
    end

    Backtest->>Backtest: Complete()
    Note over Backtest: Report final P&L
```

---

## Data Flow Overview

### Signal to Trade Execution Flow

```mermaid
flowchart TB
    subgraph External["External Data Sources"]
        TV[TradingView API]
        BN[Binance WebSocket]
    end

    subgraph Signals["Signal Acquisition"]
        TVTask[TradingViewCryptoSignalPollingTimedTask]
        TVRecv[TradingViewCryptoSignalReceiver]
        SigSvc[SignalsService]
    end

    subgraph Tickers["Price Updates"]
        BNTask[BinanceTickersMonitorTimedTask]
        BNSvc[BinanceExchangeService]
        WS[WebSocket Stream]
    end

    subgraph Rules["Rule Evaluation"]
        RuleSvc[RulesService]
        SigRules[Signal Rules]
        TrdRules[Trading Rules]
        Specs[Specifications]
    end

    subgraph Trading["Order Execution"]
        TrdSvc[TradingService]
        BuyOrch[BuyOrchestrator]
        SellOrch[SellOrchestrator]
        SwapOrch[SwapOrchestrator]
        Trail[TrailingOrderManager]
    end

    subgraph Account["Account Management"]
        VirtAcct[VirtualAccount]
        ExchAcct[ExchangeAccount]
        Risk[PortfolioRiskManager]
    end

    subgraph Notification["Notifications"]
        NotifSvc[NotificationService]
        TgBot[Telegram]
        SignalRHub[SignalR Hub]
    end

    %% Signal flow
    TV -->|HTTP Poll| TVTask
    TVTask --> TVRecv
    TVRecv --> SigSvc

    %% Ticker flow
    BN -->|WebSocket| WS
    WS --> BNSvc
    BNTask -->|Monitor| BNSvc

    %% Rule evaluation
    SigSvc -->|Signals| RuleSvc
    BNSvc -->|Prices| RuleSvc
    RuleSvc --> SigRules
    RuleSvc --> TrdRules
    SigRules --> Specs
    TrdRules --> Specs

    %% Trading execution
    SigRules -->|Buy Signal| TrdSvc
    TrdRules -->|Sell/DCA Signal| TrdSvc
    TrdSvc --> Trail
    Trail -->|Trailing Buy| BuyOrch
    Trail -->|Trailing Sell| SellOrch
    TrdSvc --> SwapOrch

    %% Account updates
    BuyOrch --> VirtAcct
    BuyOrch --> ExchAcct
    SellOrch --> VirtAcct
    SellOrch --> ExchAcct
    Risk -->|Risk Check| TrdSvc

    %% Notifications
    TrdSvc -->|Trade Event| NotifSvc
    TrdSvc -->|Trade Event| SignalRHub
    NotifSvc --> TgBot
```

### Detailed Signal Processing Pipeline

```mermaid
sequenceDiagram
    participant TV as TradingView
    participant Poll as SignalPollingTask
    participant Recv as SignalReceiver
    participant SigSvc as SignalsService
    participant RuleSvc as RulesService
    participant TrdSvc as TradingService
    participant Trail as TrailingManager
    participant Orch as BuyOrchestrator
    participant Exch as ExchangeService

    loop Every 7 seconds
        Poll->>TV: POST /screener
        TV-->>Poll: Signal data (JSON)
        Poll->>Recv: Parse signals
        Recv->>SigSvc: Update signals
        SigSvc->>SigSvc: Calculate ratings
    end

    Note over RuleSvc: Signal Rules Check (3s interval)

    RuleSvc->>SigSvc: GetSignalsByPair(pair)
    SigSvc-->>RuleSvc: Dictionary<signal, rating>
    RuleSvc->>RuleSvc: CheckConditions(signalRules)

    alt Conditions Met
        RuleSvc->>TrdSvc: Buy(options)
        TrdSvc->>TrdSvc: CanBuy() validation

        alt Trailing Enabled
            TrdSvc->>Trail: StartTrailingBuy(pair)
            Trail->>Trail: Track price movement

            loop Until Stop Margin Hit
                Trail->>Exch: GetCurrentPrice(pair)
                Exch-->>Trail: current price
                Trail->>Trail: Update trailing stop
            end

            Trail->>Orch: Execute buy
        else Immediate Buy
            TrdSvc->>Orch: Execute buy
        end

        Orch->>Exch: PlaceOrder(order)
        Exch-->>Orch: OrderDetails
        Orch->>TrdSvc: LogOrder(order)
    end
```

### Trading Rules Processing (Sell/DCA)

```mermaid
sequenceDiagram
    participant Exch as ExchangeService
    participant TrdSvc as TradingService
    participant RuleSvc as RulesService
    participant Acct as TradingAccount
    participant Trail as TrailingManager
    participant Sell as SellOrchestrator
    participant Risk as PortfolioRiskManager

    Note over TrdSvc: Trading Rules Check (3s interval)

    TrdSvc->>Acct: GetTradingPairs()
    Acct-->>TrdSvc: List<TradingPair>

    loop For each trading pair
        TrdSvc->>Exch: GetCurrentPrice(pair)
        Exch-->>TrdSvc: current price
        TrdSvc->>TrdSvc: Calculate margin

        TrdSvc->>RuleSvc: CheckConditions(sellRules)

        alt Sell Conditions Met
            TrdSvc->>Risk: CheckRiskLimits()
            Risk-->>TrdSvc: Approved

            alt Trailing Enabled
                TrdSvc->>Trail: StartTrailingSell(pair)

                loop Until Stop Margin Hit
                    Trail->>Trail: Track price upward
                end

                Trail->>Sell: Execute sell
            else Immediate Sell
                TrdSvc->>Sell: Execute sell
            end

            Sell->>Exch: PlaceOrder(sellOrder)
            Exch-->>Sell: OrderDetails
        end

        TrdSvc->>RuleSvc: CheckConditions(dcaRules)

        alt DCA Conditions Met
            TrdSvc->>TrdSvc: GetNextDCALevel()
            TrdSvc->>TrdSvc: Buy(dcaOptions)
        end
    end
```

---

## Service Architecture

### Service Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Configured: Container Built
    Configured --> Starting: ICoreService.Start()
    Starting --> Running: All Services Started
    Running --> Suspended: Health Check Failed
    Suspended --> Running: Health Check Passed
    Running --> Stopping: ICoreService.Stop()
    Stopping --> Stopped: All Services Stopped
    Stopped --> [*]

    Running --> Restarting: ICoreService.Restart()
    Restarting --> Stopping
```

### Service Dependencies

```mermaid
graph TB
    subgraph "Startup Order"
        direction TB
        A[1. LoggingService]
        B[2. ConfigProvider]
        C[3. HealthCheckService]
        D[4. BacktestingService]
        E[5. ExchangeService]
        F[6. SignalsService]
        G[7. RulesService]
        H[8. TradingService]
        I[9. NotificationService]
        J[10. WebService]
        K[11. CoreService starts all tasks]
    end

    A --> B --> C --> D --> E --> F --> G --> H --> I --> J --> K
```

---

## Configuration System

### Configuration Files

| File | Hot-Reload | Description |
|------|------------|-------------|
| `core.json` | Yes | Core service settings |
| `trading.json` | Yes | Trading parameters |
| `signals.json` | Yes | Signal definitions |
| `rules.json` | Yes | Trading and signal rules |
| `exchange.json` | No | Exchange API settings |
| `web.json` | No | Web dashboard settings |
| `notification.json` | Yes | Telegram settings |
| `caching.json` | Yes | Cache TTL settings |
| `logging.json` | No | Serilog configuration |
| `backtesting.json` | No | Backtesting settings |

### Configuration Loading

```mermaid
sequenceDiagram
    participant Boot as ApplicationBootstrapper
    participant CP as ConfigProvider
    participant Svc as ConfigurableService
    participant FSW as FileSystemWatcher

    Boot->>CP: new ConfigProvider()
    CP->>CP: Load all JSON files
    CP->>CP: Build IConfiguration

    Boot->>Boot: BuildContainer()
    Boot->>Svc: Register services

    Svc->>CP: GetSection<TConfig>(serviceName)
    CP-->>Svc: TConfig instance

    Note over FSW: File changed on disk
    FSW->>CP: OnConfigFileChanged
    CP->>CP: Reload configuration
    CP->>Svc: Notify via callback
    Svc->>Svc: Reapply configuration
```

---

## Appendix

### Constants Reference

```csharp
public static class Constants
{
    public static class ServiceNames
    {
        public const string CoreService = "Core";
        public const string TradingService = "Trading";
        public const string SignalsService = "Signals";
        public const string RulesService = "Rules";
        public const string WebService = "Web";
        public const string BacktestingService = "Backtesting";
        // ... etc
    }

    public static class TimedTasks
    {
        public const int StandardDelay = 3000;      // 3s
        public const int DefaultIntervalMs = 1000;  // 1s
    }

    public static class WebSocket
    {
        public const int PingIntervalSeconds = 20;
        public const int ReconnectDelaySeconds = 5;
        public const int MaxReconnectAttempts = 5;
        public const int MaxTickersAgeSeconds = 60;
    }

    public static class Resilience
    {
        public const int DefaultReadTimeoutSeconds = 30;
        public const int DefaultOrderTimeoutSeconds = 15;
        public const int DefaultMaxConcurrentReads = 10;
        public const int DefaultMaxConcurrentOrders = 3;
        public const int DefaultRateLimitPermitsPerMinute = 1000;
    }
}
```

### Health Check Names

| Health Check | Description |
|--------------|-------------|
| `AccountRefreshed` | Account balance updated |
| `TickersUpdated` | Price data received |
| `TradingPairsProcessed` | Trading pairs evaluated |
| `TradingViewCryptoSignalsReceived` | Signals received |
| `SignalRulesProcessed` | Signal rules evaluated |
| `TradingRulesProcessed` | Trading rules evaluated |

---

*Document generated from IntelliTrader source code analysis*
