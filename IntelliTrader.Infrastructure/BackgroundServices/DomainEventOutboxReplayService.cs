using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Core-facing lifecycle manager for durable outbox replay background work.
/// </summary>
public sealed class DomainEventOutboxReplayService : IDomainEventOutboxReplayService
{
    private readonly ILoggingService _loggingService;
    private readonly IDomainEventOutboxProcessor _processor;
    private readonly DomainEventOutboxProcessorOptions _options;
    private CancellationTokenSource? _cts;
    private DomainEventOutboxProcessorService? _backgroundService;

    public DomainEventOutboxReplayService(
        ILoggingService loggingService,
        IDomainEventOutboxProcessor processor)
        : this(loggingService, processor, new DomainEventOutboxProcessorOptions())
    {
    }

    public DomainEventOutboxReplayService(
        ILoggingService loggingService,
        IDomainEventOutboxProcessor processor,
        DomainEventOutboxProcessorOptions options)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _loggingService.Info("Start domain event outbox replay service...");

        _cts = new CancellationTokenSource();
        _backgroundService = new DomainEventOutboxProcessorService(
            NullLogger<DomainEventOutboxProcessorService>.Instance,
            _processor,
            _options);

        _backgroundService.StartAsync(_cts.Token).GetAwaiter().GetResult();

        _loggingService.Info("Domain event outbox replay service started");
    }

    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        _loggingService.Info("Stop domain event outbox replay service...");

        _cts.Cancel();
        _backgroundService?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _cts.Dispose();
        _backgroundService?.Dispose();

        _cts = null;
        _backgroundService = null;

        _loggingService.Info("Domain event outbox replay service stopped");
    }
}
