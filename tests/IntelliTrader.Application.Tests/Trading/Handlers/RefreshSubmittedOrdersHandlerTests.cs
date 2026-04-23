using FluentAssertions;
using Moq;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using DomainOrderType = IntelliTrader.Domain.Events.OrderType;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public sealed class RefreshSubmittedOrdersHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<ICommandDispatcher> _commandDispatcherMock = new();
    private readonly RefreshSubmittedOrdersHandler _handler;

    public RefreshSubmittedOrdersHandlerTests()
    {
        _handler = new RefreshSubmittedOrdersHandler(
            _orderRepositoryMock.Object,
            _commandDispatcherMock.Object);
    }

    [Fact]
    public async Task HandleAsync_WhenLimitIsApplied_RefreshesOldestSubmittedOrdersOnly()
    {
        // Arrange
        var oldestSubmitted = CreateSubmittedOrder("submitted-oldest", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newestSubmitted = CreateSubmittedOrder("submitted-newest", DateTimeOffset.UtcNow.AddMinutes(-1));
        var filledOrder = CreateFilledOrder("filled-order", DateTimeOffset.UtcNow.AddMinutes(-5));

        _orderRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { newestSubmitted, filledOrder, oldestSubmitted });

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<RefreshOrderStatusCommand, RefreshOrderStatusResult>(
                It.IsAny<RefreshOrderStatusCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshOrderStatusCommand command, CancellationToken _) =>
                Result<RefreshOrderStatusResult>.Success(new RefreshOrderStatusResult
                {
                    OrderId = command.OrderId,
                    PreviousStatus = OrderLifecycleStatus.Submitted,
                    CurrentStatus = OrderLifecycleStatus.Filled,
                    AppliedDomainEffects = true
                }));

        // Act
        var result = await _handler.HandleAsync(new RefreshSubmittedOrdersCommand { Limit = 1 });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalSubmitted.Should().Be(2);
        result.Value.AttemptedCount.Should().Be(1);
        result.Value.RefreshedCount.Should().Be(1);
        result.Value.AppliedDomainEffectsCount.Should().Be(1);
        result.Value.FailedCount.Should().Be(0);

        _commandDispatcherMock.Verify(
            x => x.DispatchAsync<RefreshOrderStatusCommand, RefreshOrderStatusResult>(
                It.Is<RefreshOrderStatusCommand>(command => command.OrderId == oldestSubmitted.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _commandDispatcherMock.Verify(
            x => x.DispatchAsync<RefreshOrderStatusCommand, RefreshOrderStatusResult>(
                It.Is<RefreshOrderStatusCommand>(command => command.OrderId == newestSubmitted.Id),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenSomeRefreshesFail_AccumulatesFailureCountsWithoutStoppingBatch()
    {
        // Arrange
        var firstSubmitted = CreateSubmittedOrder("submitted-first", DateTimeOffset.UtcNow.AddMinutes(-2));
        var secondSubmitted = CreateSubmittedOrder("submitted-second", DateTimeOffset.UtcNow.AddMinutes(-1));

        _orderRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { firstSubmitted, secondSubmitted });

        _commandDispatcherMock
            .SetupSequence(x => x.DispatchAsync<RefreshOrderStatusCommand, RefreshOrderStatusResult>(
                It.IsAny<RefreshOrderStatusCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RefreshOrderStatusResult>.Success(new RefreshOrderStatusResult
            {
                OrderId = firstSubmitted.Id,
                PreviousStatus = OrderLifecycleStatus.Submitted,
                CurrentStatus = OrderLifecycleStatus.Submitted,
                AppliedDomainEffects = false
            }))
            .ReturnsAsync(Result<RefreshOrderStatusResult>.Failure(
                Error.ExchangeError("exchange unavailable")));

        // Act
        var result = await _handler.HandleAsync(new RefreshSubmittedOrdersCommand());

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalSubmitted.Should().Be(2);
        result.Value.AttemptedCount.Should().Be(2);
        result.Value.RefreshedCount.Should().Be(1);
        result.Value.AppliedDomainEffectsCount.Should().Be(0);
        result.Value.FailedCount.Should().Be(1);
    }

    private static OrderLifecycle CreateSubmittedOrder(string orderId, DateTimeOffset submittedAt)
    {
        var order = OrderLifecycle.Submit(
            OrderId.From(orderId),
            TradingPair.Create("BTCUSDT", "USDT"),
            DomainOrderSide.Buy,
            DomainOrderType.Market,
            Quantity.Create(0.02m),
            Price.Create(50000m),
            signalRule: "MomentumBreakout",
            timestamp: submittedAt,
            intent: OrderIntent.OpenPosition);

        order.ClearDomainEvents();
        return order;
    }

    private static OrderLifecycle CreateFilledOrder(string orderId, DateTimeOffset submittedAt)
    {
        var order = CreateSubmittedOrder(orderId, submittedAt);
        order.MarkFilled(
            Quantity.Create(0.02m),
            Price.Create(50000m),
            Money.Create(1000m, "USDT"),
            Money.Create(1m, "USDT"));
        order.ClearDomainEvents();
        return order;
    }
}
