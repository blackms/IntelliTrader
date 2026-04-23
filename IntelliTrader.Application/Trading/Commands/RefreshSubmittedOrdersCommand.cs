namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to refresh a batch of submitted orders from the exchange.
/// </summary>
public sealed record RefreshSubmittedOrdersCommand
{
    public int Limit { get; init; } = 50;
}

/// <summary>
/// Aggregated result of refreshing submitted orders in batch.
/// </summary>
public sealed record RefreshSubmittedOrdersResult
{
    public required int TotalSubmitted { get; init; }
    public required int AttemptedCount { get; init; }
    public required int RefreshedCount { get; init; }
    public required int AppliedDomainEffectsCount { get; init; }
    public required int FailedCount { get; init; }
}
