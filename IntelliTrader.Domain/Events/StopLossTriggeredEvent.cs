using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when a stop loss is triggered for a position.
/// </summary>
public sealed record StopLossTriggeredEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// The trading pair (e.g., "BTCUSDT").
    /// </summary>
    public string Pair { get; }

    /// <summary>
    /// The price that triggered the stop loss.
    /// </summary>
    public decimal TriggerPrice { get; }

    /// <summary>
    /// The actual price at which the stop loss was executed.
    /// </summary>
    public decimal ExecutionPrice { get; }

    /// <summary>
    /// The configured stop loss price.
    /// </summary>
    public decimal StopLossPrice { get; }

    /// <summary>
    /// The margin at the time of trigger.
    /// </summary>
    public decimal MarginAtTrigger { get; }

    /// <summary>
    /// The estimated loss from this stop loss execution.
    /// </summary>
    public decimal EstimatedLoss { get; }

    /// <summary>
    /// The type of stop loss (Fixed, Trailing, ATRBased, etc.).
    /// </summary>
    public StopLossType StopLossType { get; }

    /// <summary>
    /// The DCA level of the position at trigger time.
    /// </summary>
    public int DCALevel { get; }

    public StopLossTriggeredEvent(
        string pair,
        decimal triggerPrice,
        decimal executionPrice,
        decimal stopLossPrice = 0,
        decimal marginAtTrigger = 0,
        decimal estimatedLoss = 0,
        StopLossType stopLossType = StopLossType.Fixed,
        int dcaLevel = 0,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
        Pair = pair;
        TriggerPrice = triggerPrice;
        ExecutionPrice = executionPrice;
        StopLossPrice = stopLossPrice;
        MarginAtTrigger = marginAtTrigger;
        EstimatedLoss = estimatedLoss;
        StopLossType = stopLossType;
        DCALevel = dcaLevel;
    }
}

/// <summary>
/// The type of stop loss.
/// </summary>
public enum StopLossType
{
    Fixed,
    Trailing,
    ATRBased,
    TimeDecay
}
