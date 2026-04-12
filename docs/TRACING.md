# Distributed Tracing

IntelliTrader uses [OpenTelemetry](https://opentelemetry.io/) for distributed tracing
and exports telemetry data via the OTLP (OpenTelemetry Protocol) exporter. Traces can
be viewed in any OTLP-compatible backend such as Jaeger, Grafana Tempo, or Zipkin.

## Quick Start with Jaeger

Run Jaeger locally using Docker (all-in-one image with OTLP support):

```bash
docker run -d --name jaeger \
  -e COLLECTOR_OTLP_ENABLED=true \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:1.58
```

| Port  | Purpose                  |
|-------|--------------------------|
| 16686 | Jaeger UI                |
| 4317  | OTLP gRPC receiver       |
| 4318  | OTLP HTTP/Protobuf       |

Then start IntelliTrader. The default OTLP endpoint is `http://localhost:4317` (gRPC).

Open <http://localhost:16686> to browse traces.

## Configuration

### Environment Variables

| Variable                         | Default                    | Description                         |
|----------------------------------|----------------------------|-------------------------------------|
| `OTEL_EXPORTER_OTLP_ENDPOINT`   | `http://localhost:4317`    | OTLP collector endpoint             |
| `ASPNETCORE_ENVIRONMENT`         | `Production`               | Added as `deployment.environment`   |

### Programmatic Configuration

In your startup code, choose between the two extension methods:

```csharp
// Development: console exporter only
services.AddIntelliTraderTelemetry(enableConsoleExporter: true);

// Production: OTLP export to Jaeger/Tempo
services.AddIntelliTraderTelemetryWithOtlp();

// Explicit endpoint with HTTP/Protobuf transport
services.AddIntelliTraderTelemetryWithOtlp(
    otlpEndpoint: "http://tempo.internal:4318",
    useHttpProtobuf: true);
```

## Traced Operations

All spans are emitted under the `IntelliTrader.Trading` ActivitySource.

### Trading Workflows

| Span Name              | Kind     | Key Attributes                                        |
|------------------------|----------|-------------------------------------------------------|
| `BuyOrder`             | Internal | `trading.pair`, `trading.price`, `trading.quantity`   |
| `SellOrder`            | Internal | `trading.pair`, `trading.price`, `trading.quantity`   |
| `SwapOrder`            | Internal | `trading.old_pair`, `trading.new_pair`                |
| `DCAOrder`             | Internal | `trading.pair`, `trading.dca_level`                   |
| `StopLoss`             | Internal | `trading.pair`, `trading.margin`                      |
| `ProcessTradingRules`  | Internal | `trading.pair_count`                                  |
| `ProcessPosition`      | Internal | `trading.pair`, `trading.margin`                      |
| `ProcessTrailing`      | Internal | `trading.pair`, `trading.trailing_type`               |

### Signal Processing

| Span Name              | Kind     | Key Attributes                                        |
|------------------------|----------|-------------------------------------------------------|
| `EvaluateSignals`      | Internal | `signal.name`, `signal.pair_count`                    |
| `MatchSignalRule`      | Internal | `trading.pair`, `rule.name`                           |

### Exchange API Calls

| Span Name              | Kind     | Key Attributes                                        |
|------------------------|----------|-------------------------------------------------------|
| `ExchangeApiCall`      | Client   | `exchange.operation`                                  |
| `FetchTickers`         | Client   | `exchange.operation`, `exchange.ticker_count`         |
| `PlaceOrder`           | Client   | `trading.pair`, `trading.side`, `trading.amount`      |

All spans support `otel.status_code` and `otel.status_description` via the
`TradingTelemetry.SetActivityResult()` helper.

## Interpreting Traces

### Finding a Trade Lifecycle

1. Open the Jaeger UI and select service **IntelliTrader**.
2. Search for operation `BuyOrder` or `SellOrder`.
3. Click a trace to see the full span waterfall.

A typical buy workflow trace looks like:

```
EvaluateSignals (signal polling)
  └── MatchSignalRule (rule evaluation per pair)
        └── BuyOrder (order decision)
              └── PlaceOrder (exchange API call)
```

### Key Things to Look For

- **Latency spikes** in `PlaceOrder` or `FetchTickers` spans indicate exchange API slowdowns.
- **Error status** (`otel.status_code=ERROR`) on any span highlights failures; check `otel.status_description` for details.
- **DCA chains**: Filter by `DCAOrder` to see dollar-cost-averaging sequences and their `trading.dca_level`.
- **Signal-to-trade correlation**: `EvaluateSignals` spans show how many pairs were evaluated (`signal.pair_count`). Child `MatchSignalRule` spans show which rules fired.

### Metrics (Prometheus)

In addition to traces, IntelliTrader exposes Prometheus metrics at `/metrics`:

- `trades.executed`, `trades.failed` -- trade counters by pair and type
- `order.latency` -- order execution latency histogram
- `exchange.api_latency` -- exchange API call latency
- `signals.processing_time` -- signal evaluation duration
- `positions.open`, `portfolio.value` -- live gauges

## Alternative Backends

### Grafana Tempo

Set the OTLP endpoint to your Tempo instance:

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo:4317
```

### Grafana Alloy / OpenTelemetry Collector

Deploy the [OpenTelemetry Collector](https://opentelemetry.io/docs/collector/) as a
sidecar or gateway and point IntelliTrader at it. The collector can fan out to
multiple backends (Jaeger + Prometheus + logging).

### Docker Compose Example

```yaml
services:
  jaeger:
    image: jaegertracing/all-in-one:1.58
    ports:
      - "16686:16686"
      - "4317:4317"
    environment:
      - COLLECTOR_OTLP_ENABLED=true

  intellitrader:
    build: .
    environment:
      - OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317
    depends_on:
      - jaeger
```
