using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Repository port for Portfolio aggregate persistence.
/// This is a driven (secondary) port - the application defines it, infrastructure implements it.
/// </summary>
public interface IPortfolioRepository
{
    /// <summary>
    /// Gets a portfolio by its ID.
    /// </summary>
    Task<Portfolio?> GetByIdAsync(PortfolioId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a portfolio by name.
    /// </summary>
    Task<Portfolio?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default/primary portfolio.
    /// </summary>
    Task<Portfolio?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all portfolios.
    /// </summary>
    Task<IReadOnlyList<Portfolio>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a portfolio (insert or update).
    /// </summary>
    Task SaveAsync(Portfolio portfolio, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a portfolio.
    /// </summary>
    Task DeleteAsync(PortfolioId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a portfolio exists with the given name.
    /// </summary>
    Task<bool> ExistsWithNameAsync(string name, CancellationToken cancellationToken = default);
}
