using IntelliTrader.Application.Ports.Driven;
using Microsoft.Extensions.Logging;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Configuration for the DomainEventOutboxProcessorService.
/// </summary>
public sealed class DomainEventOutboxProcessorOptions
{
    /// <summary>
    /// Interval between outbox replay cycles. Default: 10 seconds.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delay before the first replay cycle. Default: 5 seconds.
    /// </summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum number of outbox messages to process in a cycle.
    /// </summary>
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Background service that periodically replays pending domain events from the durable outbox.
/// </summary>
public sealed class DomainEventOutboxProcessorService : TimedBackgroundService
{
    private readonly ILogger<DomainEventOutboxProcessorService> _logger;
    private readonly IDomainEventOutboxProcessor _processor;
    private readonly DomainEventOutboxProcessorOptions _options;

    public DomainEventOutboxProcessorService(
        ILogger<DomainEventOutboxProcessorService> logger,
        IDomainEventOutboxProcessor processor,
        DomainEventOutboxProcessorOptions? options = null)
        : base(logger, options?.Interval ?? TimeSpan.FromSeconds(10), options?.StartDelay)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _options = options ?? new DomainEventOutboxProcessorOptions();
    }

    protected override async Task ExecuteWorkAsync(CancellationToken stoppingToken)
    {
        var result = await _processor.ProcessPendingAsync(_options.BatchSize, stoppingToken)
            .ConfigureAwait(false);

        if (result.AttemptedCount == 0)
        {
            _logger.LogDebug("No pending outbox events to process");
            return;
        }

        _logger.LogInformation(
            "Processed outbox events. Attempted={AttemptedCount}, Processed={ProcessedCount}, Failed={FailedCount}",
            result.AttemptedCount,
            result.ProcessedCount,
            result.FailedCount);
    }
}
