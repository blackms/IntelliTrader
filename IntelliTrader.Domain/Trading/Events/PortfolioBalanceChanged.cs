using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Trading.Events;

/// <summary>
/// Domain event raised when the portfolio balance changes.
/// </summary>
public sealed record PortfolioBalanceChanged : IDomainEvent
{
    public PortfolioId PortfolioId { get; }
    public Money PreviousTotal { get; }
    public Money NewTotal { get; }
    public Money PreviousAvailable { get; }
    public Money NewAvailable { get; }
    public string Reason { get; }
    public DateTimeOffset OccurredAt { get; }

    public PortfolioBalanceChanged(
        PortfolioId portfolioId,
        Money previousTotal,
        Money newTotal,
        Money previousAvailable,
        Money newAvailable,
        string reason)
    {
        PortfolioId = portfolioId;
        PreviousTotal = previousTotal;
        NewTotal = newTotal;
        PreviousAvailable = previousAvailable;
        NewAvailable = newAvailable;
        Reason = reason;
        OccurredAt = DateTimeOffset.UtcNow;
    }
}
