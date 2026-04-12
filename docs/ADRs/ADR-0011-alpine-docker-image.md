# ADR-0011: Alpine-Based Docker Image for Production

## Status
Accepted

## Date
2026-04-12

## Context
IntelliTrader needs a production container image that balances size, security, and compatibility. The application targets .NET 9 and runs as a long-lived process on Kubernetes. Smaller images reduce pull times, storage costs, and the attack surface from unused OS packages.

The main candidates are Debian-based (`aspnet:9.0`), Ubuntu Chiseled (`aspnet:9.0-noble-chiseled`), and Alpine-based (`aspnet:9.0-alpine`) base images.

## Decision
We use `mcr.microsoft.com/dotnet/aspnet:9.0-alpine` as the production runtime base image and `mcr.microsoft.com/dotnet/sdk:9.0-alpine` for the build stage. Alpine uses musl libc instead of glibc, producing images roughly 5x smaller than Debian equivalents (~50MB vs ~250MB).

Key configuration choices:
- `RUN apk add --no-cache icu-libs` to enable globalization (currency formatting, date parsing)
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` to ensure correct locale behavior for financial data
- Multi-stage build: SDK stage compiles, runtime stage only contains the published output

## Consequences

### Positive
- Image size reduced from ~250MB to ~50MB, improving pull and startup times
- Smaller attack surface: Alpine ships with fewer packages and a minimal shell
- Faster CI builds due to smaller layer caching footprint
- Consistent with 12-factor app practices for immutable deployments

### Negative
- musl libc differences may cause subtle runtime behavior differences vs glibc
- Some native NuGet packages may not ship musl-compatible binaries
- Debugging in production is harder due to missing tools (no bash, limited coreutils)

### Neutral
- Alpine images require explicit timezone data (`tzdata` package) if local time formatting is needed

## References
- [ADR-0012: Helm Chart with Recreate Strategy](ADR-0012-helm-chart-recreate-strategy.md) - Deployment strategy for the containerized application
