# IntelliTrader Operations Guide

This document covers deployment, operations, and incident response for the IntelliTrader cryptocurrency trading bot.

---

## Table of Contents

1. [Local Development Setup](#local-development-setup)
2. [Build and Test Commands](#build-and-test-commands)
3. [Docker](#docker)
4. [Deployment](#deployment)
5. [Rollback Strategy](#rollback-strategy)
6. [Runbooks - Common Incidents](#runbooks---common-incidents)
7. [Observability](#observability)

---

## Local Development Setup

### Prerequisites

- **.NET 9 SDK** - Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Git** - For version control
- **IDE** - Visual Studio 2022, VS Code with C# extension, or JetBrains Rider

Verify .NET installation:

```bash
dotnet --version
# Should output: 9.x.x
```

### Clone and Build

```bash
# Clone the repository
git clone <repository-url>
cd IntelliTrader

# Restore dependencies and build
dotnet build IntelliTrader.sln
```

### Configuration Setup

All configuration files are located in `IntelliTrader/config/`:

| File | Purpose |
|------|---------|
| `core.json` | Instance name, health checks, password, timezone |
| `trading.json` | Market settings, buy/sell parameters, DCA levels, virtual trading |
| `exchange.json` | API keys path, rate limiting |
| `signals.json` | TradingView signal definitions |
| `rules.json` | Signal rules (buy triggers) and trading rules (sell/DCA triggers) |
| `web.json` | Web dashboard port and settings |
| `logging.json` | Serilog configuration (console + rolling files) |
| `notification.json` | Telegram alerts configuration |
| `paths.json` | References to all other config files |

**Minimum Configuration for Development:**

1. **`config/core.json`** - Set password and instance name:
```json
{
  "Core": {
    "DebugMode": true,
    "PasswordProtected": true,
    "Password": "$2a$12$...",  // BCrypt hash
    "InstanceName": "Dev",
    "HealthCheckEnabled": true,
    "HealthCheckInterval": 180
  }
}
```

2. **`config/trading.json`** - Enable virtual trading for development:
```json
{
  "Trading": {
    "VirtualTrading": true,
    "VirtualAccountInitialBalance": 0.12,
    "Market": "BTC",
    "Exchange": "Binance"
  }
}
```

3. **`config/web.json`** - Set web dashboard port:
```json
{
  "Web": {
    "Enabled": true,
    "DebugMode": true,
    "Port": 7000
  }
}
```

### Running Locally

```bash
# From repository root
dotnet run --project IntelliTrader/IntelliTrader.csproj

# Or build first, then run the DLL
dotnet build IntelliTrader.sln
dotnet IntelliTrader/bin/Debug/net9.0/IntelliTrader.dll
```

The web dashboard will be available at `http://localhost:7000`.

**Important:** Always use Enter/Return key to exit the program gracefully to avoid corrupting account data files.

---

## Build and Test Commands

### Build Commands

```bash
# Build entire solution (Debug)
dotnet build IntelliTrader.sln

# Build for Release
dotnet build IntelliTrader.sln -c Release

# Build specific project
dotnet build IntelliTrader/IntelliTrader.csproj

# Clean build artifacts
dotnet clean IntelliTrader.sln
```

### Test Commands

The solution includes 10 test projects using xUnit, FluentAssertions, and Moq:

```bash
# Run all tests
dotnet test IntelliTrader.sln

# Run tests with detailed output
dotnet test IntelliTrader.sln --verbosity normal

# Run tests with coverage (requires coverlet.collector)
dotnet test IntelliTrader.sln --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/IntelliTrader.Core.Tests/IntelliTrader.Core.Tests.csproj

# Run tests matching a filter
dotnet test --filter "FullyQualifiedName~HealthCheck"

# Run tests in parallel (faster)
dotnet test IntelliTrader.sln --parallel
```

### Test Projects

| Project | Coverage Area |
|---------|--------------|
| `IntelliTrader.Core.Tests` | Core services, health checks, timed tasks |
| `IntelliTrader.Domain.Tests` | Domain models and business logic |
| `IntelliTrader.Trading.Tests` | Trading service, order execution |
| `IntelliTrader.Exchange.Tests` | Exchange integration, WebSocket |
| `IntelliTrader.Signals.Tests` | Signal processing and aggregation |
| `IntelliTrader.Web.Tests` | Web controllers, API endpoints |
| `IntelliTrader.Application.Tests` | Application bootstrapping |
| `IntelliTrader.Infrastructure.Tests` | Infrastructure services |
| `IntelliTrader.Rules.Tests` | Rule engine and conditions |
| `IntelliTrader.Backtesting.Tests` | Backtesting service |

---

## Docker

**Note:** The repository does not currently include a Dockerfile for IntelliTrader. The application is designed to run as a standalone .NET application.

To containerize the application, you would need to create a Dockerfile. Example structure:

```dockerfile
# Example Dockerfile (not currently in repo)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish IntelliTrader/IntelliTrader.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IntelliTrader.dll"]
```

---

## Deployment

### Environment Preparation

1. **Install .NET 9 Runtime** on the target server:
```bash
# Ubuntu/Debian
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --runtime aspnetcore --version 9.0.0
```

2. **Create deployment directory**:
```bash
mkdir -p /opt/intellitrader
mkdir -p /opt/intellitrader/config
mkdir -p /opt/intellitrader/data
mkdir -p /opt/intellitrader/log
```

3. **Set appropriate permissions**:
```bash
chown -R intellitrader:intellitrader /opt/intellitrader
chmod 750 /opt/intellitrader
```

### Configuration for Production

1. **Disable debug mode** in `config/core.json`:
```json
{
  "Core": {
    "DebugMode": false,
    "PasswordProtected": true
  }
}
```

2. **Disable web debug mode** in `config/web.json`:
```json
{
  "Web": {
    "DebugMode": false
  }
}
```

3. **Configure logging** for production in `config/logging.json`:
```json
{
  "Logging": {
    "MinimumLevel": {
      "Default": "Information"
    }
  }
}
```

4. **Set appropriate health check timeouts** in `config/core.json`:
```json
{
  "Core": {
    "HealthCheckEnabled": true,
    "HealthCheckInterval": 180,
    "HealthCheckSuspendTradingTimeout": 900,
    "HealthCheckFailuresToRestartServices": 3
  }
}
```

### API Key Encryption

For live trading, encrypt your Binance API keys:

```bash
# Using the built-in encryption utility
dotnet IntelliTrader.dll --encrypt --path=keys.bin --publickey=YOUR_API_KEY --privatekey=YOUR_API_SECRET
```

**Windows batch file** (`Encrypt-Keys.bat`):
```batch
dotnet bin\Debug\net9.0\IntelliTrader.dll --encrypt --path=keys.bin --publickey=public_key --privatekey=private_key
pause
```

**Important:** The encrypted file is only valid for:
- The current user
- The computer it was created on

Store the `keys.bin` file securely and reference it in `config/exchange.json`:
```json
{
  "Exchange": {
    "KeysPath": "keys.bin"
  }
}
```

### Starting the Service

**Manual start:**
```bash
cd /opt/intellitrader
dotnet IntelliTrader.dll
```

**As a systemd service** (create `/etc/systemd/system/intellitrader.service`):
```ini
[Unit]
Description=IntelliTrader Cryptocurrency Trading Bot
After=network.target

[Service]
Type=simple
User=intellitrader
WorkingDirectory=/opt/intellitrader
ExecStart=/usr/bin/dotnet /opt/intellitrader/IntelliTrader.dll
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
systemctl daemon-reload
systemctl enable intellitrader
systemctl start intellitrader
```

---

## Rollback Strategy

### Application Rollback

1. **Stop the service:**
```bash
systemctl stop intellitrader
# Or press Enter in console if running interactively
```

2. **Backup current version:**
```bash
mv /opt/intellitrader /opt/intellitrader.backup.$(date +%Y%m%d_%H%M%S)
```

3. **Restore previous version:**
```bash
# From backup
cp -r /opt/intellitrader.previous /opt/intellitrader

# Or redeploy from Git
git checkout <previous-tag>
dotnet publish -c Release -o /opt/intellitrader
```

4. **Restore configuration (if needed):**
```bash
cp -r /opt/intellitrader.backup.*/config/* /opt/intellitrader/config/
```

5. **Restart the service:**
```bash
systemctl start intellitrader
```

### Configuration Rollback

Configuration files support hot-reload. To rollback configuration:

1. **Backup current config:**
```bash
cp /opt/intellitrader/config/trading.json /opt/intellitrader/config/trading.json.backup
```

2. **Restore previous config:**
```bash
cp /opt/intellitrader/config/trading.json.previous /opt/intellitrader/config/trading.json
```

3. **Changes take effect automatically** due to file watcher (no restart needed for most configs).

### Data File Rollback

Account data is stored in JSON files:
- `data/exchange-account.json` - Live trading account state
- `data/virtual-account.json` - Virtual trading account state

To rollback account state:
```bash
systemctl stop intellitrader
cp data/virtual-account.json.backup data/virtual-account.json
systemctl start intellitrader
```

---

## Runbooks - Common Incidents

### Exchange Connection Failures

**Symptoms:**
- Health check shows `[-] Tickers updated` with stale timestamp
- Log entries showing connection errors to Binance

**Diagnosis:**
```bash
# Check logs for connection errors
grep -i "connection\|timeout\|429\|503" log/*-general.txt | tail -50

# Check health status via web API
curl http://localhost:7000/api/status
```

**Resolution:**

The system uses **Polly resilience pipelines** with automatic handling:

| Pipeline | Behavior |
|----------|----------|
| **ReadPipeline** | 3 retries with exponential backoff + jitter, 30s timeout, circuit breaker (50% failure ratio) |
| **OrderPipeline** | 1 retry only (to prevent duplicates), 15s timeout, stricter circuit breaker (30% failure ratio) |
| **WebSocketPipeline** | 5 reconnect attempts, automatic fallback to REST API |

**Circuit Breaker States:**
- **Closed**: Normal operation
- **Open**: Failing fast, not making requests (30-60s duration)
- **Half-Open**: Testing if service recovered

If circuit breaker is open for extended periods:

1. Check Binance status: https://www.binance.com/en/support/announcement
2. Verify network connectivity from server
3. Check if IP is rate-limited (look for 429 errors)
4. If rate-limited, wait for cooldown (Binance bans escalate: 2min -> 3 days)

**Manual intervention:**
```bash
# Restart service to reset circuit breakers
systemctl restart intellitrader
```

### Signal Service Unavailable

**Symptoms:**
- Health check shows `[-] TV Signals received` as stale
- No new signals being processed
- Buy rules not triggering

**Diagnosis:**
```bash
# Check TradingView signal errors
grep -i "signal\|tradingview" log/*-general.txt | tail -50
```

**Resolution:**

1. **Check TradingView service status** - External dependency
2. **Verify signal configuration** in `config/signals.json`
3. **Check signal polling task** is running:
   - Look for `TradingViewCryptoSignalPollingTimedTask` in logs
   - Default polling interval: 7 seconds

4. **Trading automatically suspends** when health checks fail:
   - After `HealthCheckSuspendTradingTimeout` (default 900s) of stale data
   - Trading resumes automatically when signals recover

### High Memory Usage

**Symptoms:**
- Process memory growing continuously
- System becoming unresponsive

**Diagnosis:**
```bash
# Check process memory
ps aux | grep IntelliTrader

# Check .NET memory stats (if enabled)
dotnet-counters monitor -p <PID> --counters System.Runtime
```

**Resolution:**

1. **Check order history size** in `config/trading.json`:
```json
{
  "Trading": {
    "MaxOrderHistorySize": 10000  // Reduce if needed
  }
}
```

2. **Check log file accumulation:**
```bash
ls -la log/
# Clean old logs if excessive
find log/ -name "*.txt" -mtime +7 -delete
```

3. **Check for memory leaks in timed tasks:**
   - Review recent code changes
   - Ensure disposable objects are being disposed

4. **Restart service** as temporary mitigation:
```bash
systemctl restart intellitrader
```

### Order Execution Failures

**Symptoms:**
- Buy/Sell orders not executing
- Log shows order errors
- Health check shows `ORDER CIRCUIT BREAKER OPENED`

**Diagnosis:**
```bash
# Check trade logs
grep -i "order\|trade\|buy\|sell" log/*-trades.txt | tail -50
grep -i "ORDER CIRCUIT BREAKER\|PlaceOrder" log/*-general.txt | tail -50
```

**Resolution:**

1. **Check if trading is suspended:**
```bash
curl http://localhost:7000/api/status | jq '.TradingSuspended'
```

2. **If order circuit breaker opened:**
   - The system automatically suspends order execution for 60 seconds
   - Check for API errors (insufficient balance, invalid pair, etc.)
   - Verify API keys are valid and have trading permissions

3. **Check Binance account:**
   - Verify sufficient balance
   - Check if trading pair is valid and not delisted
   - Verify API key permissions include trading

4. **Order retry behavior:**
   - Orders only retry once (to prevent duplicates)
   - Only connection errors trigger retry (not response errors)
   - If timeout occurs: **MANUALLY VERIFY ORDER STATUS** on Binance

5. **Resume trading if safe:**
   - Trading resumes automatically when health checks pass
   - Manual restart if needed: `systemctl restart intellitrader`

---

## Observability

### OpenTelemetry Integration

IntelliTrader includes built-in OpenTelemetry instrumentation in `IntelliTrader.Infrastructure.Telemetry`:

**Service Name:** `IntelliTrader`

**Configuration** (in `Startup.cs`):
```csharp
// Console exporter enabled in Development
// OTLP exporter for production (set OTEL_EXPORTER_OTLP_ENDPOINT)
services.AddIntelliTraderTelemetry(enableConsoleExporter: isDevelopment);
```

### Available Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `trades.executed` | Counter | Total trades executed (with pair, order_type, virtual tags) |
| `trades.buy_orders` | Counter | Total buy orders |
| `trades.sell_orders` | Counter | Total sell orders |
| `trades.failed` | Counter | Failed trade attempts (with reason tag) |
| `trades.dca_orders` | Counter | DCA orders executed |
| `trades.stop_loss_triggered` | Counter | Stop loss triggers |
| `trades.trailing_triggered` | Counter | Trailing stop triggers |
| `positions.open` | Gauge | Current open positions count |
| `portfolio.value` | Gauge | Total portfolio value |
| `trailing.buy_active` | Gauge | Active buy trailing stops |
| `trailing.sell_active` | Gauge | Active sell trailing stops |
| `trades.profit` | Histogram | Profit/loss percentage distribution |
| `order.latency` | Histogram | Order execution latency (ms) |
| `position.duration` | Histogram | Position hold duration (hours) |
| `trades.cost` | Histogram | Trade cost distribution |

### Distributed Tracing

Activity spans are created for:
- `BuyOrder` - Buy order execution
- `SellOrder` - Sell order execution
- `ProcessTradingRules` - Trading rule evaluation
- `ProcessPosition` - Position processing
- `ProcessTrailing` - Trailing stop processing
- `DCAOrder` - DCA order execution
- `StopLoss` - Stop loss processing

### Logging Configuration

Serilog is configured in `config/logging.json`:

**Log Files:**
- `log/{Date}-general.txt` - General application logs
- `log/{Date}-trades.txt` - Trade-specific logs

**Log Levels:**
- `Verbose` - Most detailed (development)
- `Information` - Normal operation
- `Warning` - Health check failures, circuit breaker events
- `Error` - Order failures, critical errors

**Example log output:**
```
14:32:05 [INF] Health check results:
 [+] (14:32:01) Account refreshed
 [+] (14:32:03) Tickers updated
 [+] (14:32:04) TV Signals received
 [-] (14:30:00) Trading pairs processed  # Stale - will trigger suspension
```

### Health Check Endpoints

Internal health checks are accessible via the web API:

```bash
# Get current status including health checks
curl http://localhost:7000/api/status
```

**Health Check Items:**

| Check | Updates When |
|-------|-------------|
| `Account refreshed` | Account balance refreshed from exchange |
| `Tickers updated` | Price data updated (WebSocket or REST) |
| `TV Signals received` | TradingView signals polled |
| `Trading pairs processed` | Trading pairs evaluated |
| `Signals rules processed` | Signal buy rules evaluated |
| `Trading rules processed` | Trading sell rules evaluated |

**Health Check Behavior:**
- Checks run every `HealthCheckInterval` seconds (default: 180)
- Trading suspends if any check is stale for `HealthCheckSuspendTradingTimeout` seconds (default: 900)
- After `HealthCheckFailuresToRestartServices` failures (default: 3), services restart automatically

### Alert Recommendations

Configure Telegram alerts in `config/notification.json`:

```json
{
  "Notification": {
    "Enabled": true,
    "TelegramEnabled": true,
    "TelegramBotToken": "YOUR_BOT_TOKEN",
    "TelegramChatId": YOUR_CHAT_ID,
    "TelegramAlertsEnabled": true
  }
}
```

**Recommended Alerts to Monitor:**

1. **Health Check Failures**
   - "Health check failed" notifications
   - Trading suspension/resume events

2. **Circuit Breaker Events**
   - "ORDER CIRCUIT BREAKER OPENED" - Critical
   - "Circuit breaker OPENED for exchange reads" - Warning

3. **Trading Events**
   - Order execution failures
   - Stop loss triggers
   - Large position changes

4. **Infrastructure Metrics** (via OpenTelemetry exporter):
   - `trades.failed` counter increases
   - `order.latency` p99 > 5000ms
   - Memory usage above threshold

### Production Monitoring Setup

For production, export telemetry to an observability platform:

```bash
# Set OTLP endpoint (e.g., for Jaeger, Grafana, etc.)
export OTEL_EXPORTER_OTLP_ENDPOINT=http://collector:4317

# Or configure in code
services.AddIntelliTraderTelemetryWithOtlp("http://collector:4317");
```

**Recommended Dashboard Panels:**
1. Trade execution rate (by type: buy/sell/dca)
2. Order latency percentiles (p50, p95, p99)
3. Open positions count over time
4. Portfolio value trend
5. Trade profit/loss distribution
6. Health check status grid
7. Circuit breaker state timeline
