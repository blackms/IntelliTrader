using Autofac;
using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Infrastructure.Events;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class DomainEventHandlerInboxRegistrationTests
{
    [Fact]
    public async Task BuildContainer_DispatcherSkipsDuplicateEventForSameHandler()
    {
        var inboxPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "handler-inbox.json");
        var handler = new CountingDomainEventHandler();
        var domainEvent = new TestDomainEvent();
        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());

        try
        {
            DeleteFileIfExists(inboxPath);
            using var container = builder.Build();
            var dispatcher = container.Resolve<IDomainEventDispatcher>();
            var inMemoryDispatcher = dispatcher.Should().BeOfType<InMemoryDomainEventDispatcher>().Subject;
            inMemoryDispatcher.RegisterHandler(handler);

            await dispatcher.DispatchAsync(domainEvent);
            await dispatcher.DispatchAsync(domainEvent);

            handler.HandleCount.Should().Be(1);
        }
        finally
        {
            DeleteFileIfExists(inboxPath);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
