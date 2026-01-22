# IntelliTrader High-Level Design (HLD)

This document provides a comprehensive architectural overview of the IntelliTrader cryptocurrency trading bot system.

## Table of Contents

1. [System Context](#1-system-context)
2. [Container View](#2-container-view)
3. [Key Business Flows](#3-key-business-flows)
4. [Data Flow Diagram](#4-data-flow-diagram)
5. [Deployment View](#5-deployment-view)

---

## 1. System Context

IntelliTrader is a .NET Core cryptocurrency trading bot that integrates with external exchanges and signal providers to execute automated trading strategies.

### System Context Diagram (C4 Level 1)

```mermaid
C4Context
    title System Context Diagram - IntelliTrader

    Person(trader, "Trader", "Monitors portfolio, configures rules, executes manual trades")

    System(intellitrader, "IntelliTrader", "Automated cryptocurrency trading bot with signal-based buying, DCA support, and risk management")

    System_Ext(binance, "Binance Exchange", "Cryptocurrency exchange providing market data and order execution")
    System_Ext(tradingview, "TradingView", "Technical analysis signals and market ratings")
    System_Ext(telegram, "Telegram", "Notification delivery for trade alerts")

    Rel(trader, intellitrader, "Configures rules, monitors via dashboard", "HTTPS :7000")
    Rel(intellitrader, binance, "Fetches tickers, places orders", "WebSocket/REST API")
    Rel(intellitrader, tradingview, "Polls signals every 7s", "HTTPS")
    Rel(intellitrader, telegram, "Sends trade notifications", "Bot API")
```

### Trust Boundaries

```mermaid
flowchart TB
    subgraph "Trust Boundary: Internal Network"
        subgraph "Application Server"
            IT[IntelliTrader Bot]
            WEB[Web Dashboard :7000]
            DATA[(Local Data Store)]
        end
    end

    subgraph "Trust Boundary: External Services"
        BINANCE[Binance Exchange API]
        TV[TradingView Scanner API]
        TG[Telegram Bot API]
    end

    subgraph "Trust Boundary: User Access"
        TRADER[Trader/Operator]
    end

    TRADER -->|HTTPS + Cookie Auth| WEB
    IT <-->|WebSocket + REST| BINANCE
    IT -->|HTTPS Polling| TV
    IT -->|HTTPS| TG
    IT <--> DATA
    WEB <--> IT
```

### External System Interactions

| External System | Protocol | Purpose | Authentication |
|----------------|----------|---------|----------------|
| Binance Exchange | WebSocket + REST | Real-time tickers, order placement | API Key/Secret (encrypted) |
| TradingView | HTTPS REST | Signal acquisition (ratings, volume, volatility) | None (public endpoint) |
| Telegram | HTTPS | Trade notifications | Bot Token |

---

## 2. Container View

### Container Diagram (C4 Level 2)

```mermaid
C4Container
    title Container Diagram - IntelliTrader

    Person(trader, "Trader", "Operator")

    Container_Boundary(intellitrader, "IntelliTrader System") {
        Container(web, "Web Dashboard", "ASP.NET Core MVC", "Dashboard, trading controls, configuration management")
        Container(core, "Core Service", ".NET Service", "Orchestrates all services, manages lifecycle")
        Container(trading, "Trading Service", ".NET Service", "Buy/Sell/Swap execution, position management")
        Container(signals, "Signals Service", ".NET Service", "Signal aggregation and rating calculation")
        Container(rules, "Rules Engine", ".NET Service", "Condition evaluation using Specification pattern")
        Container(exchange, "Exchange Service", ".NET Service", "Binance API wrapper with resilience")
        Container(notification, "Notification Service", ".NET Service", "Telegram alerts")
        Container(health, "Health Check Service", ".NET Service", "Service health monitoring")
        ContainerDb(config, "Config Files", "JSON", "Trading rules, signal definitions, settings")
        ContainerDb(account, "Account Data", "JSON", "Virtual/Exchange account state")
    }

    System_Ext(binance, "Binance Exchange", "External")
    System_Ext(tradingview, "TradingView", "External")
    System_Ext(telegram, "Telegram", "External")

    Rel(trader, web, "Uses", "HTTPS")
    Rel(web, core, "Controls")
    Rel(web, trading, "Manual trades")
    Rel(core, trading, "Starts/Stops")
    Rel(core, signals, "Starts/Stops")
    Rel(core, notification, "Starts/Stops")
    Rel(core, health, "Monitors")
    Rel(trading, exchange, "Places orders")
    Rel(trading, signals, "Gets ratings")
    Rel(trading, rules, "Evaluates conditions")
    Rel(signals, rules, "Evaluates buy conditions")
    Rel(exchange, binance, "API calls")
    Rel(signals, tradingview, "Polls signals")
    Rel(notification, telegram, "Sends alerts")
    Rel(trading, config, "Reads")
    Rel(trading, account, "Reads/Writes")
```

### Solution Structure

```
IntelliTrader.sln
|
+-- IntelliTrader/                    # Main executable, entry point
|   +-- Program.cs                    # Application bootstrapper
|   +-- config/                       # JSON configuration files
|
+-- IntelliTrader.Core/               # Core abstractions and services
|   +-- Interfaces/Services/          # Service contracts (ICoreService, etc.)
|   +-- Services/CoreService.cs       # Main orchestrator
|   +-- Models/Tasks/                 # HighResolutionTimedTask base
|
+-- IntelliTrader.Domain/             # Domain entities and value objects
+-- IntelliTrader.Application/        # Application services and ports
|
+-- IntelliTrader.Trading/            # Trading operations
|   +-- Services/TradingService.cs    # Facade for buy/sell/swap
|   +-- Services/*Orchestrator.cs     # Single-responsibility orchestrators
|   +-- Models/Accounts/              # Virtual/Exchange accounts
|
+-- IntelliTrader.Signals.Base/       # Signal service abstractions
+-- IntelliTrader.Signals.TradingView/# TradingView signal receiver
|
+-- IntelliTrader.Rules/              # Rule engine with Specification pattern
+-- IntelliTrader.Exchange.Base/      # Exchange abstractions
+-- IntelliTrader.Exchange.Binance/   # Binance implementation
|
+-- IntelliTrader.Infrastructure/     # Background services, telemetry
|   +-- BackgroundServices/           # Rule processors, order execution
|
+-- IntelliTrader.Web/                # ASP.NET Core dashboard
|   +-- Controllers/HomeController.cs # MVC controller
|   +-- Hubs/TradingHub.cs           # SignalR real-time updates
|   +-- MinimalApiEndpoints.cs       # REST API endpoints
|
+-- IntelliTrader.Backtesting/        # Historical replay support
+-- IntelliTrader.Integration.*/      # Third-party integrations
```

### Service Dependencies

```mermaid
graph TD
    subgraph "Presentation Layer"
        WEB[WebService<br/>Port 7000]
        HUB[TradingHub<br/>SignalR]
    end

    subgraph "Application Layer"
        CORE[CoreService<br/>Orchestrator]
    end

    subgraph "Domain Services"
        TRADING[TradingService<br/>Facade]
        SIGNALS[SignalsService]
        RULES[RulesService]
        HEALTH[HealthCheckService]
        NOTIFY[NotificationService]
    end

    subgraph "Infrastructure Layer"
        EXCHANGE[BinanceExchangeService]
        BGSVCS[Background Services]
        CONFIG[ConfigProvider]
    end

    subgraph "External"
        BINANCE[(Binance API)]
        TV[(TradingView API)]
        TG[(Telegram API)]
    end

    WEB --> CORE
    WEB --> TRADING
    HUB --> TRADING

    CORE --> TRADING
    CORE --> SIGNALS
    CORE --> NOTIFY
    CORE --> HEALTH
    CORE --> WEB

    TRADING --> EXCHANGE
    TRADING --> SIGNALS
    TRADING --> RULES
    TRADING --> NOTIFY

    SIGNALS --> RULES
    SIGNALS --> TV

    EXCHANGE --> BINANCE
    NOTIFY --> TG

    BGSVCS --> TRADING
    BGSVCS --> SIGNALS

    CONFIG -.-> CORE
    CONFIG -.-> TRADING
    CONFIG -.-> SIGNALS
    CONFIG -.-> RULES
```

---

## 3. Key Business Flows

### 3.1 Signal Acquisition Flow

TradingView signals are polled every 7 seconds to acquire market ratings and technical indicators.

```mermaid
sequenceDiagram
    participant Timer as HighResolutionTimedTask
    participant Receiver as TradingViewCryptoSignalReceiver
    participant HTTP as HttpClient
    participant TV as TradingView API
    participant Health as HealthCheckService
    participant Signals as SignalsService

    loop Every 7 seconds
        Timer->>Receiver: Run()
        Receiver->>Receiver: Build request with exchange/market
        Receiver->>HTTP: PostAsync(scanner URL, request)
        HTTP->>TV: POST /crypto/scan
        TV-->>HTTP: JSON response (signals array)
        HTTP-->>Receiver: Response content

        Receiver->>Receiver: Parse JSON signals
        Receiver->>Receiver: Calculate rating changes
        Receiver->>Receiver: Store historical snapshots
        Receiver->>Health: UpdateHealthCheck("TV Signals")

        Note over Receiver,Signals: Signals available via GetSignals()
    end
```

**Signal Data Structure:**
- **Pair**: Trading pair symbol (e.g., BTCUSDT)
- **Price**: Current price
- **PriceChange**: Price change percentage
- **Volume**: Trading volume
- **Rating**: Technical analysis rating (-1 to +1)
- **Volatility**: Price volatility metric
- **VolumeChange**: Volume change vs historical
- **RatingChange**: Rating change vs historical

### 3.2 Buy Decision Flow

The signal rule processor evaluates buy conditions against configured rules.

```mermaid
sequenceDiagram
    participant BGSvc as SignalRuleProcessorService
    participant Signals as SignalsService
    participant Rules as RulesService
    participant Trading as TradingService
    participant Account as TradingAccount
    participant Risk as PortfolioRiskManager

    loop Every 3 seconds
        BGSvc->>Signals: GetAllSignals()
        BGSvc->>Signals: GetGlobalRating()

        loop For each market pair
            BGSvc->>Rules: GetRules("Signals")

            loop For each signal rule
                BGSvc->>Rules: CheckConditions(conditions, signals, globalRating, pair)

                alt All conditions satisfied
                    BGSvc->>Trading: CanBuy(options)
                    Trading->>Account: GetBalance()
                    Trading->>Risk: IsCircuitBreakerTriggered()
                    Trading->>Risk: CanOpenPosition(positionRisk)

                    alt Buy allowed
                        Trading->>Trading: InitiateBuy(options)

                        alt Trailing enabled
                            Trading->>Trading: TrailingManager.InitiateTrailingBuy()
                        else Direct buy
                            Trading->>Trading: PlaceBuyOrder()
                        end
                    end
                end
            end
        end
    end
```

**Buy Rule Conditions (from rules.json):**
- MinVolume / MaxVolume
- MinRating / MaxRating
- MinGlobalRating / MaxGlobalRating
- MinPriceChange / MaxPriceChange

### 3.3 Sell/DCA Decision Flow

Trading rules determine when to sell positions or execute DCA (Dollar Cost Averaging) buys.

```mermaid
sequenceDiagram
    participant BGSvc as TradingRuleProcessorService
    participant Trading as TradingService
    participant Rules as RulesService
    participant Account as TradingAccount
    participant Signals as SignalsService

    loop Every 3 seconds
        BGSvc->>Account: GetTradingPairs()
        BGSvc->>Signals: GetGlobalRating()

        loop For each trading pair
            BGSvc->>Trading: GetPairConfig(pair)
            BGSvc->>Rules: GetRules("Trading")

            loop For each trading rule
                BGSvc->>Rules: CheckConditions(conditions, signals, globalRating, pair, tradingPair)

                alt Conditions match
                    BGSvc->>BGSvc: ApplyRuleModifiers(pairConfig, rule.Modifiers)
                end
            end

            alt Current margin >= SellMargin
                BGSvc->>Trading: Sell(options)
            else Current margin <= NextDCAMargin AND DCA enabled
                BGSvc->>Trading: Buy(DCA options)
            else StopLoss triggered
                BGSvc->>Trading: Sell(stopLoss options)
            end
        end
    end
```

**Trading Rule Modifiers:**
- BuyEnabled / SellEnabled
- SellMargin / SellTrailing
- SellStopLossEnabled / SellStopLossMargin
- BuyDCAEnabled / DCA levels

### 3.4 Order Execution Flow

Orders are placed through the exchange service with resilience patterns.

```mermaid
sequenceDiagram
    participant Trading as TradingService
    participant Exchange as BinanceExchangeService
    participant Resilience as Polly Pipeline
    participant API as Binance API
    participant Account as TradingAccount
    participant Notify as NotificationService
    participant Risk as PortfolioRiskManager

    Trading->>Trading: PlaceBuyOrder(options)
    Trading->>Trading: CalculatePositionSize() [if enabled]
    Trading->>Trading: Create BuyOrder request

    Trading->>Account: PlaceOrder(orderRequest)
    Account->>Exchange: PlaceOrder(order)

    Exchange->>Resilience: Execute with retry policy
    Resilience->>API: POST order

    alt Success
        API-->>Resilience: Order filled
        Resilience-->>Exchange: OrderDetails
        Exchange-->>Account: OrderDetails

        Account->>Account: AddTradingPair() or UpdatePosition()
        Account-->>Trading: OrderDetails

        Trading->>Trading: LogOrder(orderDetails)
        Trading->>Notify: NotifyAsync("Buy order filled")
        Trading->>Risk: RegisterPosition(pair, risk)
    else Failure
        API-->>Resilience: Error
        Resilience->>Resilience: Retry (max 1 for orders)

        alt Circuit breaker open
            Resilience-->>Exchange: CircuitBreakerException
        end
    end
```

**Resilience Patterns:**
- **Read operations**: 3 retries, 30s timeout, circuit breaker
- **Order operations**: 1 retry only (critical), 15s timeout, stricter circuit breaker

---

## 4. Data Flow Diagram

### Complete Data Flow

```mermaid
flowchart TB
    subgraph "External Data Sources"
        TV[("TradingView<br/>Scanner API")]
        BINANCE[("Binance<br/>WebSocket/REST")]
    end

    subgraph "Data Ingestion"
        TVTASK["TradingViewCryptoSignalPollingTimedTask<br/>(7s interval)"]
        WSSVC["BinanceWebSocketService<br/>(Real-time)"]
        MONITOR["BinanceTickersMonitorTimedTask<br/>(Health check)"]
    end

    subgraph "Data Storage"
        SIGNALS[("Signal History<br/>ConcurrentDictionary")]
        TICKERS[("Ticker Cache<br/>ConcurrentDictionary")]
        ACCOUNT[("Account State<br/>JSON file")]
        ORDERS[("Order History<br/>BoundedConcurrentStack")]
    end

    subgraph "Processing Engine"
        SIGRULES["SignalRuleProcessorService<br/>(3s interval)"]
        TRDRULES["TradingRuleProcessorService<br/>(3s interval)"]
        RULESVC["RulesService<br/>(Specification Pattern)"]
    end

    subgraph "Execution"
        TRADING["TradingService<br/>(Facade)"]
        BUYORCH["BuyOrchestrator"]
        SELLORCH["SellOrchestrator"]
        SWAPORCH["SwapOrchestrator"]
        TRAILING["TrailingOrderManager"]
    end

    subgraph "Output"
        EXCH["BinanceExchangeService"]
        NOTIFY["NotificationService"]
        WEB["WebService / TradingHub"]
    end

    TV -->|"JSON signals"| TVTASK
    BINANCE -->|"WebSocket tickers"| WSSVC
    BINANCE -->|"REST fallback"| WSSVC

    TVTASK -->|"Store"| SIGNALS
    WSSVC -->|"Update"| TICKERS
    MONITOR -->|"Check health"| WSSVC

    SIGNALS -->|"Read"| SIGRULES
    TICKERS -->|"Read"| SIGRULES
    TICKERS -->|"Read"| TRDRULES
    ACCOUNT -->|"Read"| TRDRULES

    SIGRULES -->|"Evaluate"| RULESVC
    TRDRULES -->|"Evaluate"| RULESVC

    SIGRULES -->|"Buy signal"| TRADING
    TRDRULES -->|"Sell/DCA signal"| TRADING

    TRADING --> BUYORCH
    TRADING --> SELLORCH
    TRADING --> SWAPORCH
    TRADING --> TRAILING

    BUYORCH -->|"Place order"| EXCH
    SELLORCH -->|"Place order"| EXCH

    EXCH -->|"Execute"| BINANCE
    EXCH -->|"Update"| ACCOUNT
    EXCH -->|"Log"| ORDERS

    TRADING -->|"Alert"| NOTIFY
    TRADING -->|"Push update"| WEB
```

### Data Structures

| Component | Storage Type | Purpose | Retention |
|-----------|-------------|---------|-----------|
| Signal History | ConcurrentDictionary | Historical signal snapshots for change calculation | SignalPeriod + 5 minutes |
| Ticker Cache | ConcurrentDictionary | Real-time price data | Until WebSocket disconnect |
| Account State | JSON file | Trading pairs, balances, metadata | Persistent |
| Order History | BoundedConcurrentStack | Recent orders for timeout checks | MaxOrderHistorySize (configurable) |
| Trailing State | ConcurrentDictionary | Active trailing buy/sell operations | Until executed/cancelled |

---

## 5. Deployment View

### Single-Host Deployment

```mermaid
C4Deployment
    title Deployment Diagram - Single Host

    Deployment_Node(server, "Application Server", "Windows/Linux") {
        Deployment_Node(runtime, ".NET Runtime", ".NET 8.0+") {
            Container(app, "IntelliTrader", "Console Application", "Main trading bot process")
            Container(web, "Kestrel", "Web Server", "Dashboard on port 7000")
        }

        Deployment_Node(storage, "File System") {
            ContainerDb(config, "config/", "JSON Files", "Configuration")
            ContainerDb(data, "data/", "JSON Files", "Account state")
            ContainerDb(logs, "log/", "Text Files", "Application logs")
        }
    }

    Deployment_Node(network, "External Network") {
        System_Ext(binance, "Binance API", "api.binance.com")
        System_Ext(tv, "TradingView", "scanner.tradingview.com")
        System_Ext(telegram, "Telegram", "api.telegram.org")
    }

    Rel(app, config, "Reads")
    Rel(app, data, "Reads/Writes")
    Rel(app, logs, "Writes")
    Rel(app, web, "Hosts")
    Rel(app, binance, "HTTPS/WSS")
    Rel(app, tv, "HTTPS")
    Rel(app, telegram, "HTTPS")
```

### Runtime Topology

```mermaid
flowchart TB
    subgraph "IntelliTrader Process"
        subgraph "Main Thread"
            MAIN[Program.Main]
            CORE[CoreService]
        end

        subgraph "Timed Tasks (Thread Pool)"
            TVPOLL["TradingView Polling<br/>(7s)"]
            TICKER["Ticker Monitor<br/>(health)"]
            HEALTH["Health Check<br/>(180s)"]
        end

        subgraph "Background Services (Hosted)"
            SIGRULE["SignalRuleProcessor<br/>(3s)"]
            TRDRULE["TradingRuleProcessor<br/>(3s)"]
            ORDEREXEC["OrderExecution<br/>(1s)"]
            SIGNALR["SignalRBroadcaster"]
        end

        subgraph "Web Host (Kestrel)"
            KESTREL["Kestrel :7000"]
            MVC["MVC Controllers"]
            HUB["SignalR Hub"]
            API["Minimal API"]
        end

        subgraph "WebSocket Client"
            BINWS["Binance WebSocket"]
        end
    end

    MAIN --> CORE
    CORE --> TVPOLL
    CORE --> TICKER
    CORE --> HEALTH
    CORE --> KESTREL

    KESTREL --> MVC
    KESTREL --> HUB
    KESTREL --> API
```

### Environment Configuration

| Environment | VirtualTrading | Exchange Keys | Web Port | Use Case |
|-------------|---------------|---------------|----------|----------|
| Development | `true` | Not required | 7000 | Strategy testing |
| Backtesting | `true` + Replay | Not required | 7000 | Historical analysis |
| Production | `false` | Encrypted file | 7000 | Live trading |

### Configuration Files

```
IntelliTrader/config/
|-- core.json           # Instance settings, health checks, password
|-- trading.json        # Exchange, market, buy/sell parameters, risk management
|-- signals.json        # TradingView signal definitions
|-- rules.json          # Signal rules (buy triggers) and trading rules (sell/DCA)
|-- web.json            # Web interface port and settings
|-- notification.json   # Telegram bot configuration
|-- exchange.json       # API keys path, rate limiting
|-- logging.json        # Log levels and output
|-- backtesting.json    # Replay settings
```

### Docker Containerization (Optional)

```dockerfile
# Example Dockerfile structure
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet build -c Release

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Mount points for persistent data
VOLUME ["/app/config", "/app/data", "/app/log"]

ENTRYPOINT ["dotnet", "IntelliTrader.dll"]
```

### Network Requirements

| Direction | Protocol | Port | Destination | Purpose |
|-----------|----------|------|-------------|---------|
| Outbound | HTTPS | 443 | api.binance.com | REST API calls |
| Outbound | WSS | 443 | stream.binance.com | Real-time tickers |
| Outbound | HTTPS | 443 | scanner.tradingview.com | Signal polling |
| Outbound | HTTPS | 443 | api.telegram.org | Notifications |
| Inbound | HTTP | 7000 | localhost | Dashboard access |

### Security Considerations

1. **API Key Protection**: Binance API keys stored in encrypted file using `CryptoUtility.SaveUnprotectedStringsToFile`
2. **Web Authentication**: Cookie-based authentication with BCrypt password hashing (legacy MD5 supported for migration)
3. **HTTPS**: External API calls use HTTPS; local dashboard accessible via HTTP (should be behind reverse proxy for production)
4. **Rate Limiting**: Binance API rate limiting configured via `RateGate`

---

## Appendix: Key Timing Intervals

| Component | Interval | Purpose |
|-----------|----------|---------|
| TradingView Signal Polling | 7 seconds | Acquire market signals |
| Signal Rules Processing | 3 seconds | Evaluate buy conditions |
| Trading Rules Processing | 3 seconds | Evaluate sell/DCA conditions |
| Order Execution | 1 second | Process pending orders |
| Health Check | 180 seconds | Service health monitoring |
| Account Refresh | 360 seconds | Sync account state |
| Ticker Staleness Threshold | 60 seconds | Trigger WebSocket reconnect |
