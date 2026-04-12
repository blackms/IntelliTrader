# ADR-0013: Anonymous Health Endpoints

## Status
Accepted

## Date
2026-04-12

## Context
IntelliTrader runs on Kubernetes, which requires HTTP health endpoints for liveness and readiness probes. The web dashboard uses cookie-based authentication to protect trading operations. Kubernetes probes cannot authenticate, so health endpoints must be accessible without credentials.

Two endpoints are needed:
- **Liveness** (`/health/live`): Is the process running and not deadlocked?
- **Readiness** (`/health/ready`): Is the application connected to the exchange and able to trade?

## Decision
We expose `/health/live` and `/health/ready` as anonymous endpoints outside the authentication middleware group. All other endpoints (dashboard, trading APIs, SignalR hub) remain behind authentication.

The readiness probe checks critical service health: exchange WebSocket connectivity, signal service availability, and trading service initialization. The liveness probe returns 200 if the process is responsive.

```csharp
app.UseEndpoints(endpoints =>
{
    // Anonymous health endpoints - outside auth group
    endpoints.MapGet("/health/live", () => Results.Ok("alive"));
    endpoints.MapGet("/health/ready", (IHealthCheckService healthCheck) =>
        healthCheck.IsReady() ? Results.Ok("ready") : Results.StatusCode(503));

    // Authenticated endpoints
    endpoints.MapControllerRoute(...);
    endpoints.MapHub<TradingHub>("/trading-hub");
});
```

## Consequences

### Positive
- Kubernetes can probe health without credentials or service account tokens
- Clear separation: health endpoints are operational, not business-facing
- Readiness probe prevents traffic routing before exchange connection is established
- Liveness probe enables automatic pod restart on deadlock

### Negative
- Health endpoints leak minimal information (alive/ready status) to unauthenticated callers
- Must be careful not to expose sensitive data (balances, positions) through health responses

### Neutral
- Standard Kubernetes practice; aligns with industry conventions for health checking

## References
- [ADR-0012: Helm Chart with Recreate Strategy](ADR-0012-helm-chart-recreate-strategy.md) - Deployment using these health probes
- [ADR-0009: Web Dashboard](ADR-0009-web-dashboard.md) - Authentication setup for the dashboard
