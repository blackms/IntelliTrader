# IntelliTrader Data Structures Documentation

This document describes configuration schemas, in-memory state models, and environment variables used by the IntelliTrader trading bot.

---

## Table of Contents

1. [Configuration Schemas](#configuration-schemas)
   - [core.json](#corejson)
   - [trading.json](#tradingjson)
   - [signals.json](#signalsjson)
   - [rules.json](#rulesjson)
   - [web.json](#webjson)
   - [notification.json](#notificationjson)
   - [exchange.json](#exchangejson)
2. [In-Memory State Models](#in-memory-state-models)
   - [Trading Pairs State](#trading-pairs-state)
   - [Order History Structure](#order-history-structure)
   - [Signal Cache Structure](#signal-cache-structure)
   - [Health Check State](#health-check-state)
3. [Environment Variables Catalog](#environment-variables-catalog)

---

## Configuration Schemas

All configuration files are located in `IntelliTrader/config/` and support hot-reload.

### core.json

Core application settings including authentication and health checks.

```json
{
  "Core": {
    "DebugMode": true,
    "PasswordProtected": true,
    "Password": "$2a$12$...",
    "InstanceName": "Main",
    "TimezoneOffset": 1,
    "HealthCheckEnabled": true,
    "HealthCheckInterval": 180,
    "HealthCheckSuspendTradingTimeout": 900,
    "HealthCheckFailuresToRestartServices": 3
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DebugMode` | `bool` | `false` | Enable debug logging and features |
| `PasswordProtected` | `bool` | `true` | Require password for web access |
| `Password` | `string` | - | BCrypt hash (or legacy MD5) for authentication |
| `InstanceName` | `string` | `"Main"` | Instance identifier for multi-instance setups |
| `TimezoneOffset` | `double` | `0` | Hours offset from UTC for display |
| `HealthCheckEnabled` | `bool` | `true` | Enable health monitoring |
| `HealthCheckInterval` | `double` | `180` | Seconds between health checks |
| `HealthCheckSuspendTradingTimeout` | `double` | `900` | Seconds of failures before suspending trading |
| `HealthCheckFailuresToRestartServices` | `int` | `3` | Consecutive failures before service restart |

### trading.json

Trading configuration including buy/sell parameters, DCA levels, and risk management.

```json
{
  "Trading": {
    "Enabled": true,
    "Market": "BTC",
    "Exchange": "Binance",
    "MaxPairs": 10,
    "MinCost": 0.000999,
    "ExcludedPairs": ["BNBBTC"],

    "BuyEnabled": true,
    "BuyType": "Market",
    "BuyMaxCost": 0.0012,
    "BuyMultiplier": 1,
    "BuyMinBalance": 0,
    "BuySamePairTimeout": 900,
    "BuyTrailing": -0.15,
    "BuyTrailingStopMargin": 0.05,
    "BuyTrailingStopAction": "Buy",

    "BuyDCAEnabled": true,
    "BuyDCAMultiplier": 1,
    "BuyDCAMinBalance": 0,
    "BuyDCASamePairTimeout": 4200,
    "BuyDCATrailing": -1.2,
    "BuyDCATrailingStopMargin": 0.3,
    "BuyDCATrailingStopAction": "Cancel",

    "SellEnabled": true,
    "SellType": "Market",
    "SellMargin": 2,
    "SellTrailing": 0.7,
    "SellTrailingStopMargin": 1.7,
    "SellTrailingStopAction": "Sell",

    "SellStopLossEnabled": false,
    "SellStopLossAfterDCA": true,
    "SellStopLossMinAge": 3,
    "SellStopLossMargin": -20,

    "StopLossInternal": {
      "Type": "ATR",
      "ATRPeriod": 14,
      "ATRMultiplier": 3.0,
      "MinimumPercent": 2.0,
      "RangingMultiplier": 2.5,
      "TrendingMultiplier": 3.5,
      "AutoAdjustMultiplier": true
    },

    "SellDCAMargin": 1.5,
    "SellDCATrailing": 0.5,
    "SellDCATrailingStopMargin": 1.25,
    "SellDCATrailingStopAction": "Sell",

    "RepeatLastDCALevel": false,
    "DCALevels": [
      { "Margin": -1.5, "BuyTrailing": -0.6, "BuyTrailingStopMargin": 0.2 },
      { "Margin": -2.5 },
      { "Margin": -4.5 },
      { "Margin": -6.5 }
    ],

    "TradingCheckInterval": 1,
    "AccountRefreshInterval": 360,
    "AccountInitialBalance": 0.12,
    "AccountInitialBalanceDate": "2018-04-08T00:00:00+00:00",
    "AccountFilePath": "data/exchange-account.json",

    "VirtualTrading": true,
    "VirtualAccountInitialBalance": 0.12,
    "VirtualAccountFilePath": "data/virtual-account.json",

    "PositionSizing": {
      "Enabled": false,
      "Type": "Kelly",
      "RiskPercent": 1.0,
      "KellyFraction": 0.5,
      "MaxPositionPercent": 10.0,
      "DefaultStopLossPercent": 5.0,
      "WinRate": 55.0,
      "AverageWinLoss": 1.5,
      "MinTradesForStats": 30
    },

    "RiskManagement": {
      "Enabled": true,
      "MaxPortfolioHeat": 6.0,
      "MaxDrawdownPercent": 10.0,
      "DailyLossLimitPercent": 5.0,
      "CircuitBreakerEnabled": true,
      "DefaultPositionRiskPercent": 1.0
    }
  }
}
```

#### Core Trading Settings

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Master trading enable/disable |
| `Market` | `string` | `"BTC"` | Base market currency |
| `Exchange` | `string` | `"Binance"` | Exchange name |
| `MaxPairs` | `int` | `10` | Maximum concurrent trading pairs |
| `MinCost` | `decimal` | `0.000999` | Minimum order cost threshold |
| `ExcludedPairs` | `string[]` | `[]` | Pairs to exclude from trading |

#### Buy Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BuyEnabled` | `bool` | `true` | Enable buying |
| `BuyType` | `string` | `"Market"` | Order type: `Market` or `Limit` |
| `BuyMaxCost` | `decimal` | - | Maximum cost per buy order |
| `BuyMultiplier` | `decimal` | `1` | Cost multiplier |
| `BuyMinBalance` | `decimal` | `0` | Minimum balance to maintain |
| `BuySamePairTimeout` | `int` | `900` | Seconds before buying same pair again |
| `BuyTrailing` | `decimal` | `-0.15` | Trailing percentage to trigger buy |
| `BuyTrailingStopMargin` | `decimal` | `0.05` | Stop margin for trailing |
| `BuyTrailingStopAction` | `string` | `"Buy"` | Action: `Buy` or `Cancel` |

#### DCA (Dollar Cost Averaging) Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BuyDCAEnabled` | `bool` | `true` | Enable DCA buying |
| `BuyDCAMultiplier` | `decimal` | `1` | DCA cost multiplier |
| `BuyDCASamePairTimeout` | `int` | `4200` | Seconds between DCA buys |
| `BuyDCATrailing` | `decimal` | `-1.2` | DCA trailing percentage |
| `RepeatLastDCALevel` | `bool` | `false` | Repeat last level indefinitely |
| `DCALevels` | `array` | - | Array of DCA level configurations |

**DCA Level Structure:**
```json
{
  "Margin": -1.5,              // Required: margin to trigger this level
  "BuyTrailing": -0.6,         // Optional: override trailing
  "BuyTrailingStopMargin": 0.2 // Optional: override stop margin
}
```

#### Sell Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SellEnabled` | `bool` | `true` | Enable selling |
| `SellType` | `string` | `"Market"` | Order type: `Market` or `Limit` |
| `SellMargin` | `decimal` | `2` | Target profit margin percentage |
| `SellTrailing` | `decimal` | `0.7` | Trailing percentage for sell |
| `SellTrailingStopMargin` | `decimal` | `1.7` | Stop margin for trailing sell |
| `SellTrailingStopAction` | `string` | `"Sell"` | Action when stop hit |
| `SellDCAMargin` | `decimal` | `1.5` | Target margin for DCA positions |

#### Stop Loss Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SellStopLossEnabled` | `bool` | `false` | Enable stop loss |
| `SellStopLossAfterDCA` | `bool` | `true` | Only after DCA |
| `SellStopLossMinAge` | `double` | `3` | Minimum age (hours) |
| `SellStopLossMargin` | `decimal` | `-20` | Stop loss margin |

**Internal Stop Loss (ATR-based):**
```json
{
  "Type": "ATR",                    // ATR, Fixed, Trailing, TimeDecay
  "ATRPeriod": 14,                  // ATR calculation period
  "ATRMultiplier": 3.0,             // Multiplier for ATR
  "MinimumPercent": 2.0,            // Minimum stop loss percentage
  "RangingMultiplier": 2.5,         // Multiplier in ranging markets
  "TrendingMultiplier": 3.5,        // Multiplier in trending markets
  "AutoAdjustMultiplier": true      // Auto-adjust based on market conditions
}
```

#### Position Sizing Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `false` | Enable position sizing |
| `Type` | `string` | `"Kelly"` | `Kelly`, `Fixed`, `Percent` |
| `RiskPercent` | `decimal` | `1.0` | Risk per trade (%) |
| `KellyFraction` | `decimal` | `0.5` | Kelly criterion fraction |
| `MaxPositionPercent` | `decimal` | `10.0` | Max position size (%) |
| `WinRate` | `decimal` | `55.0` | Historical win rate (%) |
| `AverageWinLoss` | `decimal` | `1.5` | Average win/loss ratio |

#### Risk Management Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable risk management |
| `MaxPortfolioHeat` | `decimal` | `6.0` | Max total risk (%) |
| `MaxDrawdownPercent` | `decimal` | `10.0` | Max drawdown before circuit breaker |
| `DailyLossLimitPercent` | `decimal` | `5.0` | Daily loss limit (%) |
| `CircuitBreakerEnabled` | `bool` | `true` | Auto-suspend on limit breach |
| `DefaultPositionRiskPercent` | `decimal` | `1.0` | Default risk per position |

### signals.json

Signal source configuration for TradingView and other providers.

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
          "VolatilityPeriod": "Week",
          "RequestUrl": "https://scanner.tradingview.com/crypto/scan",
          "RequestData": "{...}"
        }
      }
    ]
  }
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Enabled` | `bool` | Enable signal processing |
| `GlobalRatingSignals` | `string[]` | Signals to include in global rating calculation |
| `Definitions` | `array` | Signal source definitions |

**Signal Definition:**

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Unique signal identifier |
| `Receiver` | `string` | Receiver class name |
| `Configuration` | `object` | Receiver-specific configuration |

**TradingView Configuration:**

| Property | Type | Description |
|----------|------|-------------|
| `PollingInterval` | `int` | Seconds between polls |
| `SignalPeriod` | `int` | Timeframe in minutes (5, 15, 60, 240) |
| `VolatilityPeriod` | `string` | `Day`, `Week`, `Month` |
| `RequestUrl` | `string` | TradingView API endpoint |
| `RequestData` | `string` | JSON request template with placeholders |

### rules.json

Rule engine configuration for signal-based and trading rules.

```json
{
  "Rules": {
    "Modules": [
      {
        "Module": "Signals",
        "Configuration": {
          "ProcessingMode": "AllMatches",
          "CheckInterval": 3
        },
        "Entries": [...]
      },
      {
        "Module": "Trading",
        "Configuration": {
          "ProcessingMode": "AllMatches",
          "CheckInterval": 3
        },
        "Entries": [...]
      }
    ]
  }
}
```

#### Signal Rules (Buy Triggers)

```json
{
  "Enabled": true,
  "Name": "Default",
  "Action": "Buy",
  "Modifiers": {
    "CostMultiplier": 1
  },
  "Conditions": [
    {
      "Signal": "TV-15mins",
      "MinVolume": 150000,
      "MinGlobalRating": 0.1,
      "MinRating": 0.35
    }
  ],
  "Trailing": {
    "Enabled": true,
    "MinDuration": 25,
    "MaxDuration": 240,
    "StartConditions": [...]
  }
}
```

**Rule Condition Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Signal` | `string` | Signal name to check |
| `MinVolume` | `long?` | Minimum volume |
| `MaxVolume` | `long?` | Maximum volume |
| `MinRating` | `double?` | Minimum signal rating |
| `MaxRating` | `double?` | Maximum signal rating |
| `MinGlobalRating` | `double?` | Minimum global rating |
| `MaxGlobalRating` | `double?` | Maximum global rating |
| `MinPriceChange` | `decimal?` | Minimum price change % |
| `MaxPriceChange` | `decimal?` | Maximum price change % |

#### Trading Rules (Market Condition Modifiers)

```json
{
  "Enabled": true,
  "Name": "Bear",
  "Modifiers": {
    "BuyEnabled": false,
    "BuyDCAEnabled": false,
    "SellMargin": 1.3,
    "SellTrailing": 0.5,
    "SellStopLossEnabled": true
  },
  "Conditions": [
    {
      "MinGlobalRating": -0.2,
      "MaxGlobalRating": -0.1
    }
  ]
}
```

**Trading Rule Conditions (Pair-Specific):**

| Property | Type | Description |
|----------|------|-------------|
| `MinAge` | `double?` | Minimum position age (hours) |
| `MaxAge` | `double?` | Maximum position age |
| `MinMargin` | `decimal?` | Minimum current margin |
| `MaxMargin` | `decimal?` | Maximum current margin |
| `MinDCALevel` | `int?` | Minimum DCA level |
| `MaxDCALevel` | `int?` | Maximum DCA level |

### web.json

Web interface configuration.

```json
{
  "Web": {
    "Enabled": true,
    "DebugMode": true,
    "Port": 7000
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | Enable web interface |
| `DebugMode` | `bool` | `false` | Enable debug features |
| `Port` | `int` | `7000` | HTTP listening port |

### notification.json

Telegram notification settings.

```json
{
  "Notification": {
    "Enabled": false,
    "TelegramEnabled": true,
    "TelegramBotToken": "",
    "TelegramChatId": 0,
    "TelegramAlertsEnabled": true
  }
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Enabled` | `bool` | Master notification enable |
| `TelegramEnabled` | `bool` | Enable Telegram notifications |
| `TelegramBotToken` | `string` | Bot API token from BotFather |
| `TelegramChatId` | `long` | Chat ID for notifications |
| `TelegramAlertsEnabled` | `bool` | Enable alert messages |

### exchange.json

Exchange connection settings.

```json
{
  "Exchange": {
    "KeysPath": "keys.bin",
    "RateLimitOccurences": 40,
    "RateLimitTimeframe": 10
  }
}
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `KeysPath` | `string` | `"keys.bin"` | Encrypted API keys file path |
| `RateLimitOccurences` | `int` | `40` | Max requests per timeframe |
| `RateLimitTimeframe` | `int` | `10` | Timeframe in seconds |

---

## In-Memory State Models

### Trading Pairs State

The `ITradingPair` interface represents an active trading position.

```csharp
public interface ITradingPair
{
    string Pair { get; }                    // e.g., "BTCUSDT"
    string FormattedName { get; }           // e.g., "BTC/USDT"
    int DCALevel { get; }                   // Current DCA level (0 = initial buy)
    List<string> OrderIds { get; }          // All order IDs for this position
    List<DateTimeOffset> OrderDates { get; } // Order timestamps
    decimal TotalAmount { get; }            // Total held amount
    decimal AveragePricePaid { get; }       // Weighted average buy price
    decimal FeesPairCurrency { get; }       // Fees in pair currency
    decimal FeesMarketCurrency { get; }     // Fees in market currency
    decimal AverageCostPaid { get; }        // Total cost including fees
    decimal CurrentCost { get; }            // Current value
    decimal CurrentPrice { get; }           // Live price
    decimal CurrentMargin { get; }          // Current profit/loss %
    double CurrentAge { get; }              // Hours since first buy
    double LastBuyAge { get; }              // Hours since last buy
    OrderMetadata Metadata { get; }         // Additional metadata
}
```

**OrderMetadata:**
```csharp
public class OrderMetadata
{
    List<string>? TradingRules { get; set; }    // Active trading rules
    string? SignalRule { get; set; }            // Signal rule that triggered buy
    List<string>? Signals { get; set; }         // Signal sources
    double? BoughtRating { get; set; }          // Rating at buy time
    double? CurrentRating { get; set; }         // Current rating
    double? BoughtGlobalRating { get; set; }    // Global rating at buy
    double? CurrentGlobalRating { get; set; }   // Current global rating
    decimal? LastBuyMargin { get; set; }        // Margin at last DCA
    int? AdditionalDCALevels { get; set; }      // Extra DCA levels executed
    decimal? AdditionalCosts { get; set; }      // Additional costs incurred
    string? SwapPair { get; set; }              // Target pair for swap
}
```

### Order History Structure

The `IOrderDetails` interface represents a completed order.

```csharp
public interface IOrderDetails
{
    OrderSide Side { get; }           // Buy or Sell
    OrderResult Result { get; }       // Order result status
    DateTimeOffset Date { get; }      // Order execution time
    string OrderId { get; }           // Exchange order ID
    string Pair { get; }              // Trading pair
    string Message { get; }           // Status message
    decimal Amount { get; }           // Requested amount
    decimal AmountFilled { get; }     // Actually filled amount
    decimal Price { get; }            // Requested price
    decimal AveragePrice { get; }     // Actual average fill price
    decimal Fees { get; }             // Fees charged
    string FeesCurrency { get; }      // Fee currency (e.g., "BNB")
    decimal AverageCost { get; }      // Total cost
    OrderMetadata Metadata { get; }   // Additional metadata
}
```

**OrderSide Enum:**
- `Buy`
- `Sell`

**OrderResult Enum:**
- `Filled`
- `Cancelled`
- `Pending`
- `PartiallyFilled`
- `Error`

### Trade Result Structure

The `TradeResult` class represents a completed trade cycle (buy to sell).

```csharp
public class TradeResult
{
    bool IsSuccessful { get; set; }             // Trade completed successfully
    bool IsSwap { get; set; }                   // Was this a swap operation
    string Pair { get; set; }                   // Trading pair
    decimal Amount { get; set; }                // Amount traded
    List<DateTimeOffset> OrderDates { get; set; } // All order dates
    decimal AveragePricePaid { get; set; }      // Average buy price
    decimal FeesPairCurrency { get; set; }      // Fees in pair currency
    decimal FeesMarketCurrency { get; set; }    // Fees in market currency
    decimal AverageCost { get; }                // Calculated: price * (amount + fees) + market fees
    DateTimeOffset SellDate { get; set; }       // When sold
    decimal SellPrice { get; set; }             // Sell price
    decimal SellCost { get; }                   // Calculated: SellPrice * Amount
    decimal BalanceDifference { get; set; }     // Balance change
    decimal Profit { get; set; }                // Profit/loss amount
    OrderMetadata Metadata { get; set; }        // Trade metadata
}
```

### Signal Cache Structure

The `ISignal` interface represents a cached signal value.

```csharp
public interface ISignal
{
    string Name { get; }              // Signal source name
    string Pair { get; }              // Trading pair
    long? Volume { get; }             // Current volume
    double? VolumeChange { get; set; } // Volume change %
    decimal? Price { get; }           // Current price
    decimal? PriceChange { get; }     // Price change %
    double? Rating { get; }           // Signal rating (-1 to 1)
    double? RatingChange { get; }     // Rating change
    double? Volatility { get; }       // Volatility metric
}
```

### Health Check State

```csharp
public interface IHealthCheck
{
    string Name { get; }              // Check name (e.g., "ExchangeConnection")
    string Message { get; }           // Status message
    DateTimeOffset LastUpdated { get; } // Last check time
    bool Failed { get; }              // Check failed
}
```

---

## Environment Variables Catalog

IntelliTrader primarily uses JSON configuration files. The following table documents any environment variables that may affect behavior:

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `ASPNETCORE_ENVIRONMENT` | .NET environment (Development/Production) | `Production` | No |
| `ASPNETCORE_URLS` | Override web server URLs | `http://*:7000` | No |
| `DOTNET_RUNNING_IN_CONTAINER` | Docker container detection | - | No |

**Note:** API keys are stored in encrypted `keys.bin` file, not environment variables. To set up keys:

```bash
dotnet IntelliTrader.dll --encrypt --path keys.bin --publickey YOUR_API_KEY --privatekey YOUR_API_SECRET
```

### Docker Compose Environment Example

```yaml
services:
  intellitrader:
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - TZ=Europe/Rome
    volumes:
      - ./config:/app/config
      - ./data:/app/data
      - ./keys.bin:/app/keys.bin:ro
```

### Configuration File Locations

| File | Description |
|------|-------------|
| `config/core.json` | Core application settings |
| `config/trading.json` | Trading parameters |
| `config/signals.json` | Signal source definitions |
| `config/rules.json` | Trading and signal rules |
| `config/web.json` | Web interface settings |
| `config/notification.json` | Telegram notifications |
| `config/exchange.json` | Exchange connection settings |
| `config/logging.json` | Logging configuration |
| `config/caching.json` | Caching settings |
| `config/paths.json` | File path overrides |
| `data/virtual-account.json` | Virtual trading account state |
| `data/exchange-account.json` | Live trading account state |
| `keys.bin` | Encrypted API keys |
