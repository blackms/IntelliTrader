using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Repository port for persisted exchange order lifecycles.
/// This keeps command-side order execution and query-side order inspection
/// within the new Application/Domain stack.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Gets an order by its exchange-assigned identifier.
    /// </summary>
    Task<OrderLifecycle?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all persisted orders.
    /// </summary>
    Task<IReadOnlyList<OrderLifecycle>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves an order lifecycle.
    /// </summary>
    Task SaveAsync(OrderLifecycle order, CancellationToken cancellationToken = default);
}
