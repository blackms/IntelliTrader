using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.Orders;

public class OrderLifecycleTests
{
    private static TradingPair CreatePair() => TradingPair.Create("BTCUSDT", "USDT");
    private static OrderId CreateOrderId(string value = "order-123") => OrderId.From(value);
    private static Quantity CreateQuantity(decimal value = 0.02m) => Quantity.Create(value);
    private static Price CreatePrice(decimal value = 50000m) => Price.Create(value);
    private static Money CreateMoney(decimal value, string currency = "USDT") => Money.Create(value, currency);

    [Fact]
    public void Submit_WithValidParameters_RaisesOrderPlacedEvent()
    {
        // Act
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice(),
            "MomentumBreakout");

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.Submitted);
        order.CanAffectPosition.Should().BeFalse();
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderPlacedEvent>();

        var domainEvent = (OrderPlacedEvent)order.DomainEvents.Single();
        domainEvent.OrderId.Should().Be("order-123");
        domainEvent.Pair.Should().Be("BTCUSDT");
        domainEvent.Side.Should().Be(OrderSide.Buy);
        domainEvent.OrderType.Should().Be(OrderType.Market);
        domainEvent.Amount.Should().Be(0.02m);
        domainEvent.SignalRule.Should().Be("MomentumBreakout");
    }

    [Fact]
    public void MarkPartiallyFilled_WithValidTransition_RaisesPartialFillEvent()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());
        order.ClearDomainEvents();

        // Act
        order.MarkPartiallyFilled(
            Quantity.Create(0.01m),
            Price.Create(50010m),
            CreateMoney(500.10m),
            CreateMoney(0.5m));

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        order.CanAffectPosition.Should().BeTrue();
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>();

        var domainEvent = (OrderFilledEvent)order.DomainEvents.Single();
        domainEvent.OrderId.Should().Be("order-123");
        domainEvent.IsPartialFill.Should().BeTrue();
        domainEvent.FilledAmount.Should().Be(0.01m);
        domainEvent.AveragePrice.Should().Be(50010m);
    }

    [Fact]
    public void MarkPartiallyFilled_WhenAlreadyPartiallyFilledWithHigherCumulativeQuantity_UpdatesFill()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Create(0.05m),
            CreatePrice());
        order.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            CreateMoney(1000m),
            CreateMoney(1m));
        order.ClearDomainEvents();

        // Act
        order.MarkPartiallyFilled(
            Quantity.Create(0.03m),
            Price.Create(50000m),
            CreateMoney(1500m),
            CreateMoney(1.5m));

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        order.FilledQuantity.Value.Should().Be(0.03m);
        order.Cost.Amount.Should().Be(1500m);
        order.Fees.Amount.Should().Be(1.5m);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>();
    }

    [Fact]
    public void MarkFilled_AfterCancellation_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());

        order.Cancel();

        // Act
        var action = () => order.MarkFilled(
            CreateQuantity(),
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Canceled*");
    }

    [Fact]
    public void LinkRelatedPosition_WithOpenPositionOrder_AssignsPositionId()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice(),
            intent: OrderIntent.OpenPosition);
        var positionId = PositionId.Create();

        // Act
        order.LinkRelatedPosition(positionId);

        // Assert
        order.RelatedPositionId.Should().Be(positionId);
    }
}
