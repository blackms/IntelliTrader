# ADR-0009: ASP.NET Core Web Dashboard with SignalR Real-Time Updates

## Status
Accepted

## Context
Traders need real-time visibility into:

1. Current positions and their profit/loss margins
2. Active trailing orders (buy and sell)
3. Signal ratings and global market sentiment
4. Health status of exchange connections
5. Order history and trade execution logs

Polling-based dashboards create unnecessary load and provide delayed data. In fast-moving crypto markets, seeing prices 3-5 seconds old can mean missing profitable exits or seeing phantom profits.

## Decision
We implemented an ASP.NET Core web dashboard with SignalR for push-based real-time updates. The dashboard bridges Autofac services to ASP.NET Core DI and broadcasts updates via a background service.

Startup configuration from `Startup.cs`:
```csharp
public class Startup
{
    public static ILifetimeScope Container { get; set; }  // Autofac container

    public void ConfigureServices(IServiceCollection services)
    {
        // Bridge Autofac services to ASP.NET Core DI
        services.AddSingleton(_ => Container.Resolve<ICoreService>());
        services.AddSingleton(_ => Container.Resolve<ITradingService>());
        services.AddSingleton(_ => Container.Resolve<ISignalsService>());
        services.AddSingleton(_ => Container.Resolve<IHealthCheckService>());

        // SignalR notifier for broadcasting updates
        services.AddSingleton<ITradingHubNotifier, TradingHubNotifier>();

        // Background service for periodic broadcasts
        services.AddHostedService<SignalRBroadcasterService>();

        services.AddAuthentication(...)
            .AddCookie(options => options.LoginPath = "/Login");

        services.AddControllersWithViews();
        services.AddSignalR();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapMinimalApiEndpoints();  // Data-only APIs
            endpoints.MapControllerRoute(...);   // MVC views
            endpoints.MapHub<TradingHub>("/trading-hub");  // SignalR
        });
    }
}
```

TradingHub for real-time updates from `TradingHub.cs`:
```csharp
[Authorize]
public class TradingHub : Hub
{
    public const string TradingUpdatesGroup = "TradingUpdates";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TradingUpdatesGroup);
        await SendInitialStatusAsync();  // Immediate data on connect
        await base.OnConnectedAsync();
    }

    public async Task SubscribeToPair(string pair)
    {
        var normalizedPair = pair.ToUpperInvariant();
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Pair_{normalizedPair}");
        await SendPairPriceAsync(normalizedPair);  // Immediate price
    }

    public async Task RequestStatus()
    {
        await Clients.Caller.SendAsync("StatusUpdate", new
        {
            Balance = _tradingService.Account?.GetBalance(),
            GlobalRating = _signalsService.GetGlobalRating(),
            TrailingBuys = _tradingService.GetTrailingBuys(),
            TrailingSells = _tradingService.GetTrailingSells(),
            TradingSuspended = _tradingService.IsTradingSuspended,
            HealthChecks = _healthCheckService.GetHealthChecks(),
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
```

Background broadcaster service:
```csharp
public class SignalRBroadcasterService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _hubNotifier.BroadcastStatusUpdateAsync();
            await Task.Delay(1000, stoppingToken);  // 1 second interval
        }
    }
}
```

## Alternatives Considered

1. **Polling-Based REST API**
   - Pros: Simpler implementation, stateless
   - Cons: Unnecessary load, delayed updates, wasted bandwidth
   - Rejected: Real-time trading requires push-based updates

2. **Blazor Server**
   - Pros: C# everywhere, no JavaScript
   - Cons: Heavier SignalR usage, less ecosystem support
   - Rejected: Traditional MVC + SignalR is more flexible

3. **Standalone React/Vue SPA**
   - Pros: Modern UI frameworks, better interactivity
   - Cons: Separate build pipeline, API-first architecture needed
   - Rejected: MVC views are sufficient for our needs

4. **WebSocket Direct (No SignalR)**
   - Pros: Lower-level control, no abstraction overhead
   - Cons: No automatic reconnection, message framing, groups
   - Rejected: SignalR provides essential features out of the box

## Consequences

### Positive
- **Real-Time Updates**: Price changes, order fills pushed within 1 second
- **Reduced Load**: No polling; updates only sent when data changes
- **Selective Subscriptions**: Clients subscribe to specific pairs for targeted updates
- **Authentication**: Cookie-based auth protects trading operations
- **Initial State**: Clients receive full status on connection
- **OpenTelemetry**: Observability integration for tracing and metrics

### Negative
- **Connection State**: SignalR requires connection management (reconnect logic)
- **Memory Usage**: Each connected client maintains hub state
- **Security Surface**: WebSocket connections need careful authorization
- **Debugging**: Real-time flows harder to trace than request/response

## How to Validate

1. **Real-Time Price Updates**:
   ```javascript
   const connection = new signalR.HubConnectionBuilder()
       .withUrl("/trading-hub")
       .build();
   connection.on("PriceUpdate", (data) => console.log(data));
   await connection.start();
   await connection.invoke("SubscribeToPair", "BTCUSDT");
   // Verify PriceUpdate received within 1-2 seconds of exchange update
   ```

2. **Authentication Enforcement**:
   ```bash
   # Attempt to connect to /trading-hub without login
   # Verify: 401 Unauthorized response
   ```

3. **Status Broadcast**:
   ```javascript
   connection.on("StatusUpdate", (status) => {
       assert(status.Balance !== undefined);
       assert(status.GlobalRating !== undefined);
       assert(status.Timestamp !== undefined);
   });
   ```

4. **Connection Recovery**:
   ```javascript
   // Simulate network disconnect
   // Verify SignalR auto-reconnects and receives updates
   ```

## References
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Web/Startup.cs` - Web host configuration
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Web/Hubs/TradingHub.cs` - SignalR hub implementation
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Web/BackgroundServices/SignalRBroadcasterService.cs` - Periodic broadcast service
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader.Web/Services/TradingHubNotifier.cs` - Hub notifier service
- `/Users/alessiorocchi/Projects/IntelliTrader/IntelliTrader/config/web.json` - Web configuration
