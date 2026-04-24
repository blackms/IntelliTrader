using System.Collections.Concurrent;
using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Core;
using IntelliTrader.Infrastructure.BackgroundServices;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class ActiveOrderRefreshServiceBehaviorTests
{
    [Fact]
    public async Task Start_WhenCalledTwiceWhileRunning_DoesNotStartSecondLoop()
    {
        var loggingServiceMock = new Mock<ILoggingService>();
        var dispatcherMock = new Mock<ICommandDispatcher>();
        var firstDispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedSecondDispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new ConcurrentQueue<RefreshActiveOrdersCommand>();
        var dispatchCount = 0;

        dispatcherMock
            .Setup(x => x.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
                It.IsAny<RefreshActiveOrdersCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshActiveOrdersCommand command, CancellationToken _) =>
            {
                commands.Enqueue(command);
                var currentCount = Interlocked.Increment(ref dispatchCount);
                if (currentCount == 1)
                {
                    firstDispatch.TrySetResult(true);
                }
                else
                {
                    unexpectedSecondDispatch.TrySetResult(true);
                }

                return SuccessfulRefresh();
            });

        var service = new ActiveOrderRefreshService(
            loggingServiceMock.Object,
            dispatcherMock.Object,
            new OrderStatusRefreshOptions
            {
                Interval = TimeSpan.FromSeconds(30),
                StartDelay = TimeSpan.Zero,
                MaxOrdersPerCycle = 17
            });

        try
        {
            service.Start();
            await firstDispatch.Task.WaitAsync(TimeSpan.FromSeconds(1));

            service.Start();

            var completedTask = await Task.WhenAny(
                unexpectedSecondDispatch.Task,
                Task.Delay(TimeSpan.FromMilliseconds(150)));

            completedTask.Should().NotBe(unexpectedSecondDispatch.Task);
            dispatchCount.Should().Be(1);
            commands.Should().ContainSingle()
                .Which.Limit.Should().Be(17);

            InfoMessages(loggingServiceMock).Should().Equal(
                "Start active order refresh service...",
                "Active order refresh service started");
        }
        finally
        {
            service.Stop();
        }
    }

    [Fact]
    public async Task Start_WhenStoppedAndStartedAgain_DispatchesNewRefreshCyclePerLifecycle()
    {
        var loggingServiceMock = new Mock<ILoggingService>();
        var dispatcherMock = new Mock<ICommandDispatcher>();
        var firstDispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDispatch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new ConcurrentQueue<RefreshActiveOrdersCommand>();
        var dispatchCount = 0;

        dispatcherMock
            .Setup(x => x.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
                It.IsAny<RefreshActiveOrdersCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshActiveOrdersCommand command, CancellationToken _) =>
            {
                commands.Enqueue(command);
                var currentCount = Interlocked.Increment(ref dispatchCount);
                if (currentCount == 1)
                {
                    firstDispatch.TrySetResult(true);
                }
                else if (currentCount == 2)
                {
                    secondDispatch.TrySetResult(true);
                }

                return SuccessfulRefresh();
            });

        var service = new ActiveOrderRefreshService(
            loggingServiceMock.Object,
            dispatcherMock.Object,
            new OrderStatusRefreshOptions
            {
                Interval = TimeSpan.FromSeconds(30),
                StartDelay = TimeSpan.Zero,
                MaxOrdersPerCycle = 23
            });

        try
        {
            service.Start();
            await firstDispatch.Task.WaitAsync(TimeSpan.FromSeconds(1));
            service.Stop();

            service.Start();
            await secondDispatch.Task.WaitAsync(TimeSpan.FromSeconds(1));

            dispatchCount.Should().Be(2);
            commands.Should().HaveCount(2);
            commands.Select(command => command.Limit).Should().OnlyContain(limit => limit == 23);

            InfoMessages(loggingServiceMock).Should().Equal(
                "Start active order refresh service...",
                "Active order refresh service started",
                "Stop active order refresh service...",
                "Active order refresh service stopped",
                "Start active order refresh service...",
                "Active order refresh service started");
        }
        finally
        {
            service.Stop();
        }

        InfoMessages(loggingServiceMock).Should().Equal(
            "Start active order refresh service...",
            "Active order refresh service started",
            "Stop active order refresh service...",
            "Active order refresh service stopped",
            "Start active order refresh service...",
            "Active order refresh service started",
            "Stop active order refresh service...",
            "Active order refresh service stopped");
    }

    [Fact]
    public void Stop_WhenServiceWasNotStarted_IsNoOp()
    {
        var loggingServiceMock = new Mock<ILoggingService>();
        var dispatcherMock = new Mock<ICommandDispatcher>();
        var service = new ActiveOrderRefreshService(loggingServiceMock.Object, dispatcherMock.Object);

        service.Stop();

        loggingServiceMock.Invocations.Should().BeEmpty();
        dispatcherMock.Invocations.Should().BeEmpty();
    }

    private static IReadOnlyList<string> InfoMessages(Mock<ILoggingService> loggingServiceMock)
    {
        return loggingServiceMock.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILoggingService.Info))
            .Select(invocation => invocation.Arguments.FirstOrDefault())
            .OfType<string>()
            .ToList();
    }

    private static Result<RefreshActiveOrdersResult> SuccessfulRefresh()
    {
        return Result<RefreshActiveOrdersResult>.Success(new RefreshActiveOrdersResult
        {
            TotalActive = 1,
            AttemptedCount = 1,
            RefreshedCount = 1,
            AppliedDomainEffectsCount = 0,
            FailedCount = 0
        });
    }
}
