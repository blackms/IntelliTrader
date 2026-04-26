namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Replays pending domain events from the durable outbox.
/// </summary>
public interface IDomainEventOutboxProcessor
{
    Task<DomainEventOutboxProcessingResult> ProcessPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}

public sealed record DomainEventOutboxProcessingResult(
    int AttemptedCount,
    int ProcessedCount,
    int FailedCount);
