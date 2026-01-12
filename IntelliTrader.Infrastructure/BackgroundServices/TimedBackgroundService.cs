using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntelliTrader.Infrastructure.BackgroundServices;

/// <summary>
/// Base class for timed background services that execute work at regular intervals.
/// Provides high-resolution timing similar to the legacy HighResolutionTimedTask.
/// </summary>
public abstract class TimedBackgroundService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _startDelay;

    /// <summary>
    /// Gets the number of times the task has been executed.
    /// </summary>
    public long ExecutionCount { get; private set; }

    /// <summary>
    /// Gets the total time spent waiting for previous executions to complete.
    /// </summary>
    public TimeSpan TotalOverrunTime { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the service is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Creates a new TimedBackgroundService.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="interval">The interval between executions.</param>
    /// <param name="startDelay">Optional delay before the first execution.</param>
    protected TimedBackgroundService(
        ILogger logger,
        TimeSpan interval,
        TimeSpan? startDelay = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = interval;
        _startDelay = startDelay ?? TimeSpan.Zero;
    }

    /// <summary>
    /// The work to execute at each interval.
    /// </summary>
    protected abstract Task ExecuteWorkAsync(CancellationToken stoppingToken);

    /// <summary>
    /// Called when an unhandled exception occurs during execution.
    /// Override to provide custom error handling.
    /// </summary>
    protected virtual Task OnErrorAsync(Exception exception, CancellationToken stoppingToken)
    {
        _logger.LogError(exception, "Error during {ServiceName} execution", GetType().Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{ServiceName} starting with interval {Interval}ms",
            GetType().Name, _interval.TotalMilliseconds);

        if (_startDelay > TimeSpan.Zero)
        {
            await Task.Delay(_startDelay, stoppingToken);
        }

        IsRunning = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            var executionStart = DateTime.UtcNow;

            try
            {
                await ExecuteWorkAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                await OnErrorAsync(ex, stoppingToken);
            }

            ExecutionCount++;

            var executionTime = DateTime.UtcNow - executionStart;
            var delayTime = _interval - executionTime;

            if (delayTime > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delayTime, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            else
            {
                // Execution took longer than the interval
                TotalOverrunTime += executionTime - _interval;
                _logger.LogWarning("{ServiceName} execution took {ExecutionTime}ms, exceeding interval of {Interval}ms",
                    GetType().Name, executionTime.TotalMilliseconds, _interval.TotalMilliseconds);
            }
        }

        IsRunning = false;
        _logger.LogInformation("{ServiceName} stopped after {ExecutionCount} executions",
            GetType().Name, ExecutionCount);
    }
}
