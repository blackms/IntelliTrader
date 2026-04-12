# Alerting Rules

IntelliTrader includes a configurable alerting system that monitors system state and sends notifications via Telegram when alert conditions are detected. Alerts use debouncing to avoid notification spam -- each alert fires once when the condition becomes active, and once more when it resolves.

## Configuration

Alerting is configured in `config/alerting.json`:

```json
{
  "Alerting": {
    "Enabled": true,
    "CheckIntervalSeconds": 60,
    "TradingSuspendedAlert": true,
    "HealthCheckFailureThreshold": 3,
    "SignalStalenessMinutes": 5,
    "ConnectivityAlertEnabled": true,
    "ConsecutiveOrderFailureThreshold": 3,
    "HighErrorRateThreshold": 0.5
  }
}
```

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch for the alerting system |
| `CheckIntervalSeconds` | `60` | How often alert conditions are evaluated (seconds) |
| `TradingSuspendedAlert` | `true` | Alert when trading is suspended/resumed |
| `HealthCheckFailureThreshold` | `3` | Number of consecutive health check failures before alerting |
| `SignalStalenessMinutes` | `5` | Minutes without signal updates before alerting |
| `ConnectivityAlertEnabled` | `true` | Alert on exchange connectivity issues |
| `ConsecutiveOrderFailureThreshold` | `3` | Number of consecutive order failures before alerting |
| `HighErrorRateThreshold` | `0.5` | Error rate ratio (0.0-1.0) that triggers an alert |

## Alert Types

### Trading Suspended

**Trigger**: `ITradingService.IsTradingSuspended` flips to `true`.

**Notification**: `ALERT: Trading has been suspended. Manual intervention may be required.`

**Resolution notification**: `RESOLVED: Trading has been resumed.`

**Response procedure**:
1. Check the IntelliTrader logs for the root cause (health check failure, manual suspension, etc.)
2. If caused by a health check failure, investigate the failing health check (see below)
3. If the exchange is operational and the issue is transient, trading will auto-resume when health checks pass
4. For persistent issues, investigate exchange connectivity and API key validity
5. If manually suspended via API, use `ResumeTrading()` when ready

### Health Check Failures

**Trigger**: Any health check reports `Failed = true` for N consecutive checks (configurable via `HealthCheckFailureThreshold`).

**Notification**: `ALERT: Health check '<name>' has failed N consecutive times. Message: <details>`

**Resolution notification**: `RESOLVED: Health check '<name>' has recovered.`

**Health check names**:
- `Account refreshed` -- account balance polling
- `Tickers updated` -- exchange price feed
- `Trading pairs processed` -- pair evaluation loop
- `TV Signals received` -- TradingView signal polling
- `Signals rules processed` -- signal rule evaluation
- `Trading rules processed` -- trading rule evaluation

**Response procedure**:
1. Identify which health check is failing from the alert message
2. For `Tickers updated`: check exchange API status and network connectivity
3. For `TV Signals received`: check TradingView signal endpoint availability
4. For `Account refreshed`: verify API key permissions and rate limits
5. For processing health checks (`Trading pairs processed`, etc.): check logs for exceptions in the processing loop
6. If failures are transient, the alert will auto-resolve once the health check passes again

### Signal Staleness

**Trigger**: The TradingView signal health check (`TV Signals received`) has not been updated for more than `SignalStalenessMinutes` minutes.

**Notification**: `ALERT: Signal data is stale. Last update was X.X minutes ago (threshold: Y min).`

**Resolution notification**: `RESOLVED: Signal data is being received again.`

**Response procedure**:
1. Check TradingView signal endpoint availability
2. Verify network connectivity from the IntelliTrader host
3. Check `signals.json` configuration for correct signal URLs
4. Review logs for HTTP errors or timeouts in signal polling
5. If TradingView is experiencing an outage, wait for recovery -- the alert will auto-resolve

### Exchange Connectivity

**Trigger**: The `Tickers updated` health check reports `Failed = true`.

**Notification**: `ALERT: Exchange connectivity issue detected. Tickers health check failed.`

**Resolution notification**: `RESOLVED: Exchange connectivity has been restored.`

**Response procedure**:
1. Check Binance API status at https://www.binance.com/en/support/announcement
2. Verify network connectivity from the IntelliTrader host
3. Check if API keys are still valid and have correct permissions
4. Review `exchange.json` for correct configuration
5. Check rate limit usage -- excessive requests may cause temporary blocks
6. If using WebSocket, check for connection drops in logs and verify firewall rules

## Prerequisites

Alerting uses the existing Telegram notification system. Ensure the following are configured in `config/notification.json`:

```json
{
  "Notification": {
    "Enabled": true,
    "TelegramEnabled": true,
    "TelegramBotToken": "<your-bot-token>",
    "TelegramChatId": <your-chat-id>,
    "TelegramAlertsEnabled": true
  }
}
```

## Architecture

The alerting system consists of:

- `IAlertingService` / `AlertingService` -- monitors system state and evaluates alert conditions
- `AlertingTimedTask` -- periodic task that invokes `CheckAlerts()` at the configured interval
- `AlertingConfig` -- configuration model loaded from `alerting.json`

The service is registered as a singleton in the Autofac DI container and started/stopped by `CoreService`. It uses `Lazy<ITradingService>` and `Lazy<ICoreService>` to avoid dependency injection cycles.
