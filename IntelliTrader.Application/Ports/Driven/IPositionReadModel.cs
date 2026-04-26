using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Read-side port for position query models.
/// </summary>
public interface IPositionReadModel
{
    Task<PositionReadModelEntry?> GetByIdAsync(
        PositionId id,
        CancellationToken cancellationToken = default);

    Task<PositionReadModelEntry?> GetByPairAsync(
        TradingPair pair,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PositionReadModelEntry>> GetActiveAsync(
        string? market,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PositionReadModelEntry>> GetClosedAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        TradingPair? pair,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record PositionReadModelEntry
{
    public PositionId Id { get; init; } = null!;
    public TradingPair Pair { get; init; } = null!;
    public Price AveragePrice { get; init; } = Price.Zero;
    public Quantity TotalQuantity { get; init; } = Quantity.Zero;
    public Money TotalCost { get; init; } = null!;
    public Money TotalFees { get; init; } = null!;
    public int DCALevel { get; init; }
    public int EntryCount { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
    public string? SignalRule { get; init; }
    public bool IsClosed { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public Money? RealizedPnL { get; init; }
}
