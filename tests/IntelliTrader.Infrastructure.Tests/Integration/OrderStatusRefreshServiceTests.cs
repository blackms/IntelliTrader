using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class OrderStatusRefreshServiceTests
{
    [Fact]
    public async Task StartAsync_DispatchesBulkRefreshCommandOnSchedule()
    {
        // Arrange
        var dispatcherMock = new Mock<ICommandDispatcher>();
        dispatcherMock
            .Setup(x => x.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
                It.IsAny<RefreshActiveOrdersCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RefreshActiveOrdersResult>.Success(new RefreshActiveOrdersResult
            {
                TotalActive = 1,
                AttemptedCount = 1,
                RefreshedCount = 1,
                AppliedDomainEffectsCount = 1,
                FailedCount = 0
            }));

        var loggerMock = new Mock<ILogger<OrderStatusRefreshService>>();
        var service = new OrderStatusRefreshService(
            loggerMock.Object,
            dispatcherMock.Object,
            new OrderStatusRefreshOptions
            {
                Interval = TimeSpan.FromMilliseconds(50),
                StartDelay = TimeSpan.Zero,
                MaxOrdersPerCycle = 25
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(140);
        await service.StopAsync(CancellationToken.None);

        // Assert
        dispatcherMock.Verify(
            x => x.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
                It.Is<RefreshActiveOrdersCommand>(command => command.Limit == 25),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        service.ExecutionCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteWorkAsync_WhenRefreshFails_LogsWarningWithErrorDetails()
    {
        // Arrange
        var dispatcherMock = new Mock<ICommandDispatcher>();
        dispatcherMock
            .Setup(x => x.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
                It.IsAny<RefreshActiveOrdersCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RefreshActiveOrdersResult>.Failure(
                Error.ExchangeError("exchange unavailable")));

        var loggerMock = new Mock<ILogger<OrderStatusRefreshService>>();
        var service = new OrderStatusRefreshService(
            loggerMock.Object,
            dispatcherMock.Object,
            new OrderStatusRefreshOptions
            {
                MaxOrdersPerCycle = 25
            });

        // Act
        await InvokeExecuteWorkAsync(service);

        // Assert
        dispatcherMock.Verify(
            x => x.DispatchAsync<RefreshActiveOrdersCommand, RefreshActiveOrdersResult>(
                It.Is<RefreshActiveOrdersCommand>(command => command.Limit == 25),
                It.IsAny<CancellationToken>()),
            Times.Once);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("Failed to refresh active orders") &&
                    state.ToString()!.Contains("ExchangeError") &&
                    state.ToString()!.Contains("exchange unavailable")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static async Task InvokeExecuteWorkAsync(OrderStatusRefreshService service)
    {
        var method = typeof(OrderStatusRefreshService).GetMethod(
            "ExecuteWorkAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            await (Task)method.Invoke(service, [CancellationToken.None])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
