using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when trading is suspended.
/// </summary>
public sealed record TradingSuspendedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// The reason for suspension.
    /// </summary>
    public SuspensionReason Reason { get; }

    /// <summary>
    /// Describes who or what suspended trading.
    /// </summary>
    public string SuspendedBy { get; }

    /// <summary>
    /// Whether this is a forced suspension (cannot be overridden).
    /// </summary>
    public bool IsForced { get; }

    /// <summary>
    /// The number of open positions at suspension time.
    /// </summary>
    public int OpenPositions { get; }

    /// <summary>
    /// The number of pending orders at suspension time.
    /// </summary>
    public int PendingOrders { get; }

    /// <summary>
    /// Additional details about the suspension.
    /// </summary>
    public string? Details { get; }

    public TradingSuspendedEvent(
        SuspensionReason reason,
        string suspendedBy,
        bool isForced = false,
        int openPositions = 0,
        int pendingOrders = 0,
        string? details = null,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
        Reason = reason;
        SuspendedBy = suspendedBy;
        IsForced = isForced;
        OpenPositions = openPositions;
        PendingOrders = pendingOrders;
        Details = details;
    }
}

/// <summary>
/// The reason for trading suspension.
/// </summary>
public enum SuspensionReason
{
    Manual,
    CircuitBreaker,
    DailyLossLimit,
    SystemError,
    ExchangeError,
    MaintenanceWindow,
    RiskLimitExceeded
}
