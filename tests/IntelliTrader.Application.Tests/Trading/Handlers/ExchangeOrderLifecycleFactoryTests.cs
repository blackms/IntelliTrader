using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;
using ExchangeOrderSide = IntelliTrader.Application.Ports.Driven.OrderSide;
using ExchangeOrderStatus = IntelliTrader.Application.Ports.Driven.OrderStatus;
using ExchangeOrderType = IntelliTrader.Application.Ports.Driven.OrderType;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class ExchangeOrderLifecycleFactoryTests
{
    private static readonly Type FactoryType = typeof(RefreshOrderStatusHandler)
        .Assembly
        .GetType("IntelliTrader.Application.Trading.Handlers.ExchangeOrderLifecycleFactory", throwOnError: true)!;

    private static readonly MethodInfo CreateMethod = FactoryType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo RefreshMethod = FactoryType.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Static)!;

    [Fact]
    public void Create_WhenStatusIsPendingCancel_MapsToCanceledLifecycle()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(status: ExchangeOrderStatus.PendingCancel);

        // Act
        var lifecycle = InvokeCreate(orderResult);

        // Assert
        lifecycle.Status.Should().Be(OrderLifecycleStatus.Canceled);
    }

    [Fact]
    public void Create_WhenStatusIsExpired_MapsToRejectedLifecycle()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(status: ExchangeOrderStatus.Expired);

        // Act
        var lifecycle = InvokeCreate(orderResult);

        // Assert
        lifecycle.Status.Should().Be(OrderLifecycleStatus.Rejected);
    }

    [Fact]
    public void Create_WhenTypeIsStopLossLimit_MapsToStopLoss()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(type: ExchangeOrderType.StopLossLimit);

        // Act
        var lifecycle = InvokeCreate(orderResult);

        // Assert
        lifecycle.Type.Should().Be(DomainOrderType.StopLoss);
    }

    [Fact]
    public void Create_WhenTypeIsTakeProfitLimit_MapsToTakeProfit()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(type: ExchangeOrderType.TakeProfitLimit);

        // Act
        var lifecycle = InvokeCreate(orderResult);

        // Assert
        lifecycle.Type.Should().Be(DomainOrderType.TakeProfit);
    }

    [Fact]
    public void Create_WhenSideIsUnsupported_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(side: (ExchangeOrderSide)999);

        // Act
        var act = () => InvokeCreate(orderResult);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Unsupported order side*");
    }

    [Fact]
    public void Create_WhenTypeIsUnsupported_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(type: (ExchangeOrderType)999);

        // Act
        var act = () => InvokeCreate(orderResult);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Unsupported order type*");
    }

    [Fact]
    public void Create_WhenStatusIsUnsupported_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var orderResult = CreateExchangeOrderResult(status: (ExchangeOrderStatus)999);

        // Act
        var act = () => InvokeCreate(orderResult);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Unsupported order status*");
    }

    [Fact]
    public void Refresh_WhenExchangeStatusMatchesSubmittedLifecycle_ReturnsFalseWithoutChangingLifecycle()
    {
        // Arrange
        var lifecycle = CreateSubmittedLifecycle();
        lifecycle.ClearDomainEvents();

        var orderInfo = CreateExchangeOrderInfo(status: ExchangeOrderStatus.New);

        // Act
        var changed = InvokeRefresh(lifecycle, orderInfo);

        // Assert
        changed.Should().BeFalse();
        lifecycle.Status.Should().Be(OrderLifecycleStatus.Submitted);
        lifecycle.FilledQuantity.Should().Be(Quantity.Zero);
        lifecycle.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_WhenExchangeReportsSubmittedForPartiallyFilledLifecycle_ReturnsFalseWithoutRegressingState()
    {
        // Arrange
        var lifecycle = CreateSubmittedLifecycle();
        lifecycle.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        lifecycle.ClearDomainEvents();

        var orderInfo = CreateExchangeOrderInfo(
            status: ExchangeOrderStatus.New,
            filledQuantity: 0m,
            averagePrice: 0m,
            fees: 0m);

        // Act
        var changed = InvokeRefresh(lifecycle, orderInfo);

        // Assert
        changed.Should().BeFalse();
        lifecycle.Status.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        lifecycle.FilledQuantity.Value.Should().Be(0.02m);
        lifecycle.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_WhenPartialFillSnapshotMatchesCurrentState_ReturnsFalseWithoutNewEvents()
    {
        // Arrange
        var lifecycle = CreateSubmittedLifecycle();
        lifecycle.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        lifecycle.ClearDomainEvents();

        var orderInfo = CreateExchangeOrderInfo(
            status: ExchangeOrderStatus.PartiallyFilled,
            filledQuantity: 0.02m,
            averagePrice: 50000m,
            fees: 1m);

        // Act
        var changed = InvokeRefresh(lifecycle, orderInfo);

        // Assert
        changed.Should().BeFalse();
        lifecycle.Status.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        lifecycle.FilledQuantity.Value.Should().Be(0.02m);
        lifecycle.Cost.Amount.Should().Be(1000m);
        lifecycle.Fees.Amount.Should().Be(1m);
        lifecycle.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_WhenSubmittedLifecycleIsCanceledWithExecutedQuantity_CapturesFillBeforeCanceling()
    {
        // Arrange
        var lifecycle = CreateSubmittedLifecycle();
        lifecycle.ClearDomainEvents();

        var orderInfo = CreateExchangeOrderInfo(
            status: ExchangeOrderStatus.Canceled,
            filledQuantity: 0.01m,
            averagePrice: 50100m,
            fees: 0.5m);

        // Act
        var changed = InvokeRefresh(lifecycle, orderInfo);

        // Assert
        changed.Should().BeTrue();
        lifecycle.Status.Should().Be(OrderLifecycleStatus.Canceled);
        lifecycle.FilledQuantity.Value.Should().Be(0.01m);
        lifecycle.AveragePrice.Value.Should().Be(50100m);
        lifecycle.Cost.Amount.Should().Be(501m);
        lifecycle.Fees.Amount.Should().Be(0.5m);
        lifecycle.HasUnappliedFill.Should().BeTrue();
        lifecycle.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>();
    }

    [Fact]
    public void Refresh_WhenCanceledLifecycleLaterReportsExecutedQuantity_CapturesFillWithoutReopeningOrder()
    {
        // Arrange
        var lifecycle = CreateSubmittedLifecycle();
        lifecycle.Cancel();
        lifecycle.ClearDomainEvents();

        var orderInfo = CreateExchangeOrderInfo(
            status: ExchangeOrderStatus.Canceled,
            filledQuantity: 0.01m,
            averagePrice: 50100m,
            fees: 0.5m);

        // Act
        var changed = InvokeRefresh(lifecycle, orderInfo);

        // Assert
        changed.Should().BeTrue();
        lifecycle.Status.Should().Be(OrderLifecycleStatus.Canceled);
        lifecycle.FilledQuantity.Value.Should().Be(0.01m);
        lifecycle.AveragePrice.Value.Should().Be(50100m);
        lifecycle.Cost.Amount.Should().Be(501m);
        lifecycle.Fees.Amount.Should().Be(0.5m);
        lifecycle.HasUnappliedFill.Should().BeTrue();
        lifecycle.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>();
    }

    [Fact]
    public void Refresh_WhenStatusIsUnsupported_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var lifecycle = CreateSubmittedLifecycle();
        var orderInfo = CreateExchangeOrderInfo(status: (ExchangeOrderStatus)999);

        // Act
        var act = () => InvokeRefresh(lifecycle, orderInfo);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Unsupported order status*");
    }

    private static OrderLifecycle InvokeCreate(
        ExchangeOrderResult orderResult,
        string? signalRule = null,
        OrderIntent intent = OrderIntent.Unknown,
        PositionId? relatedPositionId = null)
    {
        try
        {
            return (OrderLifecycle)CreateMethod.Invoke(null, [orderResult, signalRule, intent, relatedPositionId])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static bool InvokeRefresh(OrderLifecycle lifecycle, ExchangeOrderInfo orderInfo)
    {
        try
        {
            return (bool)RefreshMethod.Invoke(null, [lifecycle, orderInfo])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static OrderLifecycle CreateSubmittedLifecycle()
    {
        var lifecycle = OrderLifecycle.Submit(
            OrderId.From("factory-refresh-order-1"),
            TradingPair.Create("BTCUSDT", "USDT"),
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(0.05m),
            Price.Create(50000m),
            signalRule: "MomentumBreakout",
            timestamp: DateTimeOffset.UtcNow,
            intent: OrderIntent.OpenPosition);
        lifecycle.ClearDomainEvents();
        return lifecycle;
    }

    private static ExchangeOrderResult CreateExchangeOrderResult(
        ExchangeOrderStatus status = ExchangeOrderStatus.New,
        ExchangeOrderSide side = ExchangeOrderSide.Buy,
        ExchangeOrderType type = ExchangeOrderType.Market,
        decimal requestedQuantity = 0.05m,
        decimal filledQuantity = 0m,
        decimal price = 50000m,
        decimal averagePrice = 0m,
        decimal fees = 0m)
    {
        return new ExchangeOrderResult
        {
            OrderId = "exchange-order-result-1",
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            Side = side,
            Type = type,
            Status = status,
            RequestedQuantity = Quantity.Create(requestedQuantity),
            FilledQuantity = Quantity.Create(filledQuantity),
            Price = Price.Create(price),
            AveragePrice = Price.Create(averagePrice),
            Cost = Money.Create(averagePrice * filledQuantity, "USDT"),
            Fees = Money.Create(fees, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ExchangeOrderInfo CreateExchangeOrderInfo(
        ExchangeOrderStatus status,
        decimal filledQuantity = 0m,
        decimal averagePrice = 0m,
        decimal fees = 0m)
    {
        return new ExchangeOrderInfo
        {
            OrderId = "exchange-order-info-1",
            Pair = TradingPair.Create("BTCUSDT", "USDT"),
            Side = ExchangeOrderSide.Buy,
            Type = ExchangeOrderType.Market,
            Status = status,
            OriginalQuantity = Quantity.Create(0.05m),
            FilledQuantity = Quantity.Create(filledQuantity),
            Price = Price.Create(50000m),
            AveragePrice = Price.Create(averagePrice),
            Fees = Money.Create(fees, "USDT"),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
