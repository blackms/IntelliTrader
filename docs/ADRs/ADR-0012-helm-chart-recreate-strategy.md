# ADR-0012: Helm Chart with Recreate Deployment Strategy

## Status
Accepted

## Date
2026-04-12

## Context
IntelliTrader is deployed to Kubernetes via a Helm chart. The application maintains in-memory state (open positions, trailing orders, signal ratings) and holds a single WebSocket connection to the Binance exchange. Running multiple replicas simultaneously would cause:

1. Duplicate order execution (both replicas acting on the same signal)
2. Conflicting exchange state (two WebSocket streams with divergent order books)
3. Data corruption in the shared trading account

A rolling update strategy (`RollingUpdate`) briefly runs old and new pods concurrently, which is unsafe for this singleton trading bot.

## Decision
We use the `Recreate` deployment strategy in the Helm chart. This terminates the existing pod completely before starting the new one, guaranteeing that at most one instance is active at any time.

```yaml
strategy:
  type: Recreate
```

Combined with `replicas: 1`, this ensures single-instance operation. The brief downtime during restarts (typically 10-30 seconds) is acceptable because:
- Trading signals are cached and re-fetched on startup
- Open positions are tracked on the exchange, not only in-memory
- Missed signals during restart represent negligible risk

## Consequences

### Positive
- Eliminates risk of duplicate order execution during deployments
- Simple to reason about: exactly one instance running at all times
- No need for distributed locking or leader election
- Safe for stateful, singleton workloads

### Negative
- Brief downtime during every deployment (pod termination + startup)
- No zero-downtime deployments; maintenance windows may be needed for critical market periods
- Cannot horizontally scale without architectural changes (distributed state, leader election)

### Neutral
- Health probes (liveness and readiness) control when the new pod receives traffic after restart

## References
- [ADR-0011: Alpine Docker Image](ADR-0011-alpine-docker-image.md) - Container image used by this deployment
- [ADR-0013: Anonymous Health Endpoints](ADR-0013-anonymous-health-endpoints.md) - Health checks used by Kubernetes probes
