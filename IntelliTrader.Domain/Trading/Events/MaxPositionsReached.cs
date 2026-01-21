using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when the maximum number of positions is reached.
/// </summary>
public sealed record MaxPositionsReached : IDomainEvent
{
    public Guid EventId { get; }
    public PortfolioId PortfolioId { get; }
    public int MaxPositions { get; }
    public int CurrentPositions { get; }
    public DateTimeOffset OccurredAt { get; }
    public string? CorrelationId { get; }

    public MaxPositionsReached(
        PortfolioId portfolioId,
        int maxPositions,
        int currentPositions,
        string? correlationId = null)
    {
        EventId = Guid.NewGuid();
        PortfolioId = portfolioId;
        MaxPositions = maxPositions;
        CurrentPositions = currentPositions;
        OccurredAt = DateTimeOffset.UtcNow;
        CorrelationId = correlationId;
    }
}
