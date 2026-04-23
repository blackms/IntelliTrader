using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driving;
using Microsoft.Extensions.Logging;

namespace IntelliTrader.Infrastructure.Dispatching;

/// <summary>
/// Simple in-memory command dispatcher that resolves handlers from DI.
/// This implementation is suitable for single-process applications.
/// </summary>
/// <remarks>
/// Design decisions:
/// 1. Uses IServiceProvider for handler resolution (compatible with both Autofac and Microsoft DI)
/// 2. Includes logging for observability
/// 3. Wraps exceptions in Result for consistent error handling
/// 4. Supports both result-returning and void commands
/// </remarks>
public sealed class InMemoryCommandDispatcher : ICommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryCommandDispatcher> _logger;

    public InMemoryCommandDispatcher(
        IServiceProvider serviceProvider,
        ILogger<InMemoryCommandDispatcher> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<TResult>> DispatchAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = typeof(TCommand);
        var resultType = typeof(TResult);

        _logger.LogDebug(
            "Dispatching command {CommandType} expecting result {ResultType}",
            commandType.Name,
            resultType.Name);

        try
        {
            var transactionalUnitOfWork = _serviceProvider.GetService(typeof(ITransactionalUnitOfWork)) as ITransactionalUnitOfWork;
            if (transactionalUnitOfWork is not null)
            {
                await transactionalUnitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            }

            // Resolve the handler from DI
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType);
            var handler = _serviceProvider.GetService(handlerType);

            if (handler is null)
            {
                _logger.LogError(
                    "No handler registered for command {CommandType}",
                    commandType.Name);

                return Result<TResult>.Failure(
                    Error.NotFound("CommandHandler", commandType.Name));
            }

            // Invoke the handler
            var handleMethod = handlerType.GetMethod("HandleAsync");
            if (handleMethod is null)
            {
                return Result<TResult>.Failure(
                    new Error("MethodNotFound", "HandleAsync method not found on handler"));
            }

            var task = (Task<Result<TResult>>?)handleMethod.Invoke(
                handler,
                new object[] { command, cancellationToken });

            if (task is null)
            {
                return Result<TResult>.Failure(
                    new Error("InvocationFailed", "Handler returned null"));
            }

            var result = await task.ConfigureAwait(false);

            if (transactionalUnitOfWork?.HasActiveTransaction == true)
            {
                await transactionalUnitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Command {CommandType} handled with success={IsSuccess}",
                commandType.Name,
                result.IsSuccess);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while dispatching command {CommandType}",
                commandType.Name);

            return Result<TResult>.Failure(
                new Error("DispatchError", ex.Message));
        }
    }

    public async Task<Result> DispatchAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = typeof(TCommand);

        _logger.LogDebug("Dispatching void command {CommandType}", commandType.Name);

        try
        {
            var transactionalUnitOfWork = _serviceProvider.GetService(typeof(ITransactionalUnitOfWork)) as ITransactionalUnitOfWork;
            if (transactionalUnitOfWork is not null)
            {
                await transactionalUnitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            }

            // Resolve the handler from DI
            var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
            var handler = _serviceProvider.GetService(handlerType);

            if (handler is null)
            {
                _logger.LogError(
                    "No handler registered for command {CommandType}",
                    commandType.Name);

                return Result.Failure(
                    Error.NotFound("CommandHandler", commandType.Name));
            }

            // Invoke the handler
            var handleMethod = handlerType.GetMethod("HandleAsync");
            if (handleMethod is null)
            {
                return Result.Failure(
                    new Error("MethodNotFound", "HandleAsync method not found on handler"));
            }

            var task = (Task<Result>?)handleMethod.Invoke(
                handler,
                new object[] { command, cancellationToken });

            if (task is null)
            {
                return Result.Failure(
                    new Error("InvocationFailed", "Handler returned null"));
            }

            var result = await task.ConfigureAwait(false);

            if (transactionalUnitOfWork?.HasActiveTransaction == true)
            {
                await transactionalUnitOfWork.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Void command {CommandType} handled with success={IsSuccess}",
                commandType.Name,
                result.IsSuccess);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while dispatching void command {CommandType}",
                commandType.Name);

            return Result.Failure(
                new Error("DispatchError", ex.Message));
        }
    }
}
