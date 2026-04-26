using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when the portfolio balance changes.
/// </summary>
public sealed record PortfolioBalanceChanged : IDomainEvent
{
    public Guid EventId { get; }
    public PortfolioId PortfolioId { get; }
    public Money PreviousTotal { get; }
    public Money NewTotal { get; }
    public Money PreviousAvailable { get; }
    public Money NewAvailable { get; }
    public string Reason { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? CorrelationId { get; }

    public PortfolioBalanceChanged(
        PortfolioId portfolioId,
        Money previousTotal,
        Money newTotal,
        Money previousAvailable,
        Money newAvailable,
        string reason,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        PortfolioId = portfolioId;
        PreviousTotal = previousTotal;
        NewTotal = newTotal;
        PreviousAvailable = previousAvailable;
        NewAvailable = newAvailable;
        Reason = reason;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
    }
}
