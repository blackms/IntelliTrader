# ADR-0014: Virtual Trading Mode

## Status
Accepted

## Date
2026-04-12

## Context
Testing trading strategies with real funds is expensive and risky. Developers and traders need a way to validate signal configurations, rule parameters, and DCA settings against live market data without executing real orders on the exchange.

A paper trading mode must use real-time prices from Binance but simulate order fills, balance tracking, and position management entirely in memory.

## Decision
We implement a virtual trading mode controlled by a single configuration toggle in `trading.json`:

```json
{
  "VirtualTrading": true
}
```

When enabled:
- **Prices are real**: The exchange service fetches live ticker data from Binance via WebSocket
- **Orders are simulated**: Buy/sell operations update an in-memory virtual account instead of calling the exchange API
- **Positions are tracked**: Virtual positions maintain entry price, quantity, DCA levels, and trailing state
- **Rules execute normally**: Signal and trading rules evaluate identically in both modes

The trading service checks `Config.VirtualTrading` before routing orders to the exchange. This keeps the trading logic unified; only the execution layer differs.

## Consequences

### Positive
- Risk-free strategy validation against live market conditions
- No code branching in rules, signals, or DCA logic; the same pipeline runs in both modes
- Fast iteration: change config, restart, observe behavior
- New users can familiarize themselves with the system before committing funds

### Negative
- Virtual fills assume instant execution at current price; real markets have slippage and partial fills
- No exchange-side validation (minimum order size, balance checks) in virtual mode
- Virtual and live performance may diverge in volatile markets

### Neutral
- Virtual trading state is lost on restart (positions are in-memory only)
- The web dashboard displays virtual positions identically to real ones, distinguished only by a config indicator

## References
- [ADR-0001: Autofac Dependency Injection](ADR-0001-dependency-injection.md) - Service resolution for trading mode
- [ADR-0005: Rule Engine](ADR-0005-rule-engine.md) - Rules execute identically in both modes
