# IntelliTrader Documentation

Welcome to the IntelliTrader documentation hub. Find guides, design documents, and references below.

---

## Quick Links

| Document | Description |
|----------|-------------|
| [ONBOARDING.md](./ONBOARDING.md) | New contributor guide - start here |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | Development workflow and PR guidelines |

---

## Getting Started

### New to IntelliTrader?

Start with the [Onboarding Guide](./ONBOARDING.md) which covers:
- Project overview in 5 minutes
- Architecture tour with diagrams
- Where to start reading code
- Coding conventions

### Ready to Contribute?

Read the [Contributing Guide](./CONTRIBUTING.md) for:
- Development workflow (fork, branch, commit)
- PR guidelines and review checklist
- Code style guide
- Test patterns

---

## Design Documents

Detailed technical design documents for major features:

| Document | Topic |
|----------|-------|
| [DOMAIN_EVENTS_DESIGN.md](./design/DOMAIN_EVENTS_DESIGN.md) | Domain events architecture and implementation |
| [POLLY_RESILIENCE_DESIGN.md](./design/POLLY_RESILIENCE_DESIGN.md) | Resilience patterns with Polly v8 |

---

## Architecture Decision Records

Architecture decisions are tracked in [`docs/ADRs/`](./ADRs/).

---

## Configuration Reference

Configuration files are located in `IntelliTrader/config/`:

| File | Purpose |
|------|---------|
| `core.json` | Health checks, debug mode, password protection |
| `trading.json` | Market settings, buy/sell parameters, DCA levels, risk management |
| `signals.json` | TradingView signal definitions and polling intervals |
| `rules.json` | Signal rules (buy triggers), trading rules (sell/DCA conditions) |
| `web.json` | Web dashboard port and settings |
| `notification.json` | Telegram bot configuration |
| `exchange.json` | Exchange-specific settings |
| `logging.json` | Log levels and output configuration |
| `backtesting.json` | Backtesting mode configuration |

---

## API Reference

### REST Endpoints

| Method | Endpoint | Description |
|:------:|----------|-------------|
| `GET` | `/api/status` | Bot status, balance, health |
| `GET` | `/api/signal-names` | Available signal sources |
| `GET` | `/api/health` | Health check endpoint |
| `POST` | `/api/trading-pairs` | Active positions |
| `POST` | `/api/market-pairs` | Market data with signals |

### Trading Operations

| Method | Endpoint | Description |
|:------:|----------|-------------|
| `POST` | `/Buy` | Execute manual buy |
| `POST` | `/Sell` | Execute manual sell |
| `POST` | `/Swap` | Swap position |
| `POST` | `/Settings` | Update runtime settings |

### SignalR Hub

Connect to `/tradingHub` for real-time updates:
- `StatusUpdate` - Bot status changes
- `TradingPairsUpdate` - Position updates
- `OrderPlaced` - New order notifications
- `OrderFilled` - Filled order notifications

---

## Useful Commands

```bash
# Build
dotnet build IntelliTrader.sln

# Run (virtual trading)
dotnet run --project IntelliTrader

# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Encrypt API keys for live trading
dotnet run --project IntelliTrader -- \
  --encrypt --path keys.bin \
  --publickey YOUR_KEY --privatekey YOUR_SECRET
```

---

## External Resources

- [.NET 9 Documentation](https://learn.microsoft.com/en-us/dotnet/)
- [Binance API Documentation](https://binance-docs.github.io/apidocs/)
- [TradingView Technical Analysis](https://www.tradingview.com/support/solutions/43000614149-technical-analysis/)
- [Polly Resilience Library](https://github.com/App-vNext/Polly)
- [Autofac DI Container](https://autofac.org/)

---

## Support

- **Questions**: Open a [GitHub Discussion](https://github.com/blackms/IntelliTrader/discussions)
- **Bugs**: Open a [GitHub Issue](https://github.com/blackms/IntelliTrader/issues)
- **Security**: Contact maintainers directly
