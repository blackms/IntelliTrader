using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Core-facing lifecycle manager for submitted-order refresh background work.
/// Creates a fresh <see cref="OrderStatusRefreshService"/> instance on each start
/// because <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> instances are
/// not intended to be restarted after stop.
/// </summary>
public sealed class SubmittedOrderRefreshService : ISubmittedOrderRefreshService
{
    private readonly ILoggingService _loggingService;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly OrderStatusRefreshOptions _options;
    private CancellationTokenSource? _cts;
    private OrderStatusRefreshService? _backgroundService;

    public SubmittedOrderRefreshService(
        ILoggingService loggingService,
        ICommandDispatcher commandDispatcher)
        : this(loggingService, commandDispatcher, new OrderStatusRefreshOptions())
    {
    }

    public SubmittedOrderRefreshService(
        ILoggingService loggingService,
        ICommandDispatcher commandDispatcher,
        OrderStatusRefreshOptions options)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _loggingService.Info("Start submitted order refresh service...");

        _cts = new CancellationTokenSource();
        _backgroundService = new OrderStatusRefreshService(
            NullLogger<OrderStatusRefreshService>.Instance,
            _commandDispatcher,
            _options);

        _backgroundService.StartAsync(_cts.Token).GetAwaiter().GetResult();

        _loggingService.Info("Submitted order refresh service started");
    }

    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        _loggingService.Info("Stop submitted order refresh service...");

        _cts.Cancel();
        _backgroundService?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _cts.Dispose();
        _backgroundService?.Dispose();

        _cts = null;
        _backgroundService = null;

        _loggingService.Info("Submitted order refresh service stopped");
    }
}
