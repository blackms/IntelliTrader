using System.Text.Json;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntelliTrader.Infrastructure.Events;

/// <summary>
/// Processes durable outbox messages that were not dispatched on the command path.
/// </summary>
public sealed class DomainEventOutboxProcessor : IDomainEventOutboxProcessor
{
    private readonly IDomainEventOutbox _eventOutbox;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<DomainEventOutboxProcessor> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DomainEventOutboxProcessor(
        IDomainEventOutbox eventOutbox,
        IDomainEventDispatcher eventDispatcher,
        ILogger<DomainEventOutboxProcessor>? logger = null)
    {
        _eventOutbox = eventOutbox ?? throw new ArgumentNullException(nameof(eventOutbox));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        _logger = logger ?? NullLogger<DomainEventOutboxProcessor>.Instance;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<DomainEventOutboxProcessingResult> ProcessPendingAsync(
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");

        var pendingMessages = await _eventOutbox.GetUnprocessedAsync(batchSize, cancellationToken)
            .ConfigureAwait(false);

        var processedCount = 0;
        var failedCount = 0;
        foreach (var message in pendingMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var domainEvent = Deserialize(message);
                await _eventDispatcher.DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
                await _eventOutbox.MarkProcessedAsync(message.EventId, cancellationToken).ConfigureAwait(false);
                processedCount++;

                _logger.LogDebug(
                    "Processed outbox event {EventType} with ID {EventId}",
                    message.EventType,
                    message.EventId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(
                    ex,
                    "Failed to process outbox event {EventType} with ID {EventId}; leaving it pending for retry",
                    message.EventType,
                    message.EventId);
            }
        }

        return new DomainEventOutboxProcessingResult(
            pendingMessages.Count,
            processedCount,
            failedCount);
    }

    private IDomainEvent Deserialize(DomainEventOutboxMessage message)
    {
        var eventType = Type.GetType(message.EventType, throwOnError: false);
        if (eventType is null)
        {
            throw new InvalidOperationException(
                $"Outbox event type '{message.EventType}' could not be resolved.");
        }

        if (!typeof(IDomainEvent).IsAssignableFrom(eventType))
        {
            throw new InvalidOperationException(
                $"Outbox event type '{message.EventType}' does not implement {nameof(IDomainEvent)}.");
        }

        var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType, _jsonOptions) as IDomainEvent;
        return domainEvent
               ?? throw new InvalidOperationException(
                   $"Outbox event {message.EventId} payload could not be deserialized.");
    }
}
