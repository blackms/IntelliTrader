using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging;
using Moq;
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
            .Setup(x => x.DispatchAsync<RefreshSubmittedOrdersCommand, RefreshSubmittedOrdersResult>(
                It.IsAny<RefreshSubmittedOrdersCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RefreshSubmittedOrdersResult>.Success(new RefreshSubmittedOrdersResult
            {
                TotalSubmitted = 1,
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
            x => x.DispatchAsync<RefreshSubmittedOrdersCommand, RefreshSubmittedOrdersResult>(
                It.Is<RefreshSubmittedOrdersCommand>(command => command.Limit == 25),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        service.ExecutionCount.Should().BeGreaterThan(0);
    }
}
