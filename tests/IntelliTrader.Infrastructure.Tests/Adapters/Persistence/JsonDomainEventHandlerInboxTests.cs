using FluentAssertions;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class JsonDomainEventHandlerInboxTests
{
    [Fact]
    public async Task MarkProcessedAsync_WhenReloaded_RemembersEventPerHandler()
    {
        var inboxPath = CreateInboxPath();
        var eventId = Guid.NewGuid();

        try
        {
            await using (var inbox = new JsonDomainEventHandlerInbox(inboxPath))
            {
                await inbox.MarkProcessedAsync(eventId, "HandlerA");
            }

            await using var reloadedInbox = new JsonDomainEventHandlerInbox(inboxPath);

            (await reloadedInbox.HasProcessedAsync(eventId, "HandlerA")).Should().BeTrue();
            (await reloadedInbox.HasProcessedAsync(eventId, "HandlerB")).Should().BeFalse();
        }
        finally
        {
            DeleteFileIfExists(inboxPath);
        }
    }

    private static string CreateInboxPath()
    {
        return Path.Combine(Path.GetTempPath(), $"domain_event_handler_inbox_{Guid.NewGuid():N}.json");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
