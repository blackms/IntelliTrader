<div align="center">

# IntelliTrader

### Algorithmic Crypto Trading Bot

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Build](https://img.shields.io/badge/build-passing-brightgreen?style=for-the-badge&logo=github-actions&logoColor=white)](https://github.com/blackms/IntelliTrader/actions)
[![License](https://img.shields.io/badge/license-CC--BY--NC--SA--4.0-blue?style=for-the-badge)](LICENSE)
[![Binance](https://img.shields.io/badge/Binance-F0B90B?style=for-the-badge&logo=binance&logoColor=black)](https://binance.com)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED?style=for-the-badge&logo=docker&logoColor=white)](Dockerfile)

**Intelligent signal-driven cryptocurrency trading with DCA support, trailing orders, and real-time TradingView integration.**

[Getting Started](#-getting-started) · [Features](#-features) · [Architecture](#-architecture) · [Configuration](#-configuration) · [API](#-api-overview)

</div>

---

## Mission

> Democratize algorithmic trading by providing a powerful, configurable, and extensible trading bot that leverages real-time market signals to execute intelligent trading strategies—without requiring coding expertise.

---

## Tech Stack

<table>
<tr>
<td align="center" width="150">

**Backend**

</td>
<td align="center" width="150">

**Web**

</td>
<td align="center" width="150">

**Data**

</td>
<td align="center" width="150">

**DevOps**

</td>
<td align="center" width="150">

**Integrations**

</td>
</tr>
<tr>
<td align="center">

![.NET](https://img.shields.io/badge/-.NET_8-512BD4?style=flat-square&logo=dotnet)
![C#](https://img.shields.io/badge/-C%23_12-239120?style=flat-square&logo=csharp)
![Autofac](https://img.shields.io/badge/-Autofac-FF6F00?style=flat-square)

</td>
<td align="center">

![ASP.NET](https://img.shields.io/badge/-ASP.NET_Core-512BD4?style=flat-square&logo=dotnet)
![Razor](https://img.shields.io/badge/-Razor_Views-68217A?style=flat-square)
![Bootstrap](https://img.shields.io/badge/-Bootstrap-7952B3?style=flat-square&logo=bootstrap)

</td>
<td align="center">

![JSON](https://img.shields.io/badge/-JSON_Storage-000000?style=flat-square&logo=json)
![MessagePack](https://img.shields.io/badge/-MessagePack-FF6600?style=flat-square)
![Serilog](https://img.shields.io/badge/-Serilog-2C3E50?style=flat-square)

</td>
<td align="center">

![Docker](https://img.shields.io/badge/-Docker-2496ED?style=flat-square&logo=docker)
![xUnit](https://img.shields.io/badge/-xUnit-5E1F87?style=flat-square)
![GitHub Actions](https://img.shields.io/badge/-Actions-2088FF?style=flat-square&logo=github-actions)

</td>
<td align="center">

![Binance](https://img.shields.io/badge/-Binance-F0B90B?style=flat-square&logo=binance)
![TradingView](https://img.shields.io/badge/-TradingView-131722?style=flat-square&logo=tradingview)
![Telegram](https://img.shields.io/badge/-Telegram-26A5E4?style=flat-square&logo=telegram)

</td>
</tr>
</table>

---

## Features

| Category | Capabilities |
|----------|-------------|
| **Trading Modes** | Virtual paper trading · Live exchange trading · Seamless mode switching |
| **Order Types** | Market orders · Trailing buy/sell · Stop-loss protection · Pair swapping |
| **DCA Strategy** | 4+ configurable DCA levels · Custom multipliers · Margin-based triggers |
| **Signal Intelligence** | TradingView integration · Multi-timeframe analysis (5m/15m/60m/240m) · Global rating aggregation |
| **Rules Engine** | Signal-based buy rules · Trading rules for sell/DCA · Specification pattern · Hot-reload config |
| **Web Dashboard** | Real-time monitoring · Manual trading controls · Performance stats · Health checks |
| **Notifications** | Telegram alerts · Trade execution notifications · Health check warnings |
| **Backtesting** | Historical snapshot replay · High-speed simulation · Strategy validation |

---

## Architecture

### System Overview

```mermaid
flowchart TB
    subgraph External["External Services"]
        TV[TradingView Scanner]
        BN[Binance Exchange]
        TG[Telegram Bot]
    end

    subgraph Core["Core Layer"]
        CS[CoreService]
        LS[LoggingService]
        HS[HealthCheckService]
        NS[NotificationService]
    end

    subgraph Trading["Trading Layer"]
        TS[TradingService]
        RS[RulesService]
        SS[SignalsService]
        ES[ExchangeService]
    end

    subgraph Infrastructure["Infrastructure Layer"]
        BGS[BackgroundServices]
        TM[TrailingManager]
        PR[PositionRepository]
    end

    subgraph Web["Web Layer"]
        API[REST API]
        UI[Dashboard UI]
    end

    TV -->|Signals| SS
    BN -->|Tickers/Orders| ES
    TG <-->|Alerts| NS

    CS --> TS
    CS --> SS
    CS --> HS

    TS --> RS
    TS --> ES
    SS --> RS

    BGS --> TS
    BGS --> TM
    TM --> PR

    API --> TS
    API --> SS
    UI --> API
```

### Data Flow

```mermaid
sequenceDiagram
    participant TV as TradingView
    participant SS as SignalsService
    participant RS as RulesService
    participant TS as TradingService
    participant ES as ExchangeService
    participant BN as Binance

    loop Every 7s
        TV->>SS: Signal Data (ratings, volatility)
        SS->>SS: Aggregate & Calculate Global Rating
    end

    loop Every 3s
        RS->>SS: Get Pair Signals
        RS->>RS: Evaluate Buy Conditions
        alt Conditions Met
            RS->>TS: Initiate Buy
            TS->>TS: Apply Trailing Logic
            TS->>ES: Place Order
            ES->>BN: Execute Trade
            BN-->>ES: Order Confirmation
            ES-->>TS: Update Position
        end
    end

    loop Every 1s
        TS->>ES: Get Current Prices
        ES->>BN: Fetch Tickers
        BN-->>ES: Price Data
        TS->>RS: Evaluate Sell/DCA Rules
        alt Sell Triggered
            TS->>ES: Place Sell Order
            ES->>BN: Execute Trade
        end
    end
```

### Domain Model

```mermaid
classDiagram
    class Portfolio {
        +PortfolioId Id
        +string Name
        +string Market
        +PortfolioBalance Balance
        +int MaxPositions
        +RecordPositionOpened()
        +RecordPositionClosed()
        +SyncBalance()
    }

    class Position {
        +PositionId Id
        +TradingPair Pair
        +List~PositionEntry~ Entries
        +int DCALevel
        +bool IsClosed
        +Open()
        +AddDCAEntry()
        +Close()
        +CalculateMargin()
    }

    class PositionEntry {
        +OrderId OrderId
        +Price Price
        +Quantity Quantity
        +Money Fees
        +DateTimeOffset Timestamp
    }

    class TradingPair {
        +string Symbol
        +string QuoteCurrency
    }

    Portfolio "1" --> "*" Position : tracks
    Position "1" --> "*" PositionEntry : contains
    Position --> TradingPair : trades
```

### Deployment View

```mermaid
C4Deployment
    title Deployment Diagram

    Deployment_Node(user, "User Machine", "Windows/Linux/macOS") {
        Deployment_Node(runtime, ".NET 8 Runtime") {
            Container(bot, "IntelliTrader", "Console App", "Core trading engine")
            Container(web, "Web Dashboard", "ASP.NET Core", "Monitoring & Control")
        }
        Deployment_Node(storage, "Local Storage") {
            ContainerDb(config, "Config Files", "JSON", "Hot-reload configuration")
            ContainerDb(data, "Account Data", "JSON", "Positions & history")
            ContainerDb(logs, "Log Files", "Serilog", "Structured logs")
        }
    }

    Deployment_Node(cloud, "Cloud Services") {
        System_Ext(binance, "Binance API", "Exchange")
        System_Ext(tradingview, "TradingView", "Signals")
        System_Ext(telegram, "Telegram", "Notifications")
    }

    Rel(bot, config, "Reads")
    Rel(bot, data, "Reads/Writes")
    Rel(bot, logs, "Writes")
    Rel(bot, binance, "REST/WebSocket")
    Rel(bot, tradingview, "HTTP Polling")
    Rel(bot, telegram, "Bot API")
    Rel(web, bot, "In-Process")
```

---

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Binance account (for live trading)
- TradingView access (signals are public)

### Quick Start

```bash
# Clone the repository
git clone https://github.com/blackms/IntelliTrader.git
cd IntelliTrader

# Build the solution
dotnet build IntelliTrader.sln

# Run with virtual trading (default)
dotnet run --project IntelliTrader

# Access dashboard
open http://localhost:7000
```

### Live Trading Setup

```bash
# Encrypt your API keys
dotnet run --project IntelliTrader -- \
  --encrypt \
  --path keys.bin \
  --publickey YOUR_API_KEY \
  --privatekey YOUR_API_SECRET

# Update config/trading.json
# Set "VirtualTrading": false
```

---

## Configuration

All configuration files support **hot-reload** - changes apply immediately without restart.

| File | Purpose |
|------|---------|
| `config/core.json` | Instance name, health checks, timezone |
| `config/trading.json` | Market, buy/sell settings, DCA levels |
| `config/signals.json` | TradingView signal definitions |
| `config/rules.json` | Signal rules (buy) + Trading rules (sell/DCA) |
| `config/web.json` | Dashboard port and settings |
| `config/notification.json` | Telegram bot configuration |

### Example: DCA Configuration

```json
{
  "DCALevels": [
    { "Margin": -3.0, "BuyMultiplier": 1.0, "SellMargin": 1.5 },
    { "Margin": -6.0, "BuyMultiplier": 1.5, "SellMargin": 1.0 },
    { "Margin": -10.0, "BuyMultiplier": 2.0, "SellMargin": 0.5 },
    { "Margin": -15.0, "BuyMultiplier": 2.5, "SellMargin": 0.25 }
  ]
}
```

---

## API Overview

### Status Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/Status` | Real-time bot status, balance, health |
| `GET` | `/SignalNames` | Available signal sources |
| `POST` | `/TradingPairs` | Current positions with metrics |
| `POST` | `/MarketPairs` | Market data with signal ratings |

### Trading Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/Buy` | Manual buy order |
| `POST` | `/Sell` | Manual sell order |
| `POST` | `/Swap` | Swap pair positions |
| `GET` | `/RefreshAccount` | Sync with exchange |

### Management Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/Settings` | Update runtime settings |
| `POST` | `/SaveConfig` | Persist configuration |
| `GET` | `/RestartServices` | Restart all services |

---

## Roadmap

| Priority | Feature | Status |
|----------|---------|--------|
| **P1** | Multi-exchange support (Kraken, Coinbase) | Planned |
| **P1** | PostgreSQL persistence option | Planned |
| **P1** | Docker Compose deployment | In Progress |
| **P2** | GraphQL API layer | Planned |
| **P2** | Machine learning signal enhancement | Research |
| **P2** | Mobile companion app | Planned |
| **P3** | Kubernetes Helm chart | Backlog |
| **P3** | Social trading features | Backlog |

---

## Contributing

We welcome contributions! Please follow these guidelines:

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feat/amazing-feature`
3. **Commit** with conventional commits: `feat:`, `fix:`, `docs:`, `refactor:`
4. **Test** your changes: `dotnet test`
5. **Push** to your fork: `git push origin feat/amazing-feature`
6. **Open** a Pull Request

### Development Setup

```bash
# Install dependencies
dotnet restore

# Run tests
dotnet test --collect:"XPlat Code Coverage"

# Build release
dotnet build -c Release
```

---

## License

This project is licensed under the **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License**.

[![CC BY-NC-SA 4.0](https://licensebuttons.net/l/by-nc-sa/4.0/88x31.png)](https://creativecommons.org/licenses/by-nc-sa/4.0/)

**Disclaimer**: Trading cryptocurrencies carries significant risk. This software is provided "AS IS" without warranty. You are solely responsible for any trading decisions and potential losses.

---

<div align="center">

**[Documentation](docs/)** · **[Report Bug](https://github.com/blackms/IntelliTrader/issues)** · **[Request Feature](https://github.com/blackms/IntelliTrader/issues)**

Built with passion by the IntelliTrader community

</div>
