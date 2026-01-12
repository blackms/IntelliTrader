using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when a position is added to the portfolio.
/// </summary>
public sealed record PositionAddedToPortfolio : IDomainEvent
{
    public PortfolioId PortfolioId { get; }
    public PositionId PositionId { get; }
    public TradingPair Pair { get; }
    public Money Cost { get; }
    public int ActivePositionCount { get; }
    public DateTimeOffset OccurredAt { get; }

    public PositionAddedToPortfolio(
        PortfolioId portfolioId,
        PositionId positionId,
        TradingPair pair,
        Money cost,
        int activePositionCount)
    {
        PortfolioId = portfolioId;
        PositionId = positionId;
        Pair = pair;
        Cost = cost;
        ActivePositionCount = activePositionCount;
        OccurredAt = DateTimeOffset.UtcNow;
    }
}
