using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driving;
using Microsoft.Extensions.Logging;

namespace IntelliTrader.Infrastructure.Dispatching;

/// <summary>
/// Simple in-memory query dispatcher that resolves handlers from DI.
/// This implementation is suitable for single-process applications.
/// </summary>
/// <remarks>
/// Design decisions:
/// 1. Uses IServiceProvider for handler resolution (compatible with both Autofac and Microsoft DI)
/// 2. Includes logging for observability
/// 3. Wraps exceptions in Result for consistent error handling
/// 4. Can be extended with caching behavior for read optimization
/// </remarks>
public sealed class InMemoryQueryDispatcher : IQueryDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryQueryDispatcher> _logger;

    public InMemoryQueryDispatcher(
        IServiceProvider serviceProvider,
        ILogger<InMemoryQueryDispatcher> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<TResult>> DispatchAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : class
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryType = typeof(TQuery);
        var resultType = typeof(TResult);

        _logger.LogDebug(
            "Dispatching query {QueryType} expecting result {ResultType}",
            queryType.Name,
            resultType.Name);

        try
        {
            // Resolve the handler from DI
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(queryType, resultType);
            var handler = _serviceProvider.GetService(handlerType);

            if (handler is null)
            {
                _logger.LogError(
                    "No handler registered for query {QueryType}",
                    queryType.Name);

                return Result<TResult>.Failure(
                    Error.NotFound("QueryHandler", queryType.Name));
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
                new object[] { query, cancellationToken });

            if (task is null)
            {
                return Result<TResult>.Failure(
                    new Error("InvocationFailed", "Handler returned null"));
            }

            var result = await task.ConfigureAwait(false);

            _logger.LogDebug(
                "Query {QueryType} handled with success={IsSuccess}",
                queryType.Name,
                result.IsSuccess);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception while dispatching query {QueryType}",
                queryType.Name);

            return Result<TResult>.Failure(
                new Error("DispatchError", ex.Message));
        }
    }
}
