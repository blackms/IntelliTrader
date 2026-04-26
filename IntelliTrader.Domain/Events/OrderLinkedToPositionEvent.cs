using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when an order lifecycle is linked to the position it affected.
/// </summary>
public sealed record OrderLinkedToPositionEvent : IDomainEvent
{
    public Guid EventId { get; }

    public DateTimeOffset OccurredAt { get; }

    public string? CorrelationId { get; }

    public string OrderId { get; }

    public string PositionId { get; }

    public OrderLinkedToPositionEvent(
        string orderId,
        string positionId,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
        OrderId = orderId;
        PositionId = positionId;
    }
}
