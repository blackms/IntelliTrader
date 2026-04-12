# IntelliTrader Operations Runbook

Production deployment, operations, and incident response procedures.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Startup Procedures](#startup-procedures)
3. [Shutdown Procedures](#shutdown-procedures)
4. [Health Check Verification](#health-check-verification)
5. [Common Troubleshooting](#common-troubleshooting)
6. [Incident Response](#incident-response)
7. [Backup and Recovery](#backup-and-recovery)
8. [Monitoring](#monitoring)
9. [Configuration Reference](#configuration-reference)

---

## Prerequisites

### Required Software

| Tool | Purpose |
|------|---------|
| Docker 24+ | Container runtime |
| docker compose v2 | Local/VM deployment |
| kubectl 1.28+ | Kubernetes deployment |
| helm 3.x | Helm chart deployment |
| wget / curl | Health check verification |

### Network Requirements

- **Outbound HTTPS** to `api.binance.com` and `stream.binance.com` (WebSocket)
- **Outbound HTTPS** to TradingView signal endpoints
- **Outbound HTTPS** to `api.telegram.org` (if Telegram notifications enabled)
- **Inbound TCP 7000** for dashboard access (configurable)

### Singleton Constraint

IntelliTrader holds in-memory positions, trailing orders, and a persistent WebSocket connection. **Never run more than one instance** against the same Binance account. Running multiple replicas causes duplicate trades and split state.

---

## Startup Procedures

### Docker Compose

```bash
# 1. Prepare configuration
cp .env.example .env          # edit host port, image tag as needed

# 2. Mount API keys for live trading (skip for virtual trading)
#    Uncomment the keys.bin volume line in docker-compose.yml
#    Place encrypted keys.bin next to docker-compose.yml

# 3. Start
docker compose up -d

# 4. Verify startup
docker compose logs -f        # watch for "Welcome to IntelliTrader"
docker inspect --format='{{.State.Health.Status}}' intellitrader
```

**Production-only (skip dev overrides):**

```bash
docker compose -f docker-compose.yml up -d
```

The dev override (`docker-compose.override.yml`) sets `ASPNETCORE_ENVIRONMENT=Development` and mounts config read-write. Production uses read-only config mounts.

### Kubernetes (Helm)

```bash
# 1. Create namespace
kubectl create namespace trading

# 2. Create secret for API keys (live trading only)
kubectl create secret generic intellitrader-keys \
  --from-file=keys.bin=./keys.bin \
  -n trading

# 3. Install chart
helm install intellitrader deploy/helm/intellitrader/ \
  -n trading \
  --set secret.existingSecret=intellitrader-keys \
  --set config.files."trading\.json"='{"Trading":{"VirtualTrading":false}}'

# 4. Verify
kubectl get pods -n trading -w
kubectl logs -f deploy/intellitrader -n trading
```

The Helm chart uses `strategy: Recreate` to prevent overlapping instances during rolling updates.

### Bare Metal / VM

```bash
# 1. Build
dotnet publish IntelliTrader/IntelliTrader.csproj -c Release -o /opt/intellitrader

# 2. Copy config
cp -r IntelliTrader/config /opt/intellitrader/config
mkdir -p /opt/intellitrader/data /opt/intellitrader/log

# 3. Encrypt API keys (live trading only)
dotnet /opt/intellitrader/IntelliTrader.dll \
  --encrypt --path=/opt/intellitrader/keys.bin \
  --publickey=YOUR_API_KEY --privatekey=YOUR_API_SECRET

# 4. Start
dotnet /opt/intellitrader/IntelliTrader.dll
```

### Post-Startup Checklist

After starting by any method, verify:

1. Dashboard loads at `http://<host>:7000`
2. `/api/health` returns `{"status":"ok"}`
3. `/health/ready` returns `{"status":"ready"}` (may take 30-60s)
4. Logs show "Welcome to IntelliTrader" followed by health check passes
5. If live trading: verify account balance shows on dashboard

---

## Shutdown Procedures

### Graceful Shutdown

The application handles SIGTERM with a 30-second grace period to save positions.

**Docker Compose:**

```bash
docker compose down              # sends SIGTERM, waits 30s grace period
```

**Kubernetes:**

```bash
kubectl delete pod <pod-name> -n trading   # terminationGracePeriodSeconds: 30
# Or scale to zero:
kubectl scale deploy/intellitrader --replicas=0 -n trading
```

**Bare Metal:**

```bash
# If running interactively: press Enter/Return
# If running as systemd service:
systemctl stop intellitrader
```

### Emergency Stop

Use when you need to halt trading immediately without waiting for grace period:

```bash
# Docker
docker kill intellitrader

# Kubernetes
kubectl delete pod <pod-name> -n trading --grace-period=0 --force

# Bare metal
kill -9 <pid>
```

**After emergency stop:** Check `/app/data/` files for consistency. Positions in memory may not have been flushed. Verify open orders on Binance directly.

---

## Health Check Verification

### Endpoints

| Endpoint | Auth | Purpose | Healthy Response |
|----------|------|---------|-----------------|
| `GET /api/health` | None | Docker HEALTHCHECK, basic liveness | `200 {"status":"ok"}` |
| `GET /health/live` | None | K8s liveness probe | `200 {"status":"alive","timestamp":"..."}` |
| `GET /health/ready` | None | K8s readiness probe | `200 {"status":"ready",...}` or `503 {"status":"not_ready",...}` |
| `GET /api/status` | Yes | Full status with health details | `200` (requires session auth) |

### Verification Commands

```bash
# Docker: check built-in healthcheck
docker inspect --format='{{.State.Health.Status}}' intellitrader
docker inspect --format='{{range .State.Health.Log}}{{.Output}}{{end}}' intellitrader

# Direct endpoint check
curl -s http://localhost:7000/api/health | jq .
curl -s http://localhost:7000/health/ready | jq .

# Kubernetes
kubectl get pods -n trading     # READY column shows probe status
kubectl describe pod <pod> -n trading | grep -A5 "Conditions"
```

### Readiness Probe Details

The `/health/ready` endpoint returns `503` when:

- CoreService has not finished starting
- Trading is suspended (manual or automatic)
- Any internal health check is failing

Response includes per-component breakdown:

```json
{
  "status": "not_ready",
  "coreRunning": true,
  "tradingSuspended": true,
  "failingCheckCount": 1,
  "checks": [
    {"name": "Tickers updated", "failed": true, "message": "...", "lastUpdated": "..."}
  ]
}
```

### Internal Health Checks

These are evaluated every `HealthCheckInterval` seconds (default: 180):

| Check | Passes When |
|-------|-------------|
| Account refreshed | Account balance refreshed from exchange |
| Tickers updated | Price data received via WebSocket or REST |
| TV Signals received | TradingView signals polled successfully |
| Trading pairs processed | Trading pairs evaluated by rules engine |
| Signals rules processed | Signal buy rules evaluated |
| Trading rules processed | Trading sell/DCA rules evaluated |

**Automatic escalation:**

- Stale for `HealthCheckSuspendTradingTimeout` (default 900s) -> trading suspends
- `HealthCheckFailuresToRestartServices` consecutive failures (default 3) -> services restart automatically

---

## Common Troubleshooting

### WebSocket Disconnects

**Symptoms:** Stale ticker prices, "Tickers updated" health check failing.

**Diagnosis:**

```bash
grep -i "websocket\|disconnect\|reconnect" /app/log/*-general.txt | tail -20
```

**Resolution:**

1. The WebSocket pipeline automatically retries 5 times with fallback to REST API
2. If reconnection fails persistently, check outbound connectivity to `stream.binance.com`
3. Restart the service to reset connections: `docker compose restart`

### Signal Failures

**Symptoms:** "TV Signals received" health check stale, no new buy signals.

**Diagnosis:**

```bash
grep -i "signal\|tradingview" /app/log/*-general.txt | tail -20
```

**Resolution:**

1. Check TradingView service availability
2. Verify `config/signals.json` signal definitions are correct
3. Trading automatically suspends after `HealthCheckSuspendTradingTimeout` and resumes when signals recover

### Exchange API Errors

**Symptoms:** Order failures, circuit breaker messages in logs.

**Diagnosis:**

```bash
grep -i "429\|503\|circuit.breaker\|rate.limit" /app/log/*-general.txt | tail -20
```

**Resolution:**

| Error | Action |
|-------|--------|
| HTTP 429 (rate limited) | Wait for cooldown. Binance bans escalate: 2min -> 3 days |
| HTTP 503 (service unavailable) | Check [Binance status](https://www.binance.com/en/support/announcement) |
| Circuit breaker OPEN | Automatically recovers after 30-60s. Restart if stuck |
| Insufficient balance | Check account on Binance, adjust `BuyMaxCost` in trading.json |
| Invalid pair / delisted | Remove pair from config, check Binance announcements |

**Resilience pipelines:**

- **ReadPipeline:** 3 retries, exponential backoff + jitter, 30s timeout, circuit breaker at 50% failure
- **OrderPipeline:** 1 retry only (prevents duplicates), 15s timeout, circuit breaker at 30% failure
- **WebSocketPipeline:** 5 reconnect attempts, automatic REST fallback

### Container Restart Loops

**Symptoms:** Pod in CrashLoopBackOff, container restarting repeatedly.

**Diagnosis:**

```bash
# Docker
docker logs intellitrader --tail 50

# Kubernetes
kubectl logs <pod> -n trading --previous    # logs from crashed container
kubectl describe pod <pod> -n trading       # check Events section
```

**Common causes:**

| Cause | Fix |
|-------|-----|
| Missing config files | Ensure config volume is mounted correctly |
| Invalid JSON in config | Validate JSON syntax in all config files |
| keys.bin not found (live trading) | Mount keys.bin volume, or set `VirtualTrading: true` |
| Port 7000 already in use | Change `INTELLITRADER_HOST_PORT` in `.env` |
| Insufficient memory | Increase memory limit (minimum 256Mi, recommended 512Mi) |

### High Memory Usage

**Diagnosis:**

```bash
# Docker
docker stats intellitrader

# Kubernetes
kubectl top pod -n trading
```

**Resolution:**

1. Reduce `MaxOrderHistorySize` in `config/trading.json`
2. Clean old log files: `find /app/log -name "*.txt" -mtime +7 -delete`
3. Restart service as temporary mitigation

---

## Incident Response

### Severity Levels

| Level | Description | Examples |
|-------|-------------|---------|
| SEV-1 | Active financial loss | Unintended live trades, wrong pair trading, API key compromised |
| SEV-2 | Trading halted | All health checks failing, exchange unreachable, crash loop |
| SEV-3 | Degraded operation | Signal delays, intermittent WebSocket drops, slow order execution |

### SEV-1: Suspend Trading Immediately

**Step 1 -- Stop all trading activity:**

```bash
# Option A: Suspend via dashboard UI (fastest if accessible)
# Navigate to dashboard -> click "Suspend Trading"

# Option B: Set virtual trading mode (prevents real orders)
# Edit config/trading.json: "VirtualTrading": true
# Config hot-reloads automatically

# Option C: Kill the process
docker compose down
# or
kubectl scale deploy/intellitrader --replicas=0 -n trading
```

**Step 2 -- Assess damage:**

1. Log into Binance directly and check open orders
2. Cancel any unintended open orders on Binance
3. Review trade logs: `grep -i "order\|trade\|buy\|sell" /app/log/*-trades.txt | tail -100`
4. Check account balance matches expected state

**Step 3 -- If API key compromised:**

1. Immediately disable the API key on Binance
2. Create new API key with restricted permissions (no withdrawal)
3. Re-encrypt: `dotnet IntelliTrader.dll --encrypt --path=keys.bin --publickey=NEW_KEY --privatekey=NEW_SECRET`
4. Restart with new keys

### SEV-2: Trading Halted

**Step 1 -- Diagnose:**

```bash
curl -s http://localhost:7000/health/ready | jq .
grep -i "error\|critical\|exception" /app/log/*-general.txt | tail -30
```

**Step 2 -- Resolve based on cause:**

- **Exchange unreachable:** Check Binance status, check network. Wait or restart.
- **Crash loop:** Check logs for root cause (see Container Restart Loops above).
- **Health check cascade:** Restart service. If persists, check each component individually.

**Step 3 -- Verify recovery:**

```bash
# Wait for readiness
watch -n5 'curl -s http://localhost:7000/health/ready | jq .status'
```

### Position Recovery After Crash

If the process crashed without graceful shutdown:

1. Check data files in `/app/data/`:
   - `exchange-account.json` (live trading state)
   - `virtual-account.json` (virtual trading state)
2. Compare positions in data file against actual Binance account
3. If data file is corrupt or stale, delete it and restart (positions will be re-synced from exchange on startup for live trading)

---

## Backup and Recovery

### What to Back Up

| Item | Location | Frequency | Priority |
|------|----------|-----------|----------|
| Configuration | `/app/config/*.json` | After every change | Critical |
| API keys | `keys.bin` | After rotation | Critical |
| Account state | `/app/data/` | Daily | High |
| Trade logs | `/app/log/` | Weekly | Medium |

### Backup Procedures

**Docker volumes:**

```bash
# Stop to ensure consistency
docker compose stop

# Backup config (bind mount)
tar czf backup-config-$(date +%Y%m%d).tar.gz ./IntelliTrader/config/

# Backup data volume
docker run --rm -v intellitrader-data:/data -v $(pwd):/backup \
  alpine tar czf /backup/backup-data-$(date +%Y%m%d).tar.gz -C /data .

# Backup log volume
docker run --rm -v intellitrader-log:/data -v $(pwd):/backup \
  alpine tar czf /backup/backup-log-$(date +%Y%m%d).tar.gz -C /data .

docker compose start
```

**Kubernetes PVC:**

```bash
# Scale down first
kubectl scale deploy/intellitrader --replicas=0 -n trading

# Copy data from PVC
kubectl run backup --image=alpine --restart=Never -n trading \
  --overrides='{"spec":{"containers":[{"name":"backup","image":"alpine",
  "command":["tar","czf","/backup/data.tar.gz","-C","/data","."],
  "volumeMounts":[{"name":"data","mountPath":"/data"},{"name":"backup","mountPath":"/backup"}]}],
  "volumes":[{"name":"data","persistentVolumeClaim":{"claimName":"intellitrader-data"}},
  {"name":"backup","emptyDir":{}}]}}'

kubectl cp trading/backup:/backup/data.tar.gz ./backup-data-$(date +%Y%m%d).tar.gz
kubectl delete pod backup -n trading

# Scale back up
kubectl scale deploy/intellitrader --replicas=1 -n trading
```

### Recovery Procedures

**Restore config:**

```bash
tar xzf backup-config-YYYYMMDD.tar.gz -C ./IntelliTrader/config/
# Config hot-reloads; restart only if needed
```

**Restore data volume:**

```bash
docker compose stop
docker run --rm -v intellitrader-data:/data -v $(pwd):/backup \
  alpine sh -c "rm -rf /data/* && tar xzf /backup/backup-data-YYYYMMDD.tar.gz -C /data"
docker compose start
```

**Full disaster recovery:**

1. Deploy fresh instance (Docker or Helm)
2. Restore config from backup
3. Mount `keys.bin` from secure backup
4. Restore data volume from backup
5. Start and verify via health checks
6. Verify positions match Binance account

---

## Monitoring

### Health Endpoints Summary

Poll these endpoints for automated monitoring:

```bash
# Basic liveness (Docker, load balancers)
curl -sf http://localhost:7000/api/health || echo "DOWN"

# Full readiness (Kubernetes, alerting)
curl -sf http://localhost:7000/health/ready || echo "NOT READY"
```

### Log Files

Serilog writes to `/app/log/`:

| File Pattern | Content |
|-------------|---------|
| `{Date}-general.txt` | Application logs, health checks, errors |
| `{Date}-trades.txt` | Trade execution logs (buys, sells, DCA) |

**Key log patterns to watch:**

```bash
# Health check failures
grep "\[-\]" /app/log/*-general.txt | tail -10

# Circuit breaker events
grep -i "circuit.breaker" /app/log/*-general.txt | tail -10

# Order failures
grep -i "failed\|error" /app/log/*-trades.txt | tail -10
```

### Telegram Notifications

Configure in `config/notification.json`:

```json
{
  "Notification": {
    "Enabled": true,
    "TelegramEnabled": true,
    "TelegramBotToken": "BOT_TOKEN",
    "TelegramChatId": CHAT_ID,
    "TelegramAlertsEnabled": true
  }
}
```

Alerts include: health check failures, trading suspension/resume, order failures, stop loss triggers.

### OpenTelemetry

IntelliTrader exports metrics and traces via OpenTelemetry. Set the OTLP endpoint:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://collector:4317
```

**Key metrics to dashboard:**

| Metric | Type | Alert Threshold |
|--------|------|----------------|
| `trades.failed` | Counter | Any increase |
| `order.latency` | Histogram | p99 > 5000ms |
| `positions.open` | Gauge | Unexpected changes |
| `portfolio.value` | Gauge | Significant drops |
| `trades.stop_loss_triggered` | Counter | Any increase |

### Docker HEALTHCHECK

The Dockerfile defines a built-in healthcheck:

```
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3
    CMD wget --quiet --tries=1 -O /dev/null http://127.0.0.1:7000/api/health || exit 1
```

Monitor via: `docker inspect --format='{{.State.Health.Status}}' intellitrader`

---

## Configuration Reference

### Key Settings Quick Reference

| Setting | File | Default | Description |
|---------|------|---------|-------------|
| `VirtualTrading` | trading.json | `true` | `true` = paper trading, `false` = real money |
| `Market` | trading.json | `BTC` | Base market (e.g., BTC, USDT) |
| `HealthCheckEnabled` | core.json | `true` | Enable internal health monitoring |
| `HealthCheckInterval` | core.json | `180` | Seconds between health checks |
| `HealthCheckSuspendTradingTimeout` | core.json | `900` | Seconds of stale data before auto-suspend |
| `HealthCheckFailuresToRestartServices` | core.json | `3` | Consecutive failures before service restart |
| `PasswordProtected` | core.json | `true` | Require password for dashboard |
| `DebugMode` | core.json | `false` | Enable verbose logging (disable in production) |
| `KeysPath` | exchange.json | `keys.bin` | Path to encrypted API key file |
| `Port` | web.json | `7000` | Dashboard port |

### Configuration Files

All files in `/app/config/` support hot-reload (changes take effect without restart):

| File | Purpose |
|------|---------|
| `core.json` | Instance name, health checks, password, timezone |
| `trading.json` | Market, buy/sell params, DCA levels, virtual trading |
| `exchange.json` | API keys path, rate limiting |
| `signals.json` | TradingView signal definitions |
| `rules.json` | Signal rules (buy triggers), trading rules (sell/DCA) |
| `web.json` | Dashboard port, debug mode |
| `logging.json` | Serilog levels, file paths |
| `notification.json` | Telegram bot token, chat ID, alert settings |
| `paths.json` | References to all other config files |
| `backtesting.json` | Historical replay settings |
| `caching.json` | Cache configuration |
| `integration.json` | Third-party integration settings |

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Runtime environment |
| `ASPNETCORE_URLS` | `http://+:7000` | Listening URL |
| `INTELLITRADER_HEADLESS` | `true` (in Docker) | Suppress interactive prompts |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` | `true` | Use invariant globalization |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | (unset) | OpenTelemetry collector URL |
