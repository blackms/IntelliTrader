using IntelliTrader.Application.Common;

namespace IntelliTrader.Application.Ports.Driving;

/// <summary>
/// Mediator interface for dispatching queries to their handlers.
/// Provides a clean facade for the Application layer, decoupling callers from specific handler implementations.
/// </summary>
/// <remarks>
/// This dispatcher follows the Mediator pattern, separating queries (read operations) from
/// commands (write operations). Benefits include:
/// 1. CQRS (Command Query Responsibility Segregation) support
/// 2. Read operation optimization (caching, read replicas)
/// 3. Clear separation between side-effect-free reads and mutating writes
/// 4. Easy testing and mocking
/// </remarks>
public interface IQueryDispatcher
{
    /// <summary>
    /// Dispatches a query to its handler and returns the result.
    /// </summary>
    /// <typeparam name="TQuery">The type of query</typeparam>
    /// <typeparam name="TResult">The type of result</typeparam>
    /// <param name="query">The query to dispatch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of handling the query</returns>
    Task<Result<TResult>> DispatchAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : class;
}

/// <summary>
/// Extension methods for IQueryDispatcher to provide convenient typed dispatch methods.
/// </summary>
public static class QueryDispatcherExtensions
{
    /// <summary>
    /// Dispatches a query and returns the result, throwing on failure.
    /// </summary>
    public static async Task<TResult> DispatchRequiredAsync<TQuery, TResult>(
        this IQueryDispatcher dispatcher,
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : class
    {
        var result = await dispatcher.DispatchAsync<TQuery, TResult>(query, cancellationToken);

        if (result.IsFailure)
        {
            throw new QueryDispatchException(result.Error);
        }

        return result.Value;
    }

    /// <summary>
    /// Dispatches a query and returns the result, or a default value on failure.
    /// </summary>
    public static async Task<TResult?> DispatchOrDefaultAsync<TQuery, TResult>(
        this IQueryDispatcher dispatcher,
        TQuery query,
        TResult? defaultValue = default,
        CancellationToken cancellationToken = default)
        where TQuery : class
    {
        var result = await dispatcher.DispatchAsync<TQuery, TResult>(query, cancellationToken);

        return result.IsSuccess ? result.Value : defaultValue;
    }
}

/// <summary>
/// Exception thrown when a query dispatch fails.
/// </summary>
public sealed class QueryDispatchException : Exception
{
    /// <summary>
    /// The error that caused the failure.
    /// </summary>
    public Error Error { get; }

    public QueryDispatchException(Error error)
        : base(error.ToString())
    {
        Error = error;
    }

    public QueryDispatchException(Error error, Exception innerException)
        : base(error.ToString(), innerException)
    {
        Error = error;
    }
}
