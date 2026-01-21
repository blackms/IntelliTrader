using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when trading is resumed.
/// </summary>
public sealed record TradingResumedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// Describes who or what resumed trading.
    /// </summary>
    public string ResumedBy { get; }

    /// <summary>
    /// Whether the previous suspension was forced.
    /// </summary>
    public bool WasForced { get; }

    /// <summary>
    /// The duration of the suspension.
    /// </summary>
    public TimeSpan SuspensionDuration { get; }

    /// <summary>
    /// The reason for the previous suspension.
    /// </summary>
    public SuspensionReason? PreviousSuspensionReason { get; }

    public TradingResumedEvent(
        string resumedBy,
        bool wasForced = false,
        TimeSpan? suspensionDuration = null,
        SuspensionReason? previousSuspensionReason = null,
        string? correlationId = null)
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTimeOffset.UtcNow;
        CorrelationId = correlationId;
        ResumedBy = resumedBy;
        WasForced = wasForced;
        SuspensionDuration = suspensionDuration ?? TimeSpan.Zero;
        PreviousSuspensionReason = previousSuspensionReason;
    }
}
