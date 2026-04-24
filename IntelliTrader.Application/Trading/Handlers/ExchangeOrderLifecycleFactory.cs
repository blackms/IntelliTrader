using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using ExchangeOrderSide = IntelliTrader.Application.Ports.Driven.OrderSide;
using ExchangeOrderStatus = IntelliTrader.Application.Ports.Driven.OrderStatus;
using ExchangeOrderType = IntelliTrader.Application.Ports.Driven.OrderType;

namespace IntelliTrader.Application.Trading.Handlers;

/// <summary>
/// Builds a domain order lifecycle from the exchange-facing result model.
/// This keeps transport status translation out of individual command handlers.
/// </summary>
internal static class ExchangeOrderLifecycleFactory
{
    public static OrderLifecycle Create(
        ExchangeOrderResult orderResult,
        string? signalRule = null,
        OrderIntent intent = OrderIntent.Unknown,
        PositionId? relatedPositionId = null)
    {
        ArgumentNullException.ThrowIfNull(orderResult);

        var lifecycle = OrderLifecycle.Submit(
            OrderId.From(orderResult.OrderId),
            orderResult.Pair,
            MapSide(orderResult.Side),
            MapType(orderResult.Type),
            orderResult.RequestedQuantity,
            orderResult.Price,
            signalRule: signalRule,
            timestamp: orderResult.Timestamp,
            intent: intent,
            relatedPositionId: relatedPositionId);

        switch (MapStatus(orderResult.Status))
        {
            case OrderLifecycleStatus.Submitted:
                break;

            case OrderLifecycleStatus.PartiallyFilled:
                lifecycle.MarkPartiallyFilled(
                    orderResult.FilledQuantity,
                    orderResult.AveragePrice,
                    orderResult.Cost,
                    orderResult.Fees);
                break;

            case OrderLifecycleStatus.Filled:
                lifecycle.MarkFilled(
                    orderResult.FilledQuantity,
                    orderResult.AveragePrice,
                    orderResult.Cost,
                    orderResult.Fees);
                break;

            case OrderLifecycleStatus.Canceled:
                ApplyCanceledFillIfPresent(
                    lifecycle,
                    orderResult.FilledQuantity,
                    orderResult.AveragePrice,
                    orderResult.Cost,
                    orderResult.Fees);
                lifecycle.Cancel();
                break;

            case OrderLifecycleStatus.Rejected:
                lifecycle.Reject();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(orderResult.Status), orderResult.Status, "Unsupported order status.");
        }

        return lifecycle;
    }

    public static bool Refresh(OrderLifecycle lifecycle, ExchangeOrderInfo orderInfo)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(orderInfo);

        var targetStatus = MapStatus(orderInfo.Status);
        if (targetStatus == lifecycle.Status)
        {
            if (targetStatus == OrderLifecycleStatus.PartiallyFilled && HasFillChanged(lifecycle, orderInfo))
            {
                lifecycle.MarkPartiallyFilled(
                    orderInfo.FilledQuantity,
                    orderInfo.AveragePrice,
                    CalculateCost(orderInfo),
                    orderInfo.Fees);
                return true;
            }

            return false;
        }

        switch (targetStatus)
        {
            case OrderLifecycleStatus.Submitted:
                return false;

            case OrderLifecycleStatus.PartiallyFilled:
                lifecycle.MarkPartiallyFilled(
                    orderInfo.FilledQuantity,
                    orderInfo.AveragePrice,
                    CalculateCost(orderInfo),
                    orderInfo.Fees);
                return true;

            case OrderLifecycleStatus.Filled:
                lifecycle.MarkFilled(
                    orderInfo.FilledQuantity,
                    orderInfo.AveragePrice,
                    CalculateCost(orderInfo),
                    orderInfo.Fees);
                return true;

            case OrderLifecycleStatus.Canceled:
                ApplyCanceledFillIfPresent(
                    lifecycle,
                    orderInfo.FilledQuantity,
                    orderInfo.AveragePrice,
                    CalculateCost(orderInfo),
                    orderInfo.Fees);
                lifecycle.Cancel();
                return true;

            case OrderLifecycleStatus.Rejected:
                lifecycle.Reject();
                return true;

            default:
                throw new ArgumentOutOfRangeException(nameof(orderInfo.Status), orderInfo.Status, "Unsupported order status.");
        }
    }

    private static Domain.Events.OrderSide MapSide(ExchangeOrderSide side)
    {
        return side switch
        {
            ExchangeOrderSide.Buy => Domain.Events.OrderSide.Buy,
            ExchangeOrderSide.Sell => Domain.Events.OrderSide.Sell,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unsupported order side.")
        };
    }

    private static Domain.Events.OrderType MapType(ExchangeOrderType type)
    {
        return type switch
        {
            ExchangeOrderType.Market => Domain.Events.OrderType.Market,
            ExchangeOrderType.Limit => Domain.Events.OrderType.Limit,
            ExchangeOrderType.StopLoss => Domain.Events.OrderType.StopLoss,
            ExchangeOrderType.StopLossLimit => Domain.Events.OrderType.StopLoss,
            ExchangeOrderType.TakeProfit => Domain.Events.OrderType.TakeProfit,
            ExchangeOrderType.TakeProfitLimit => Domain.Events.OrderType.TakeProfit,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported order type.")
        };
    }

    private static OrderLifecycleStatus MapStatus(ExchangeOrderStatus status)
    {
        return status switch
        {
            ExchangeOrderStatus.New => OrderLifecycleStatus.Submitted,
            ExchangeOrderStatus.PartiallyFilled => OrderLifecycleStatus.PartiallyFilled,
            ExchangeOrderStatus.Filled => OrderLifecycleStatus.Filled,
            ExchangeOrderStatus.Canceled => OrderLifecycleStatus.Canceled,
            ExchangeOrderStatus.PendingCancel => OrderLifecycleStatus.Canceled,
            ExchangeOrderStatus.Rejected => OrderLifecycleStatus.Rejected,
            ExchangeOrderStatus.Expired => OrderLifecycleStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported order status.")
        };
    }

    private static Money CalculateCost(ExchangeOrderInfo orderInfo)
    {
        return Money.Create(
            orderInfo.AveragePrice.Value * orderInfo.FilledQuantity.Value,
            orderInfo.Pair.QuoteCurrency);
    }

    private static void ApplyCanceledFillIfPresent(
        OrderLifecycle lifecycle,
        Quantity filledQuantity,
        Price averagePrice,
        Money cost,
        Money fees)
    {
        if (filledQuantity.IsZero)
        {
            return;
        }

        lifecycle.MarkPartiallyFilled(filledQuantity, averagePrice, cost, fees);
    }

    private static bool HasFillChanged(OrderLifecycle lifecycle, ExchangeOrderInfo orderInfo)
    {
        return lifecycle.FilledQuantity != orderInfo.FilledQuantity ||
               lifecycle.AveragePrice != orderInfo.AveragePrice ||
               lifecycle.Cost != CalculateCost(orderInfo) ||
               lifecycle.Fees != orderInfo.Fees;
    }
}
