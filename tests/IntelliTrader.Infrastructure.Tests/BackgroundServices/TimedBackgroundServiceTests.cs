using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.BackgroundServices;

public class TimedBackgroundServiceTests
{
    private readonly Mock<ILogger> _loggerMock;

    public TimedBackgroundServiceTests()
    {
        _loggerMock = new Mock<ILogger>();
    }

    [Fact]
    public async Task ExecuteAsync_RunsWorkAtInterval()
    {
        // Arrange
        var executionCount = 0;
        var service = new TestTimedBackgroundService(
            _loggerMock.Object,
            TimeSpan.FromMilliseconds(50),
            () =>
            {
                executionCount++;
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(175); // Wait for a few executions
        await service.StopAsync(CancellationToken.None);

        // Assert
        executionCount.Should().BeGreaterThanOrEqualTo(2);
        service.ExecutionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsStartDelay()
    {
        // Arrange
        var executionCount = 0;
        var service = new TestTimedBackgroundService(
            _loggerMock.Object,
            TimeSpan.FromMilliseconds(50),
            () =>
            {
                executionCount++;
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        // Act
        await service.StartAsync(cts.Token);
        try
        {
            await Task.Delay(70, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - should not have executed yet due to start delay
        executionCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesExceptions()
    {
        // Arrange
        var executionCount = 0;
        var service = new TestTimedBackgroundService(
            _loggerMock.Object,
            TimeSpan.FromMilliseconds(50),
            () =>
            {
                executionCount++;
                if (executionCount == 1)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return Task.CompletedTask;
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(175);
        await service.StopAsync(CancellationToken.None);

        // Assert - should continue after exception
        executionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_StopsGracefullyOnCancellation()
    {
        // Arrange
        var service = new TestTimedBackgroundService(
            _loggerMock.Object,
            TimeSpan.FromMilliseconds(1000),
            () => Task.CompletedTask);

        using var cts = new CancellationTokenSource();

        // Act
        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        // Assert
        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task IsRunning_ReflectsServiceState()
    {
        // Arrange
        var service = new TestTimedBackgroundService(
            _loggerMock.Object,
            TimeSpan.FromMilliseconds(100),
            () => Task.CompletedTask);

        // Assert initial state
        service.IsRunning.Should().BeFalse();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act - start
        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        // Assert running
        service.IsRunning.Should().BeTrue();

        // Act - stop
        await service.StopAsync(CancellationToken.None);
        await Task.Delay(50);

        // Assert stopped
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task TotalOverrunTime_TracksSlowExecutions()
    {
        // Arrange
        var executionTime = TimeSpan.FromMilliseconds(100);
        var service = new TestTimedBackgroundService(
            _loggerMock.Object,
            TimeSpan.FromMilliseconds(50), // Interval shorter than execution
            async () => await Task.Delay(executionTime));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(250);
        await service.StopAsync(CancellationToken.None);

        // Assert - should have tracked overrun time
        service.TotalOverrunTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    /// <summary>
    /// Test implementation of TimedBackgroundService for testing.
    /// </summary>
    private sealed class TestTimedBackgroundService : Infrastructure.BackgroundServices.TimedBackgroundService
    {
        private readonly Func<Task> _work;

        public TestTimedBackgroundService(
            ILogger logger,
            TimeSpan interval,
            Func<Task> work,
            TimeSpan? startDelay = null)
            : base(logger, interval, startDelay)
        {
            _work = work;
        }

        protected override Task ExecuteWorkAsync(CancellationToken stoppingToken)
        {
            return _work();
        }
    }
}
