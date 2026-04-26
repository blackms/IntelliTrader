using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Read-side port for portfolio query models.
/// </summary>
public interface IPortfolioReadModel
{
    Task<PortfolioReadModelEntry?> GetByIdAsync(
        PortfolioId id,
        CancellationToken cancellationToken = default);

    Task<PortfolioReadModelEntry?> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<PortfolioReadModelEntry?> GetDefaultAsync(
        CancellationToken cancellationToken = default);
}

public sealed record PortfolioReadModelEntry
{
    public required PortfolioId Id { get; init; }
    public required string Name { get; init; }
    public required string Market { get; init; }
    public required Money TotalBalance { get; init; }
    public required Money AvailableBalance { get; init; }
    public required Money ReservedBalance { get; init; }
    public required int ActivePositionCount { get; init; }
    public required int MaxPositions { get; init; }
    public required Money MinPositionCost { get; init; }
    public required Money InvestedBalance { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public bool IsDefault { get; init; }

    public bool CanOpenNewPosition => ActivePositionCount < MaxPositions && AvailableBalance >= MinPositionCost;

    public decimal AvailablePercentage => TotalBalance.IsZero
        ? 0m
        : AvailableBalance.Amount / TotalBalance.Amount * 100m;

    public decimal ReservedPercentage => TotalBalance.IsZero
        ? 0m
        : ReservedBalance.Amount / TotalBalance.Amount * 100m;
}
