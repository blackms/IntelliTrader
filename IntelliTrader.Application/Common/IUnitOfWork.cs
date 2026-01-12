namespace IntelliTrader.Application.Common;

/// <summary>
/// Unit of work interface for managing transactions across repositories.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits all changes made within the unit of work.
    /// </summary>
    Task<Result> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back all changes made within the unit of work.
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Extended unit of work that supports explicit transaction management.
/// </summary>
public interface ITransactionalUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a transaction is currently active.
    /// </summary>
    bool HasActiveTransaction { get; }
}
