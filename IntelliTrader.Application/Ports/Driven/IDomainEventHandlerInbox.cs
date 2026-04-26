namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Tracks processed domain events per handler to make event handling idempotent.
/// </summary>
public interface IDomainEventHandlerInbox
{
    Task<bool> HasProcessedAsync(
        Guid eventId,
        string handlerName,
        CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(
        Guid eventId,
        string handlerName,
        CancellationToken cancellationToken = default);
}
