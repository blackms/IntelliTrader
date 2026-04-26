using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Events;
using Moq;
using Xunit;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;

namespace IntelliTrader.Infrastructure.Tests.Events;

public sealed class DomainEventOutboxProcessorTests
{
    [Fact]
    public async Task ProcessPendingAsync_WhenOrderFilledEventIsPending_DispatchesAndMarksProcessed()
    {
        var outboxPath = CreateOutboxPath();
        using var outbox = new JsonDomainEventOutbox(outboxPath);
        var domainEvent = CreateOrderFilledEvent("outbox-fill-1");
        var dispatchedEvents = new List<IDomainEvent>();
        var dispatcherMock = new Mock<IDomainEventDispatcher>();
        dispatcherMock
            .Setup(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IDomainEvent, CancellationToken>((evt, _) => dispatchedEvents.Add(evt))
            .Returns(Task.CompletedTask);
        var processor = new DomainEventOutboxProcessor(outbox, dispatcherMock.Object);

        try
        {
            await outbox.EnqueueAsync([domainEvent]);

            var result = await processor.ProcessPendingAsync(batchSize: 10);

            result.ProcessedCount.Should().Be(1);
            result.FailedCount.Should().Be(0);
            dispatchedEvents.Should().ContainSingle();
            dispatchedEvents.Single().Should().BeOfType<OrderFilledEvent>()
                .Which.Should().Match<OrderFilledEvent>(evt =>
                    evt.OrderId == "outbox-fill-1" &&
                    evt.Pair == "BTCUSDT" &&
                    evt.Side == DomainOrderSide.Buy &&
                    evt.FilledAmount == 0.25m &&
                    evt.AveragePrice == 100m &&
                    evt.Cost == 25m &&
                    evt.Fees == 0.01m &&
                    evt.IsPartialFill);
            (await outbox.GetUnprocessedAsync()).Should().BeEmpty();
        }
        finally
        {
            DeleteFileIfExists(outboxPath);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatchFails_LeavesEventPendingAndReportsFailure()
    {
        var outboxPath = CreateOutboxPath();
        using var outbox = new JsonDomainEventOutbox(outboxPath);
        var domainEvent = CreateOrderFilledEvent("outbox-fill-fails-1");
        var dispatcherMock = new Mock<IDomainEventDispatcher>();
        dispatcherMock
            .Setup(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("handler unavailable"));
        var processor = new DomainEventOutboxProcessor(outbox, dispatcherMock.Object);

        try
        {
            await outbox.EnqueueAsync([domainEvent]);

            var result = await processor.ProcessPendingAsync(batchSize: 10);

            result.AttemptedCount.Should().Be(1);
            result.ProcessedCount.Should().Be(0);
            result.FailedCount.Should().Be(1);
            var pendingMessages = await outbox.GetUnprocessedAsync();
            pendingMessages.Should().ContainSingle()
                .Which.EventId.Should().Be(domainEvent.EventId);
        }
        finally
        {
            DeleteFileIfExists(outboxPath);
        }
    }

    private static OrderFilledEvent CreateOrderFilledEvent(string orderId)
    {
        return new OrderFilledEvent(
            orderId,
            "BTCUSDT",
            DomainOrderSide.Buy,
            filledAmount: 0.25m,
            averagePrice: 100m,
            cost: 25m,
            fees: 0.01m,
            isPartialFill: true,
            correlationId: "corr-1");
    }

    private static string CreateOutboxPath()
    {
        return Path.Combine(Path.GetTempPath(), $"domain_event_outbox_processor_{Guid.NewGuid():N}.json");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
