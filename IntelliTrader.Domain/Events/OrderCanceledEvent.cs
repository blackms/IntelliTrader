using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when an exchange order reaches a canceled terminal state.
/// </summary>
public sealed record OrderCanceledEvent : IDomainEvent
{
    public Guid EventId { get; }

    public DateTimeOffset OccurredAt { get; }

    public string? CorrelationId { get; }

    public string OrderId { get; }

    public string Pair { get; }

    public OrderSide Side { get; }

    public OrderCanceledEvent(
        string orderId,
        string pair,
        OrderSide side,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
        OrderId = orderId;
        Pair = pair;
        Side = side;
    }
}
