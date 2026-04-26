using IntelliTrader.Domain.Events;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Read-side port for operational trading state.
/// </summary>
public interface ITradingStateReadModel
{
    Task<TradingStateReadModelEntry> GetAsync(
        CancellationToken cancellationToken = default);
}

public sealed record TradingStateReadModelEntry
{
    public static TradingStateReadModelEntry Running => new();

    public bool IsTradingSuspended { get; init; }
    public SuspensionReason? SuspensionReason { get; init; }
    public bool IsForcedSuspension { get; init; }
    public DateTimeOffset? SuspendedAt { get; init; }
    public DateTimeOffset? ResumedAt { get; init; }
    public DateTimeOffset? LastChangedAt { get; init; }
    public int OpenPositionsAtSuspension { get; init; }
    public int PendingOrdersAtSuspension { get; init; }
    public string? ChangedBy { get; init; }
    public string? Details { get; init; }
}
