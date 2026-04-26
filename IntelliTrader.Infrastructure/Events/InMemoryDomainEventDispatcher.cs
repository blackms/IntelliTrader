using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.SharedKernel;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace IntelliTrader.Infrastructure.Events;

/// <summary>
/// In-memory implementation of domain event dispatcher.
/// Dispatches events synchronously to all registered handlers.
/// </summary>
public class InMemoryDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryDomainEventDispatcher> _logger;
    private readonly IDomainEventHandlerInbox? _handlerInbox;
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();

    public InMemoryDomainEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<InMemoryDomainEventDispatcher> logger,
        IDomainEventHandlerInbox? handlerInbox = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handlerInbox = handlerInbox;
    }

    /// <summary>
    /// Registers a handler for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The type of event to handle.</typeparam>
    /// <param name="handler">The handler to register.</param>
    public void RegisterHandler<TEvent>(IDomainEventHandler<TEvent> handler) where TEvent : IDomainEvent
    {
        var eventType = typeof(TEvent);
        var handlers = _handlers.GetOrAdd(eventType, _ => new List<object>());

        lock (handlers)
        {
            handlers.Add(handler);
        }

        _logger.LogDebug("Registered handler {HandlerType} for event {EventType}",
            handler.GetType().Name, eventType.Name);
    }

    /// <summary>
    /// Unregisters a handler for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">The type of event.</typeparam>
    /// <param name="handler">The handler to unregister.</param>
    public void UnregisterHandler<TEvent>(IDomainEventHandler<TEvent> handler) where TEvent : IDomainEvent
    {
        var eventType = typeof(TEvent);

        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }

            _logger.LogDebug("Unregistered handler {HandlerType} for event {EventType}",
                handler.GetType().Name, eventType.Name);
        }
    }

    /// <inheritdoc />
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        _logger.LogDebug("Dispatching event {EventType} with ID {EventId}",
            eventType.Name, domainEvent.EventId);

        var handlers = GetHandlersForEvent(eventType);

        if (handlers.Count == 0)
        {
            _logger.LogDebug("No handlers registered for event {EventType}", eventType.Name);
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                await InvokeHandlerAsync(handler, domainEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Handler {HandlerType} failed while processing event {EventType} (ID: {EventId})",
                    handler.GetType().Name, eventType.Name, domainEvent.EventId);

                // Re-throw to maintain existing behavior - callers can catch if they want to continue
                throw;
            }
        }

        _logger.LogDebug("Completed dispatching event {EventType} to {HandlerCount} handlers",
            eventType.Name, handlers.Count);
    }

    /// <inheritdoc />
    public async Task DispatchManyAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        var eventList = domainEvents.ToList();

        if (eventList.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Dispatching {EventCount} events", eventList.Count);

        foreach (var domainEvent in eventList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private List<object> GetHandlersForEvent(Type eventType)
    {
        var allHandlers = new List<object>();

        // Get handlers registered directly with this dispatcher
        if (_handlers.TryGetValue(eventType, out var registeredHandlers))
        {
            lock (registeredHandlers)
            {
                allHandlers.AddRange(registeredHandlers);
            }
        }

        // Try to resolve handlers from DI container
        try
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var serviceHandlers = _serviceProvider.GetService(typeof(IEnumerable<>).MakeGenericType(handlerType));

            if (serviceHandlers is System.Collections.IEnumerable enumerable)
            {
                foreach (var handler in enumerable)
                {
                    if (handler != null)
                    {
                        allHandlers.Add(handler);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve handlers from DI for event type {EventType}", eventType.Name);
        }

        return allHandlers;
    }

    private async Task InvokeHandlerAsync(object handler, IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handleMethod = handlerType.GetMethod("HandleAsync");
        var handlerName = ResolveHandlerName(handler);

        if (handleMethod == null)
        {
            _logger.LogWarning("Could not find HandleAsync method on handler {HandlerType}", handler.GetType().Name);
            return;
        }

        if (_handlerInbox is not null &&
            await _handlerInbox.HasProcessedAsync(domainEvent.EventId, handlerName, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug(
                "Skipping already processed event {EventType} with ID {EventId} for handler {HandlerName}",
                eventType.Name,
                domainEvent.EventId,
                handlerName);
            return;
        }

        var task = (Task?)handleMethod.Invoke(handler, new object[] { domainEvent, cancellationToken });

        if (task != null)
        {
            await task.ConfigureAwait(false);
        }

        if (_handlerInbox is not null)
        {
            await _handlerInbox.MarkProcessedAsync(domainEvent.EventId, handlerName, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string ResolveHandlerName(object handler)
    {
        var handlerType = handler.GetType();
        return handlerType.AssemblyQualifiedName ?? handlerType.FullName ?? handlerType.Name;
    }
}
