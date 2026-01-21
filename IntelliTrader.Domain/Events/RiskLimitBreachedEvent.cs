using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when a risk limit is breached.
/// </summary>
public sealed record RiskLimitBreachedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// The type of limit that was breached.
    /// </summary>
    public RiskLimitType LimitType { get; }

    /// <summary>
    /// The current value that triggered the breach.
    /// </summary>
    public decimal CurrentValue { get; }

    /// <summary>
    /// The maximum allowed value for this limit.
    /// </summary>
    public decimal MaxValue { get; }

    /// <summary>
    /// A description of the breach.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// The severity of the risk breach.
    /// </summary>
    public RiskSeverity Severity { get; }

    /// <summary>
    /// The trading pair associated with this breach, if applicable.
    /// </summary>
    public string? Pair { get; }

    public RiskLimitBreachedEvent(
        RiskLimitType limitType,
        decimal currentValue,
        decimal maxValue,
        string? description = null,
        RiskSeverity severity = RiskSeverity.Warning,
        string? pair = null,
        string? correlationId = null)
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTimeOffset.UtcNow;
        CorrelationId = correlationId;
        LimitType = limitType;
        CurrentValue = currentValue;
        MaxValue = maxValue;
        Description = description ?? $"{limitType} breached: {currentValue:F2} exceeds {maxValue:F2}";
        Severity = severity;
        Pair = pair;
    }
}

/// <summary>
/// The type of risk limit.
/// </summary>
public enum RiskLimitType
{
    PortfolioHeat,
    MaxPositions,
    PositionSize,
    DailyLoss,
    Drawdown,
    Exposure,
    CircuitBreaker
}

/// <summary>
/// The severity of a risk event.
/// </summary>
public enum RiskSeverity
{
    Warning,
    Critical,
    Emergency
}
