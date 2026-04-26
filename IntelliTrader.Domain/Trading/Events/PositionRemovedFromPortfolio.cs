using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when a position is removed from the portfolio (closed).
/// </summary>
public sealed record PositionRemovedFromPortfolio : IDomainEvent
{
    public Guid EventId { get; }
    public PortfolioId PortfolioId { get; }
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public Money Proceeds { get; }
    public Money PnL { get; }
    public int ActivePositionCount { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? CorrelationId { get; }

    public PositionRemovedFromPortfolio(
        PortfolioId portfolioId,
        PositionId positionId,
        TradingPair pair,
        Money proceeds,
        Money pnl,
        int activePositionCount,
        string? correlationId = null,
        Guid eventId = default,
        DateTimeOffset occurredAt = default)
    {
        EventId = eventId == Guid.Empty ? Guid.NewGuid() : eventId;
        PortfolioId = portfolioId;
        PositionId = positionId;
        Pair = pair;
        Proceeds = proceeds;
        PnL = pnl;
        ActivePositionCount = activePositionCount;
        OccurredAt = occurredAt == default ? DateTimeOffset.UtcNow : occurredAt;
        CorrelationId = correlationId;
    }
}
