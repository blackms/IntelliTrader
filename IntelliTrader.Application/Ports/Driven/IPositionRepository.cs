using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Repository port for Position aggregate persistence.
/// This is a driven (secondary) port - the application defines it, infrastructure implements it.
/// </summary>
public interface IPositionRepository
{
    /// <summary>
    /// Gets a position by its ID.
    /// </summary>
    Task<Position?> GetByIdAsync(PositionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a position by trading pair.
    /// </summary>
    Task<Position?> GetByPairAsync(TradingPair pair, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active (open) positions.
    /// </summary>
    Task<IReadOnlyList<Position>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all positions for a specific portfolio.
    /// </summary>
    Task<IReadOnlyList<Position>> GetByPortfolioAsync(
        PortfolioId portfolioId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets closed positions within a date range.
    /// </summary>
    Task<IReadOnlyList<Position>> GetClosedPositionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a position (insert or update).
    /// </summary>
    Task SaveAsync(Position position, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves multiple positions in a batch.
    /// </summary>
    Task SaveManyAsync(IEnumerable<Position> positions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a position.
    /// </summary>
    Task DeleteAsync(PositionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a position exists for the given pair.
    /// </summary>
    Task<bool> ExistsForPairAsync(TradingPair pair, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of active positions.
    /// </summary>
    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default);
}
