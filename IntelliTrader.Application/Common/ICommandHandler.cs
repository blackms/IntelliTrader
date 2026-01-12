namespace IntelliTrader.Application.Common;

/// <summary>
/// Interface for command handlers that return a result.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle</typeparam>
/// <typeparam name="TResult">The type of result returned</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>
    /// Handles the command and returns a result.
    /// </summary>
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for command handlers that don't return a value.
/// </summary>
/// <typeparam name="TCommand">The type of command to handle</typeparam>
public interface ICommandHandler<in TCommand>
{
    /// <summary>
    /// Handles the command.
    /// </summary>
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for query handlers.
/// </summary>
/// <typeparam name="TQuery">The type of query to handle</typeparam>
/// <typeparam name="TResult">The type of result returned</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>
    /// Handles the query and returns a result.
    /// </summary>
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
