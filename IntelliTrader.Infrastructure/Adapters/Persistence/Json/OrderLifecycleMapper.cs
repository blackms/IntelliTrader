using System.Reflection;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;

namespace IntelliTrader.Infrastructure.Adapters.Persistence.Json;

/// <summary>
/// Maps between OrderLifecycle aggregates and DTOs for JSON persistence.
/// </summary>
internal static class OrderLifecycleMapper
{
    public static OrderLifecycleDto ToDto(OrderLifecycle order)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderLifecycleDto
        {
            Id = order.Id.Value,
            Pair = new TradingPairDto
            {
                Symbol = order.Pair.Symbol,
                QuoteCurrency = order.Pair.QuoteCurrency
            },
            Side = order.Side.ToString(),
            Type = order.Type.ToString(),
            Status = order.Status.ToString(),
            RequestedQuantity = order.RequestedQuantity.Value,
            FilledQuantity = order.FilledQuantity.Value,
            SubmittedPrice = order.SubmittedPrice.Value,
            AveragePrice = order.AveragePrice.Value,
            CostAmount = order.Cost.Amount,
            CostCurrency = order.Cost.Currency,
            FeesAmount = order.Fees.Amount,
            FeesCurrency = order.Fees.Currency,
            AppliedQuantity = order.AppliedQuantity.Value,
            AppliedCostAmount = order.AppliedCost.Amount,
            AppliedCostCurrency = order.AppliedCost.Currency,
            AppliedFeesAmount = order.AppliedFees.Amount,
            AppliedFeesCurrency = order.AppliedFees.Currency,
            SignalRule = order.SignalRule,
            Intent = order.Intent.ToString(),
            RelatedPositionId = order.RelatedPositionId?.Value.ToString(),
            SubmittedAt = order.SubmittedAt
        };
    }

    public static OrderLifecycle FromDto(OrderLifecycleDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var order = (OrderLifecycle?)Activator.CreateInstance(
            typeof(OrderLifecycle),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null);

        if (order is null)
            throw new InvalidOperationException("Failed to create OrderLifecycle instance.");

        SetPrivateProperty(order, "Id", OrderId.From(dto.Id));
        SetPrivateProperty(order, "Pair", TradingPair.Create(dto.Pair.Symbol, dto.Pair.QuoteCurrency));
        SetPrivateProperty(order, "Side", Enum.Parse<DomainOrderSide>(dto.Side, ignoreCase: true));
        SetPrivateProperty(order, "Type", Enum.Parse<DomainOrderType>(dto.Type, ignoreCase: true));
        SetPrivateProperty(order, "Status", Enum.Parse<OrderLifecycleStatus>(dto.Status, ignoreCase: true));
        SetPrivateProperty(order, "RequestedQuantity", Quantity.Create(dto.RequestedQuantity));
        SetPrivateProperty(order, "FilledQuantity", Quantity.Create(dto.FilledQuantity));
        SetPrivateProperty(order, "SubmittedPrice", Price.Create(dto.SubmittedPrice));
        SetPrivateProperty(order, "AveragePrice", Price.Create(dto.AveragePrice));
        SetPrivateProperty(order, "Cost", Money.Create(dto.CostAmount, dto.CostCurrency));
        SetPrivateProperty(order, "Fees", Money.Create(dto.FeesAmount, dto.FeesCurrency));
        SetPrivateProperty(order, "AppliedQuantity", Quantity.Create(dto.AppliedQuantity));
        SetPrivateProperty(order, "AppliedCost", Money.Create(
            dto.AppliedCostAmount,
            string.IsNullOrWhiteSpace(dto.AppliedCostCurrency) ? dto.CostCurrency : dto.AppliedCostCurrency));
        SetPrivateProperty(order, "AppliedFees", Money.Create(
            dto.AppliedFeesAmount,
            string.IsNullOrWhiteSpace(dto.AppliedFeesCurrency) ? dto.FeesCurrency : dto.AppliedFeesCurrency));
        SetPrivateProperty(order, "SignalRule", dto.SignalRule);
        SetPrivateProperty(order, "Intent", ParseIntent(dto.Intent));
        SetPrivateProperty(
            order,
            "RelatedPositionId",
            string.IsNullOrWhiteSpace(dto.RelatedPositionId) ? null : PositionId.From(dto.RelatedPositionId));
        SetPrivateProperty(order, "SubmittedAt", dto.SubmittedAt);

        order.ClearDomainEvents();
        return order;
    }

    private static OrderIntent ParseIntent(string? intent)
    {
        return string.IsNullOrWhiteSpace(intent)
            ? OrderIntent.Unknown
            : Enum.Parse<OrderIntent>(intent, ignoreCase: true);
    }

    private static void SetPrivateProperty(object obj, string propertyName, object? value)
    {
        var type = obj.GetType();
        while (type != null)
        {
            var backingField = type.GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (backingField != null)
            {
                backingField.SetValue(obj, value);
                return;
            }

            type = type.BaseType;
        }

        throw new InvalidOperationException($"Backing field for property '{propertyName}' was not found.");
    }
}
