using FluentAssertions;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;
using IntelliTrader.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class PositionReadModelProjectionTests
{
    [Fact]
    public async Task DispatchManyAsync_WhenPositionIsOpenedAndDcaExecuted_ProjectsActivePositionAndPersistsIt()
    {
        var filePath = CreateReadModelPath();
        using var readModel = new JsonPositionReadModel(filePath);
        var handler = new PositionReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var openedAt = DateTimeOffset.Parse("2026-04-26T10:00:00Z");
        var dcaAt = DateTimeOffset.Parse("2026-04-26T10:30:00Z");

        var events = new IDomainEvent[]
        {
            new PositionOpened(
                positionId,
                pair,
                OrderId.From("open-1"),
                Price.Create(100m),
                Quantity.Create(2m),
                Money.Create(200m, "USDT"),
                Money.Create(1m, "USDT"),
                "MomentumBreakout",
                occurredAt: openedAt),
            new DCAExecuted(
                positionId,
                pair,
                OrderId.From("dca-1"),
                newDCALevel: 1,
                Price.Create(80m),
                Quantity.Create(1m),
                Money.Create(80m, "USDT"),
                Money.Create(0.5m, "USDT"),
                Price.Create(93.33333333333333333333333333m),
                Quantity.Create(3m),
                Money.Create(280m, "USDT"),
                occurredAt: dcaAt)
        };

        try
        {
            await dispatcher.DispatchManyAsync(events);

            var projected = await readModel.GetByIdAsync(positionId);
            projected.Should().NotBeNull();
            projected!.Pair.Should().Be(pair);
            projected.AveragePrice.Value.Should().Be(93.33333333333333333333333333m);
            projected.TotalQuantity.Value.Should().Be(3m);
            projected.TotalCost.Amount.Should().Be(280m);
            projected.TotalFees.Amount.Should().Be(1.5m);
            projected.DCALevel.Should().Be(1);
            projected.EntryCount.Should().Be(2);
            projected.OpenedAt.Should().Be(openedAt);
            projected.IsClosed.Should().BeFalse();

            (await readModel.GetByPairAsync(pair)).Should().BeEquivalentTo(projected);
            (await readModel.GetActiveAsync("USDT")).Should().ContainSingle(p => p.Id == positionId);

            using var reloadedReadModel = new JsonPositionReadModel(filePath);
            var reloaded = await reloadedReadModel.GetByIdAsync(positionId);
            reloaded.Should().NotBeNull();
            reloaded!.TotalQuantity.Value.Should().Be(3m);
            reloaded.TotalFees.Amount.Should().Be(1.5m);
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DispatchManyAsync_WhenPositionIsPartiallyClosed_ProjectsExactRemainingAggregateState()
    {
        var filePath = CreateReadModelPath();
        using var readModel = new JsonPositionReadModel(filePath);
        var handler = new PositionReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("open-partial-1"),
            Price.Create(50000m),
            Quantity.Create(0.1m),
            Money.Create(10m, "USDT"),
            "MomentumBreakout");
        var events = new List<IDomainEvent>(position.DomainEvents);

        position.ClearDomainEvents();
        position.AddDCAEntry(
            OrderId.From("dca-partial-1"),
            Price.Create(25000m),
            Quantity.Create(0.1m),
            Money.Create(1m, "USDT"));
        events.AddRange(position.DomainEvents);

        position.ClearDomainEvents();
        position.ApplyCloseFillDelta(
            OrderId.From("sell-partial-1"),
            Price.Create(55000m),
            Quantity.Create(0.1m),
            Money.Create(2m, "USDT"));
        events.AddRange(position.DomainEvents);

        try
        {
            await dispatcher.DispatchManyAsync(events);

            var projected = await readModel.GetByIdAsync(position.Id);
            projected.Should().NotBeNull();
            projected!.TotalQuantity.Should().Be(position.TotalQuantity);
            projected.TotalCost.Should().Be(position.TotalCost);
            projected.TotalFees.Should().Be(position.TotalFees);
            projected.AveragePrice.Should().Be(position.AveragePrice);
            projected.EntryCount.Should().Be(position.Entries.Count);
            projected.IsClosed.Should().BeFalse();
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    [Fact]
    public async Task DispatchManyAsync_WhenPositionIsClosed_ProjectsClosedPositionAndRemovesItFromActiveQueries()
    {
        var filePath = CreateReadModelPath();
        using var readModel = new JsonPositionReadModel(filePath);
        var handler = new PositionReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var openedAt = DateTimeOffset.Parse("2026-04-26T09:00:00Z");
        var closedAt = DateTimeOffset.Parse("2026-04-26T12:00:00Z");

        var events = new IDomainEvent[]
        {
            new PositionOpened(
                positionId,
                pair,
                OrderId.From("open-2"),
                Price.Create(1000m),
                Quantity.Create(0.5m),
                Money.Create(500m, "USDT"),
                Money.Create(0.8m, "USDT"),
                null,
                occurredAt: openedAt),
            new PositionClosed(
                positionId,
                pair,
                OrderId.From("sell-2"),
                Price.Create(1100m),
                Quantity.Create(0.5m),
                Money.Create(550m, "USDT"),
                Money.Create(500m, "USDT"),
                Money.Create(1.8m, "USDT"),
                Margin.FromPercentage(9.6m),
                dcaLevel: 0,
                duration: TimeSpan.FromHours(3),
                occurredAt: closedAt)
        };

        try
        {
            await dispatcher.DispatchManyAsync(events);

            var projected = await readModel.GetByIdAsync(positionId);
            projected.Should().NotBeNull();
            projected!.IsClosed.Should().BeTrue();
            projected.ClosedAt.Should().Be(closedAt);
            projected.TotalFees.Amount.Should().Be(1.8m);

            (await readModel.GetByPairAsync(pair)).Should().BeNull();
            (await readModel.GetActiveAsync("USDT")).Should().BeEmpty();
            (await readModel.GetClosedAsync(openedAt, closedAt.AddMinutes(1), pair, limit: 10))
                .Should().ContainSingle(p => p.Id == positionId);
        }
        finally
        {
            DeleteFileIfExists(filePath);
        }
    }

    private static InMemoryDomainEventDispatcher CreateDispatcher(PositionReadModelProjectionHandler handler)
    {
        var dispatcher = new InMemoryDomainEventDispatcher(
            Mock.Of<IServiceProvider>(),
            NullLogger<InMemoryDomainEventDispatcher>.Instance);
        dispatcher.RegisterHandler<PositionOpened>(handler);
        dispatcher.RegisterHandler<DCAExecuted>(handler);
        dispatcher.RegisterHandler<PositionPartiallyClosed>(handler);
        dispatcher.RegisterHandler<PositionClosed>(handler);
        return dispatcher;
    }

    private static string CreateReadModelPath()
    {
        return Path.Combine(Path.GetTempPath(), $"position_read_model_{Guid.NewGuid():N}.json");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
