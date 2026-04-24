using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using Microsoft.Extensions.Logging;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Configuration for the OrderStatusRefreshService.
/// </summary>
public sealed class OrderStatusRefreshOptions
{
    /// <summary>
    /// Interval between refresh cycles. Default: 5 seconds.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Delay before the first refresh cycle. Default: 0 seconds.
    /// </summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Maximum number of active orders to refresh per cycle.
    /// </summary>
    public int MaxOrdersPerCycle { get; set; } = 50;
}

/// <summary>
/// Background service that periodically refreshes active, non-terminal orders.
/// </summary>
public sealed class OrderStatusRefreshService : TimedBackgroundService
{
    private readonly ILogger<OrderStatusRefreshService> _logger;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly OrderStatusRefreshOptions _options;

    public OrderStatusRefreshService(
        ILogger<OrderStatusRefreshService> logger,
        ICommandDispatcher commandDispatcher,
        OrderStatusRefreshOptions? options = null)
        : base(logger, options?.Interval ?? TimeSpan.FromSeconds(5), options?.StartDelay)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _commandDispatcher = commandDispatcher ?? throw new ArgumentNullException(nameof(commandDispatcher));
        _options = options ?? new OrderStatusRefreshOptions();
    }

    protected override async Task ExecuteWorkAsync(CancellationToken stoppingToken)
    {
        var result = await _commandDispatcher.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
            new RefreshActiveOrdersCommand
            {
                Limit = _options.MaxOrdersPerCycle
            },
            stoppingToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to refresh active orders: {ErrorCode} {ErrorMessage}",
                result.Error.Code,
                result.Error.Message);
            return;
        }

        _logger.LogDebug(
            "Refreshed active orders. Active={TotalActive}, Attempted={Attempted}, Refreshed={Refreshed}, Applied={Applied}, Failed={Failed}",
            result.Value.TotalActive,
            result.Value.AttemptedCount,
            result.Value.RefreshedCount,
            result.Value.AppliedDomainEffectsCount,
            result.Value.FailedCount);
    }
}
