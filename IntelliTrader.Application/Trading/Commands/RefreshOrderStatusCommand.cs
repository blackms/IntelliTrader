using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Application.Trading.Commands;

/// <summary>
/// Command to refresh a persisted order lifecycle from the exchange.
/// </summary>
public sealed record RefreshOrderStatusCommand
{
    public required OrderId OrderId { get; init; }
}

/// <summary>
/// Result of reconciling an existing order with the exchange.
/// </summary>
public sealed record RefreshOrderStatusResult
{
    public required OrderId OrderId { get; init; }
    public required OrderLifecycleStatus PreviousStatus { get; init; }
    public required OrderLifecycleStatus CurrentStatus { get; init; }
    public required bool AppliedDomainEffects { get; init; }
    public PositionId? PositionId { get; init; }
}
