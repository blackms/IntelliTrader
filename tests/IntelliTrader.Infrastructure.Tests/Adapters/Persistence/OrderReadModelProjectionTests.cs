using FluentAssertions;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;
using IntelliTrader.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DomainOrderSide = IntelliTrader.Domain.Events.OrderSide;
using TradeHistoryEntry = IntelliTrader.Application.Trading.Queries.TradeHistoryEntry;
using TradeType = IntelliTrader.Application.Trading.Queries.TradeType;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class OrderReadModelProjectionTests
{
    [Fact]
    public async Task DispatchManyAsync_WhenOrderEventsArePublished_ProjectsOrderViewAndPersistsIt()
    {
        var filePath = CreateReadModelPath();
        using var readModel = new JsonOrderReadModel(filePath);
        var handler = new OrderReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var order = OrderLifecycle.Submit(
            OrderId.From("projected-order-1"),
            TradingPair.Create("BTCUSDT", "USDT"),
            DomainOrderSide.Buy,
            OrderType.Limit,
            Quantity.Create(0.05m),
            Price.Create(50000m),
            signalRule: "MomentumBreakout",
            timestamp: DateTimeOffset.Parse("2026-04-26T10:00:00Z"),
            intent: OrderIntent.OpenPosition);
        var events = new List<IDomainEvent>(order.DomainEvents);

        order.ClearDomainEvents();
        order.MarkPartiallyFilled(
            Quantity.Create(0.02m),
            Price.Create(50010m),
            Money.Create(1000.20m, "USDT"),
            Money.Create(1.25m, "USDT"));
        events.AddRange(order.DomainEvents);

        order.ClearDomainEvents();
        order.Cancel();
        events.AddRange(order.DomainEvents);

        try
        {
            await dispatcher.DispatchManyAsync(events);

            var projected = await readModel.GetByIdAsync(order.Id);
            projected.Should().NotBeNull();
            projected!.Status.Should().Be(OrderLifecycleStatus.Canceled);
            projected.FilledQuantity.Value.Should().Be(0.02m);
            projected.AveragePrice.Value.Should().Be(50010m);
            projected.Cost.Amount.Should().Be(1000.20m);
            projected.Fees.Amount.Should().Be(1.25m);
            projected.IsTerminal.Should().BeTrue();
            projected.CanAffectPosition.Should().BeTrue();
            (await readModel.GetActiveAsync(order.Pair, DomainOrderSide.Buy, 10)).Should().BeEmpty();

            using var reloadedReadModel = new JsonOrderReadModel(filePath);
            var reloaded = await reloadedReadModel.GetByIdAsync(order.Id);
            reloaded.Should().NotBeNull();
            reloaded!.Status.Should().Be(OrderLifecycleStatus.Canceled);
            reloaded.FilledQuantity.Value.Should().Be(0.02m);
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DispatchManyAsync_WhenOpenOrderIsFilledAndLinked_ProjectsTradeHistory()
    {
        var filePath = CreateReadModelPath();
        using var readModel = new JsonOrderReadModel(filePath);
        var handler = new OrderReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var positionId = PositionId.Create();
        var order = OrderLifecycle.Submit(
            OrderId.From("projected-order-2"),
            TradingPair.Create("ETHUSDT", "USDT"),
            DomainOrderSide.Buy,
            OrderType.Market,
            Quantity.Create(1.5m),
            Price.Create(3000m),
            timestamp: DateTimeOffset.Parse("2026-04-26T11:00:00Z"),
            intent: OrderIntent.OpenPosition);
        var events = new List<IDomainEvent>(order.DomainEvents);

        order.ClearDomainEvents();
        order.MarkFilled(
            Quantity.Create(1.5m),
            Price.Create(3005m),
            Money.Create(4507.50m, "USDT"),
            Money.Create(2.10m, "USDT"));
        events.AddRange(order.DomainEvents);

        order.ClearDomainEvents();
        order.LinkRelatedPosition(positionId);
        events.AddRange(order.DomainEvents);

        try
        {
            await dispatcher.DispatchManyAsync(events);

            var history = await readModel.GetTradingHistoryAsync(
                order.Pair,
                DateTimeOffset.Parse("2026-04-26T00:00:00Z"),
                DateTimeOffset.Parse("2026-04-27T00:00:00Z"),
                offset: 0,
                limit: 10);

            history.Should().ContainSingle()
                .Which.Should().Match<TradeHistoryEntry>(entry =>
                    entry.PositionId == positionId &&
                    entry.OrderId == "projected-order-2" &&
                    entry.Type == TradeType.Buy &&
                    entry.Quantity.Value == 1.5m &&
                    entry.Price.Value == 3005m &&
                    entry.Cost.Amount == 4507.50m &&
                    entry.Fees.Amount == 2.10m);
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    private static InMemoryDomainEventDispatcher CreateDispatcher(OrderReadModelProjectionHandler handler)
    {
        var dispatcher = new InMemoryDomainEventDispatcher(
            Mock.Of<IServiceProvider>(),
            NullLogger<InMemoryDomainEventDispatcher>.Instance);
        dispatcher.RegisterHandler<OrderPlacedEvent>(handler);
        dispatcher.RegisterHandler<OrderFilledEvent>(handler);
        dispatcher.RegisterHandler<OrderCanceledEvent>(handler);
        dispatcher.RegisterHandler<OrderRejectedEvent>(handler);
        dispatcher.RegisterHandler<OrderLinkedToPositionEvent>(handler);
        return dispatcher;
    }

    private static string CreateReadModelPath()
    {
        return Path.Combine(Path.GetTempPath(), $"order_read_model_{Guid.NewGuid():N}.json");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
