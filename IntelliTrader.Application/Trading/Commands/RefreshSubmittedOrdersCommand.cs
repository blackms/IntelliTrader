namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to refresh a batch of active, non-terminal orders from the exchange.
/// </summary>
public sealed record RefreshActiveOrdersCommand
{
    public int Limit { get; init; } = 50;
}

/// <summary>
/// Aggregated result of refreshing active, non-terminal orders in batch.
/// </summary>
public sealed record RefreshActiveOrdersResult
{
    public required int TotalActive { get; init; }
    public required int AttemptedCount { get; init; }
    public required int RefreshedCount { get; init; }
    public required int AppliedDomainEffectsCount { get; init; }
    public required int FailedCount { get; init; }
}

/// <summary>
/// Legacy command to refresh a batch of submitted orders from the exchange.
/// Prefer <see cref="RefreshActiveOrdersCommand"/> because partially filled orders are refreshable too.
/// </summary>
public sealed record RefreshSubmittedOrdersCommand
{
    public int Limit { get; init; } = 50;
}

/// <summary>
/// Legacy aggregated result of refreshing submitted orders in batch.
/// Prefer <see cref="RefreshActiveOrdersResult"/>.
/// </summary>
public sealed record RefreshSubmittedOrdersResult
{
    public required int TotalSubmitted { get; init; }
    public required int AttemptedCount { get; init; }
    public required int RefreshedCount { get; init; }
    public required int AppliedDomainEffectsCount { get; init; }
    public required int FailedCount { get; init; }
}
