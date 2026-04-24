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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyFill_WhenFilledQuantityExceedsRequestedQuantity_ThrowsArgumentException(bool isPartialFill)
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Create(0.02m),
            CreatePrice());

        // Act
        var action = () =>
        {
            if (isPartialFill)
            {
                order.MarkPartiallyFilled(
                    Quantity.Create(0.03m),
                    CreatePrice(),
                    CreateMoney(1500m),
                    CreateMoney(1.5m));
            }
            else
            {
                order.MarkFilled(
                    Quantity.Create(0.03m),
                    CreatePrice(),
                    CreateMoney(1500m),
                    CreateMoney(1.5m));
            }
        };

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithParameterName("filledQuantity")
            .WithMessage("*cannot exceed requested quantity*");
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

    [Fact]
    public void Submit_WithZeroRequestedQuantity_ThrowsArgumentException()
    {
        // Act
        var action = () => OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Zero,
            CreatePrice());

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithParameterName("requestedQuantity")
            .WithMessage("*Requested quantity cannot be zero*");
    }

    [Theory]
    [InlineData(OrderIntent.ClosePosition)]
    [InlineData(OrderIntent.ExecuteDca)]
    public void Submit_WhenCloseOrDcaOrderDoesNotProvideRelatedPosition_ThrowsArgumentException(OrderIntent intent)
    {
        // Act
        var action = () => OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Sell,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice(),
            intent: intent);

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithParameterName("relatedPositionId")
            .WithMessage("*required for close and DCA orders*");
    }

    [Fact]
    public void Submit_WhenOpenPositionOrderReferencesExistingPosition_ThrowsArgumentException()
    {
        // Act
        var action = () => OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice(),
            intent: OrderIntent.OpenPosition,
            relatedPositionId: PositionId.Create());

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithParameterName("relatedPositionId")
            .WithMessage("*cannot reference an existing position*");
    }

    [Fact]
    public void MarkFilled_WithValidTransition_TracksUnappliedFillUntilApplied()
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
        order.MarkFilled(
            CreateQuantity(),
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.Filled);
        order.IsTerminal.Should().BeTrue();
        order.CanAffectPosition.Should().BeTrue();
        order.HasUnappliedFill.Should().BeTrue();
        order.UnappliedQuantity.Value.Should().Be(0.02m);
        order.UnappliedCost.Amount.Should().Be(1000m);
        order.UnappliedFees.Amount.Should().Be(1m);
        order.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderFilledEvent>();

        order.MarkCurrentFillApplied();

        order.AppliedQuantity.Value.Should().Be(0.02m);
        order.AppliedCost.Amount.Should().Be(1000m);
        order.AppliedFees.Amount.Should().Be(1m);
        order.HasUnappliedFill.Should().BeFalse();
        order.UnappliedQuantity.Value.Should().Be(0m);
        order.UnappliedCost.Amount.Should().Be(0m);
        order.UnappliedFees.Amount.Should().Be(0m);
    }

    [Fact]
    public void MarkCurrentFillApplied_WhenOrderHasNoFill_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());

        // Act
        var action = () => order.MarkCurrentFillApplied();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*status Submitted has no fill*");
    }

    [Fact]
    public void Reject_FromSubmitted_MarksOrderAsTerminal()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());

        // Act
        order.Reject();

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.Rejected);
        order.IsTerminal.Should().BeTrue();
        order.CanAffectPosition.Should().BeFalse();
    }

    [Fact]
    public void Cancel_FromPartiallyFilled_PreservesUnappliedBuyFillUntilApplied()
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
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));
        order.ClearDomainEvents();

        // Act
        order.Cancel();

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.Canceled);
        order.IsTerminal.Should().BeTrue();
        order.CanAffectPosition.Should().BeTrue();
        order.HasUnappliedFill.Should().BeTrue();

        order.MarkCurrentFillApplied();

        order.HasUnappliedFill.Should().BeFalse();
    }

    [Fact]
    public void Reject_AfterPartialFill_ThrowsInvalidOperationException()
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
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));

        // Act
        var action = () => order.Reject();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*from PartiallyFilled to Rejected*");
    }

    [Fact]
    public void Cancel_AfterFilled_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());
        order.MarkFilled(
            CreateQuantity(),
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));

        // Act
        var action = () => order.Cancel();

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*from Filled to Canceled*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyFill_WhenFilledQuantityIsZero_ThrowsArgumentException(bool isPartialFill)
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());

        // Act
        var action = () =>
        {
            if (isPartialFill)
            {
                order.MarkPartiallyFilled(
                    Quantity.Zero,
                    CreatePrice(),
                    CreateMoney(0m),
                    CreateMoney(0m));
            }
            else
            {
                order.MarkFilled(
                    Quantity.Zero,
                    CreatePrice(),
                    CreateMoney(0m),
                    CreateMoney(0m));
            }
        };

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithParameterName("filledQuantity")
            .WithMessage("*Filled quantity cannot be zero*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyFill_WhenAveragePriceIsZero_ThrowsArgumentException(bool isPartialFill)
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Buy,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice());

        // Act
        var action = () =>
        {
            if (isPartialFill)
            {
                order.MarkPartiallyFilled(
                    Quantity.Create(0.01m),
                    Price.Zero,
                    CreateMoney(500m),
                    CreateMoney(0.5m));
            }
            else
            {
                order.MarkFilled(
                    Quantity.Create(0.02m),
                    Price.Zero,
                    CreateMoney(1000m),
                    CreateMoney(1m));
            }
        };

        // Assert
        action.Should().Throw<ArgumentException>()
            .WithParameterName("averagePrice")
            .WithMessage("*Average price cannot be zero*");
    }

    [Fact]
    public void ApplyFill_WhenCumulativeQuantityRegresses_ThrowsInvalidOperationException()
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
            Quantity.Create(0.03m),
            CreatePrice(),
            CreateMoney(1500m),
            CreateMoney(1.5m));

        // Act
        var action = () => order.MarkFilled(
            Quantity.Create(0.02m),
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be lower than the current filled quantity*");
    }

    [Fact]
    public void MarkPartiallyFilled_WhenSnapshotIsUnchanged_DoesNothing()
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
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));
        order.ClearDomainEvents();

        // Act
        order.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            CreatePrice(),
            CreateMoney(1000m),
            CreateMoney(1m));

        // Assert
        order.Status.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        order.FilledQuantity.Value.Should().Be(0.02m);
        order.Cost.Amount.Should().Be(1000m);
        order.Fees.Amount.Should().Be(1m);
        order.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void LinkRelatedPosition_WhenIntentIsNotOpenPosition_ThrowsInvalidOperationException()
    {
        // Arrange
        var order = OrderLifecycle.Submit(
            CreateOrderId(),
            CreatePair(),
            OrderSide.Sell,
            OrderType.Market,
            CreateQuantity(),
            CreatePrice(),
            intent: OrderIntent.Unknown);

        // Act
        var action = () => order.LinkRelatedPosition(PositionId.Create());

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only open position orders can be linked*");
    }

    [Fact]
    public void LinkRelatedPosition_WhenAlreadyLinkedToDifferentPosition_ThrowsInvalidOperationException()
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
        order.LinkRelatedPosition(PositionId.Create());

        // Act
        var action = () => order.LinkRelatedPosition(PositionId.Create());

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already linked to position*");
    }
}
