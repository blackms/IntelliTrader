using IntelliTrader.Application.Common;

namespace IntelliTrader.Application.Ports.Driving;

/// <summary>
/// Mediator interface for dispatching commands to their handlers.
/// Provides a clean facade for the Application layer, decoupling callers from specific handler implementations.
/// </summary>
/// <remarks>
/// This dispatcher follows the Mediator pattern, allowing:
/// 1. Loose coupling between command senders and handlers
/// 2. Cross-cutting concerns (logging, validation, etc.) to be added centrally
/// 3. Easy testing through mocking
/// 4. Gradual migration from legacy services to new handlers
/// </remarks>
public interface ICommandDispatcher
{
    /// <summary>
    /// Dispatches a command that returns a result.
    /// </summary>
    /// <typeparam name="TCommand">The type of command</typeparam>
    /// <typeparam name="TResult">The type of result</typeparam>
    /// <param name="command">The command to dispatch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of handling the command</returns>
    Task<Result<TResult>> DispatchAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class;

    /// <summary>
    /// Dispatches a command that doesn't return a value.
    /// </summary>
    /// <typeparam name="TCommand">The type of command</typeparam>
    /// <param name="command">The command to dispatch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result of handling the command</returns>
    Task<Result> DispatchAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class;
}

/// <summary>
/// Extension methods for ICommandDispatcher to provide convenient typed dispatch methods.
/// </summary>
public static class CommandDispatcherExtensions
{
    /// <summary>
    /// Dispatches a command and returns the result, throwing on failure.
    /// </summary>
    public static async Task<TResult> DispatchRequiredAsync<TCommand, TResult>(
        this ICommandDispatcher dispatcher,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class
    {
        var result = await dispatcher.DispatchAsync<TCommand, TResult>(command, cancellationToken);

        if (result.IsFailure)
        {
            throw new CommandDispatchException(result.Error);
        }

        return result.Value;
    }

    /// <summary>
    /// Dispatches a command without return value, throwing on failure.
    /// </summary>
    public static async Task DispatchRequiredAsync<TCommand>(
        this ICommandDispatcher dispatcher,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class
    {
        var result = await dispatcher.DispatchAsync(command, cancellationToken);

        if (result.IsFailure)
        {
            throw new CommandDispatchException(result.Error);
        }
    }
}

/// <summary>
/// Exception thrown when a command dispatch fails.
/// </summary>
public sealed class CommandDispatchException : Exception
{
    /// <summary>
    /// The error that caused the failure.
    /// </summary>
    public Error Error { get; }

    public CommandDispatchException(Error error)
        : base(error.ToString())
    {
        Error = error;
    }

    public CommandDispatchException(Error error, Exception innerException)
        : base(error.ToString(), innerException)
    {
        Error = error;
    }
}
