using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.BackgroundServices;

public sealed class DomainEventOutboxProcessorServiceTests
{
    [Fact]
    public async Task ExecuteWorkAsync_ProcessesPendingOutboxBatchWithConfiguredLimit()
    {
        var processorMock = new Mock<IDomainEventOutboxProcessor>();
        processorMock
            .Setup(x => x.ProcessPendingAsync(25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainEventOutboxProcessingResult(
                AttemptedCount: 2,
                ProcessedCount: 2,
                FailedCount: 0));
        var service = new DomainEventOutboxProcessorService(
            Mock.Of<ILogger<DomainEventOutboxProcessorService>>(),
            processorMock.Object,
            new DomainEventOutboxProcessorOptions
            {
                BatchSize = 25,
                Interval = TimeSpan.FromMilliseconds(50),
                StartDelay = TimeSpan.Zero
            });

        await InvokeExecuteWorkAsync(service);

        processorMock.Verify(
            x => x.ProcessPendingAsync(25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static async Task InvokeExecuteWorkAsync(DomainEventOutboxProcessorService service)
    {
        var method = typeof(DomainEventOutboxProcessorService).GetMethod(
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
