# IntelliTrader API & Contracts Documentation

This document describes all REST API endpoints, SignalR real-time communication, and domain events in the IntelliTrader trading bot.

---

## Table of Contents

1. [REST API Endpoints](#rest-api-endpoints)
   - [Authentication](#authentication)
   - [Dashboard & Views](#dashboard--views)
   - [Trading Operations](#trading-operations)
   - [Data Endpoints](#data-endpoints)
   - [System Operations](#system-operations)
2. [SignalR Hubs](#signalr-hubs)
   - [TradingHub](#tradinghub)
   - [Client-to-Server Methods](#client-to-server-methods)
   - [Server-to-Client Events](#server-to-client-events)
3. [Domain Events](#domain-events)

---

## REST API Endpoints

All endpoints require authentication unless marked with `[AllowAnonymous]`. The default web interface runs on port **7000** (configurable in `web.json`).

### Authentication

| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| GET | `/Home/Login` | Display login page | - | HTML View |
| POST | `/Home/Login` | Authenticate user | `LoginViewModel { Password, RememberMe }` | Redirect to Dashboard or error |
| GET | `/Home/Logout` | Log out current user | - | Redirect to Login |
| POST | `/Home/GeneratePasswordHash` | Generate BCrypt hash for password | Form: `password` (min 8 chars) | `{ Hash, Instructions }` |
| GET | `/Home/PasswordStatus` | Check password hash type | - | `{ PasswordProtected, HashType, NeedsMigration, MigrationInstructions }` |

**GeneratePasswordHash Response Example:**
```json
{
  "Hash": "$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/X4/jJrZk.oB8C6.Hy",
  "Instructions": "Copy this hash to core.json 'Password' field and restart the service."
}
```

### Dashboard & Views

| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| GET | `/` | Redirect to Dashboard | - | HTML View |
| GET | `/Home/Index` | Redirect to Dashboard | - | HTML View |
| GET | `/Home/Dashboard` | Main dashboard view | - | `DashboardViewModel` HTML |
| GET | `/Home/Market` | Market overview | - | `MarketViewModel` HTML |
| GET | `/Home/Stats` | Trading statistics | - | `StatsViewModel` HTML |
| GET | `/Home/Trades/{id}` | Trades for specific date | `id`: DateTimeOffset | `TradesViewModel` HTML |
| GET | `/Home/Settings` | Settings page | - | `SettingsViewModel` HTML |
| GET | `/Home/Log` | View application logs | - | `LogViewModel` HTML (last 500 entries) |
| GET | `/Home/Help` | Help page | - | `HelpViewModel` HTML |

### Trading Operations

| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| POST | `/Home/Buy` | Execute manual buy order | Form: `BuyInputModel { Pair, Amount }` | `200 OK` or `400 BadRequest` |
| POST | `/Home/BuyDefault` | Buy with default max cost | Form: `BuyDefaultInputModel { Pair }` | `200 OK` or `400 BadRequest` |
| POST | `/Home/Sell` | Execute manual sell order | Form: `SellInputModel { Pair, Amount }` | `200 OK` or `400 BadRequest` |
| POST | `/Home/Swap` | Swap one pair for another | Form: `SwapInputModel { Pair, Swap }` | `200 OK` or `400 BadRequest` |
| POST | `/Home/Settings` | Update trading settings | Form: `SettingsViewModel` | Redirect to Settings |
| POST | `/Home/SaveConfig` | Save configuration changes | Form: `ConfigUpdateModel { Name, Definition }` | `200 OK` or `400 BadRequest` |

**Input Model Validation:**

```csharp
// BuyInputModel / SellInputModel
{
  "Pair": "BTCUSDT",     // Required, 2-20 uppercase alphanumeric
  "Amount": 0.001        // Required, range: 0.00000001 - 1,000,000,000
}

// BuyDefaultInputModel
{
  "Pair": "BTCUSDT"      // Required, 2-20 uppercase alphanumeric
}

// SwapInputModel
{
  "Pair": "BTCUSDT",     // Source pair - required
  "Swap": "ETHUSDT"      // Target pair - required
}

// ConfigUpdateModel
{
  "Name": "trading",     // One of: core, trading, signals, rules, web, notification
  "Definition": "{...}"  // Valid JSON string
}
```

### Data Endpoints

| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| GET | `/Home/Status` | Current trading status (DEPRECATED) | - | Status JSON |
| GET | `/Home/SignalNames` | Available signal names (DEPRECATED) | - | `string[]` |
| POST | `/Home/TradingPairs` | Active trading pairs (DEPRECATED) | - | Trading pairs JSON array |
| POST | `/Home/MarketPairs` | Market pairs with signals (DEPRECATED) | `signalsFilter`: `string[]` | Market pairs JSON array |

**Status Response Schema:**
```json
{
  "Balance": 0.12,
  "GlobalRating": "0.150",
  "TrailingBuys": ["ETHBTC"],
  "TrailingSells": ["XRPBTC"],
  "TrailingSignals": ["TV-15mins:BTCUSDT"],
  "TradingSuspended": false,
  "HealthChecks": [
    {
      "Name": "ExchangeConnection",
      "Message": "Connected",
      "LastUpdated": "2024-01-15T10:30:00Z",
      "Failed": false
    }
  ],
  "LogEntries": [...],
  "RiskManagement": {
    "PortfolioHeat": 2.5,
    "MaxPortfolioHeat": 6.0,
    "CurrentDrawdown": 1.2,
    "MaxDrawdownPercent": 10.0,
    "DailyProfitLoss": -0.5,
    "DailyLossLimitPercent": 5.0,
    "CircuitBreakerTriggered": false,
    "DailyLossLimitReached": false
  }
}
```

**TradingPairs Response Schema:**
```json
[
  {
    "Name": "ETHBTC",
    "DCA": 1,
    "TradingViewName": "BINANCE:ETHBTC",
    "Margin": "2.50",
    "Target": "2.00",
    "CurrentPrice": "0.06500000",
    "BoughtPrice": "0.06340000",
    "Cost": "0.00120000",
    "CurrentCost": "0.00123000",
    "Amount": "0.01886792",
    "OrderDates": ["2024-01-15 10:30:00"],
    "OrderIds": ["12345"],
    "Age": "2.50",
    "CurrentRating": "0.350",
    "BoughtRating": "0.400",
    "SignalRule": "Default",
    "SwapPair": null,
    "TradingRules": ["Bull"],
    "IsTrailingSell": false,
    "IsTrailingBuy": false,
    "LastBuyMargin": "-1.50",
    "Config": {...}
  }
]
```

### System Operations

| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| GET | `/Home/RefreshAccount` | Refresh account data from exchange | - | `200 OK` |
| GET | `/Home/RestartServices` | Restart all services | - | `200 OK` |

---

## SignalR Hubs

### TradingHub

The `TradingHub` provides real-time updates for trading activities. It is located at the `/trading` endpoint and requires authentication.

**Connection URL:** `wss://{host}:{port}/trading`

**Groups:**
- `TradingUpdates` - All general trading updates (joined automatically on connect)
- `Pair_{SYMBOL}` - Pair-specific updates (e.g., `Pair_BTCUSDT`)

### Client-to-Server Methods

| Method | Parameters | Description |
|--------|------------|-------------|
| `SubscribeToPair` | `pair: string` | Subscribe to updates for a specific trading pair |
| `UnsubscribeFromPair` | `pair: string` | Unsubscribe from a specific trading pair |
| `RequestStatus` | - | Request immediate status update |
| `RequestTradingPairs` | - | Request current trading pairs data |
| `RequestHealthStatus` | - | Request current health check status |

**Example (JavaScript):**
```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/trading")
    .build();

await connection.start();

// Subscribe to a specific pair
await connection.invoke("SubscribeToPair", "BTCUSDT");

// Request current status
await connection.invoke("RequestStatus");
```

### Server-to-Client Events

| Event | Payload | Description |
|-------|---------|-------------|
| `StatusUpdate` | Status object | Complete status update with balance, ratings, health checks |
| `PriceUpdate` | `{ Pair, Price, Timestamp }` | Price update for a subscribed pair |
| `TickerUpdate` | `{ Pair, BidPrice, AskPrice, LastPrice, Timestamp }` | Full ticker update |
| `TradeExecuted` | Order details object | Trade execution notification |
| `PositionChanged` | Position object | Position added, updated, or removed |
| `HealthStatus` | `{ HealthChecks[], TradingSuspended, Timestamp }` | Health check status update |
| `BalanceUpdate` | `{ Balance, Timestamp }` | Account balance change |
| `TrailingStatus` | `{ TrailingBuys[], TrailingSells[], TrailingSignals[], Timestamp }` | Trailing order status |
| `TradingPairsUpdate` | Trading pairs array | Full trading pairs update |

**StatusUpdate Payload:**
```json
{
  "Balance": 0.12,
  "GlobalRating": "0.150",
  "TrailingBuys": ["ETHBTC"],
  "TrailingSells": [],
  "TrailingSignals": [],
  "TradingSuspended": false,
  "HealthChecks": [...],
  "Timestamp": "2024-01-15T10:30:00Z"
}
```

**TradeExecuted Payload:**
```json
{
  "OrderId": "12345",
  "Pair": "BTCUSDT",
  "Side": "Buy",
  "Result": "Filled",
  "Amount": 0.001,
  "AmountFilled": 0.001,
  "Price": 42000.00,
  "AveragePrice": 42000.00,
  "AverageCost": 42.00,
  "Fees": 0.00042,
  "FeesCurrency": "BNB",
  "Date": "2024-01-15T10:30:00Z",
  "Timestamp": "2024-01-15T10:30:00Z"
}
```

**PositionChanged Payload:**
```json
{
  "Pair": "BTCUSDT",
  "FormattedName": "BTC/USDT",
  "DCALevel": 1,
  "TotalAmount": 0.001,
  "AveragePricePaid": 42000.00,
  "AverageCostPaid": 42.00,
  "CurrentCost": 43.50,
  "CurrentPrice": 43500.00,
  "CurrentMargin": 3.57,
  "CurrentAge": 2.5,
  "ChangeType": "Updated",
  "Timestamp": "2024-01-15T10:30:00Z"
}
```

---

## Domain Events

Domain events represent significant occurrences within the trading system. All events implement `IDomainEvent` and include common properties:

```csharp
interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
    string? CorrelationId { get; }
}
```

### OrderPlacedEvent

Raised when an order is placed on the exchange.

| Property | Type | Description |
|----------|------|-------------|
| `OrderId` | `string` | Exchange-assigned order ID |
| `Pair` | `string` | Trading pair (e.g., "BTCUSDT") |
| `Side` | `OrderSide` | `Buy` or `Sell` |
| `Amount` | `decimal` | Order quantity |
| `Price` | `decimal` | Order price |
| `OrderType` | `OrderType` | `Market`, `Limit`, `StopLoss`, `TakeProfit` |
| `IsManual` | `bool` | Whether this is a manual order |
| `SignalRule` | `string?` | Signal rule that triggered the order |

### OrderFilledEvent

Raised when an order is fully or partially filled.

| Property | Type | Description |
|----------|------|-------------|
| `OrderId` | `string` | Exchange-assigned order ID |
| `Pair` | `string` | Trading pair |
| `Side` | `OrderSide` | `Buy` or `Sell` |
| `FilledAmount` | `decimal` | Amount that was filled |
| `AveragePrice` | `decimal` | Average fill price |
| `Cost` | `decimal` | Total cost (FilledAmount * AveragePrice) |
| `Fees` | `decimal` | Fees paid |
| `IsPartialFill` | `bool` | Whether this is a partial fill |

### SignalReceivedEvent

Raised when a new trading signal is received.

| Property | Type | Description |
|----------|------|-------------|
| `SignalName` | `string` | Signal name (e.g., "TV-15mins") |
| `Pair` | `string` | Trading pair |
| `Rating` | `double` | Signal rating (-1 to 1) |
| `Source` | `string` | Signal source (e.g., "TradingView") |
| `PreviousRating` | `double?` | Previous rating value |
| `Price` | `decimal?` | Price at signal time |
| `PriceChange` | `decimal?` | Price change percentage |
| `Volume` | `long?` | Volume at signal time |
| `Volatility` | `double?` | Volatility metric |

### StopLossTriggeredEvent

Raised when a stop loss is triggered for a position.

| Property | Type | Description |
|----------|------|-------------|
| `Pair` | `string` | Trading pair |
| `TriggerPrice` | `decimal` | Price that triggered stop loss |
| `ExecutionPrice` | `decimal` | Actual execution price |
| `StopLossPrice` | `decimal` | Configured stop loss price |
| `MarginAtTrigger` | `decimal` | Margin at trigger time |
| `EstimatedLoss` | `decimal` | Estimated loss amount |
| `StopLossType` | `StopLossType` | `Fixed`, `Trailing`, `ATRBased`, `TimeDecay` |
| `DCALevel` | `int` | DCA level at trigger |

### RiskLimitBreachedEvent

Raised when a risk limit is breached.

| Property | Type | Description |
|----------|------|-------------|
| `LimitType` | `RiskLimitType` | Type of limit breached |
| `CurrentValue` | `decimal` | Current value that triggered breach |
| `MaxValue` | `decimal` | Maximum allowed value |
| `Description` | `string` | Breach description |
| `Severity` | `RiskSeverity` | `Warning`, `Critical`, `Emergency` |
| `Pair` | `string?` | Associated trading pair (if applicable) |

**RiskLimitType Values:** `PortfolioHeat`, `MaxPositions`, `PositionSize`, `DailyLoss`, `Drawdown`, `Exposure`, `CircuitBreaker`

### TradingSuspendedEvent

Raised when trading is suspended.

| Property | Type | Description |
|----------|------|-------------|
| `Reason` | `SuspensionReason` | Reason for suspension |
| `SuspendedBy` | `string` | Who/what suspended trading |
| `IsForced` | `bool` | Cannot be overridden |
| `OpenPositions` | `int` | Open positions at suspension |
| `PendingOrders` | `int` | Pending orders at suspension |
| `Details` | `string?` | Additional details |

**SuspensionReason Values:** `Manual`, `CircuitBreaker`, `DailyLossLimit`, `SystemError`, `ExchangeError`, `MaintenanceWindow`, `RiskLimitExceeded`

### TradingResumedEvent

Raised when trading is resumed.

| Property | Type | Description |
|----------|------|-------------|
| `ResumedBy` | `string` | Who/what resumed trading |
| `WasForced` | `bool` | Whether suspension was forced |
| `SuspensionDuration` | `TimeSpan` | Duration of suspension |
| `PreviousSuspensionReason` | `SuspensionReason?` | Reason for previous suspension |

### Event Handler Interface

```csharp
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
```

Events are dispatched via `IDomainEventDispatcher` which routes events to all registered handlers.
