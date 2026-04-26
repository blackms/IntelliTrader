using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Events;

public sealed class InMemoryDomainEventDispatcherIdempotencyTests
{
    [Fact]
    public async Task DispatchAsync_WhenHandlerAlreadyProcessedEvent_SkipsHandler()
    {
        var domainEvent = new TestDomainEvent();
        var handler = new CountingDomainEventHandler();
        var inboxMock = new Mock<IDomainEventHandlerInbox>();
        inboxMock
            .Setup(x => x.HasProcessedAsync(
                domainEvent.EventId,
                It.Is<string>(handlerName => handlerName.Contains(nameof(CountingDomainEventHandler))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dispatcher = new InMemoryDomainEventDispatcher(
            Mock.Of<IServiceProvider>(),
            NullLogger<InMemoryDomainEventDispatcher>.Instance,
            inboxMock.Object);
        dispatcher.RegisterHandler(handler);

        await dispatcher.DispatchAsync(domainEvent);

        handler.HandleCount.Should().Be(0);
        inboxMock.Verify(
            x => x.MarkProcessedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private sealed record TestDomainEvent : DomainEvent;

    private sealed class CountingDomainEventHandler : IDomainEventHandler<TestDomainEvent>
    {
        public int HandleCount { get; private set; }

        public Task HandleAsync(TestDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            HandleCount++;
            return Task.CompletedTask;
        }
    }
}
