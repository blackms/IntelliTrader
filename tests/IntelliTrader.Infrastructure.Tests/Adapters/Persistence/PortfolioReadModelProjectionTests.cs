using FluentAssertions;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Events;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;
using IntelliTrader.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class PortfolioReadModelProjectionTests
{
    [Fact]
    public async Task GetDefaultAsync_WhenProjectionIsEmpty_BootstrapsFromLegacyPortfolioJsonAndPersistsReadModel()
    {
        var portfolioPath = CreateReadModelPath("portfolios");
        var readModelPath = CreateReadModelPath("portfolio_read_model");
        using var repository = new JsonPortfolioRepository(portfolioPath);
        var portfolio = Portfolio.Create("Default", "USDT", 10000m, 5, 100m);
        portfolio.RecordPositionOpened(
            PositionId.Create(),
            TradingPair.Create("BTCUSDT", "USDT"),
            Money.Create(1000m, "USDT"));
        portfolio.ClearDomainEvents();
        await repository.SaveAsync(portfolio);

        try
        {
            using var readModel = new JsonPortfolioReadModel(readModelPath, portfolioPath);

            var projected = await readModel.GetDefaultAsync();

            projected.Should().NotBeNull();
            projected!.Id.Should().Be(portfolio.Id);
            projected.Name.Should().Be("Default");
            projected.TotalBalance.Amount.Should().Be(10000m);
            projected.AvailableBalance.Amount.Should().Be(9000m);
            projected.ReservedBalance.Amount.Should().Be(1000m);
            projected.ActivePositionCount.Should().Be(1);
            projected.InvestedBalance.Amount.Should().Be(1000m);

            using var reloaded = new JsonPortfolioReadModel(readModelPath);
            var reloadedDefault = await reloaded.GetDefaultAsync();
            reloadedDefault.Should().NotBeNull();
            reloadedDefault!.ActivePositionCount.Should().Be(1);
            reloadedDefault.InvestedBalance.Amount.Should().Be(1000m);
        }
        finally
        {
            DeleteFileIfExists(portfolioPath);
            DeleteFileIfExists(readModelPath);
        }
    }

    [Fact]
    public async Task DispatchManyAsync_WhenPortfolioEventsArePublished_ProjectsBalancesAndActiveCounts()
    {
        var portfolioPath = CreateReadModelPath("portfolios");
        var readModelPath = CreateReadModelPath("portfolio_read_model");
        using var repository = new JsonPortfolioRepository(portfolioPath);
        var portfolio = Portfolio.Create("Default", "USDT", 10000m, 5, 100m);
        await repository.SaveAsync(portfolio);
        using var readModel = new JsonPortfolioReadModel(readModelPath, portfolioPath);
        var handler = new PortfolioReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var positionId = PositionId.Create();
        var pair = TradingPair.Create("ETHUSDT", "USDT");

        portfolio.RecordPositionOpened(positionId, pair, Money.Create(1000m, "USDT"));
        var openedEvents = portfolio.DomainEvents.ToList();
        portfolio.ClearDomainEvents();

        portfolio.RecordPositionCostIncreased(positionId, pair, Money.Create(500m, "USDT"));
        var dcaEvents = portfolio.DomainEvents.ToList();
        portfolio.ClearDomainEvents();

        portfolio.RecordPositionClosed(positionId, pair, Money.Create(1800m, "USDT"));
        var closedEvents = portfolio.DomainEvents.ToList();

        try
        {
            await dispatcher.DispatchManyAsync(openedEvents.Concat(dcaEvents).Concat(closedEvents));

            var projected = await readModel.GetDefaultAsync();
            projected.Should().NotBeNull();
            projected!.TotalBalance.Amount.Should().Be(10300m);
            projected.AvailableBalance.Amount.Should().Be(10300m);
            projected.ReservedBalance.Amount.Should().Be(0m);
            projected.ActivePositionCount.Should().Be(0);
            projected.InvestedBalance.Amount.Should().Be(0m);
            projected.LastUpdatedAt.Should().NotBeNull();
        }
        finally
        {
            DeleteFileIfExists(portfolioPath);
            DeleteFileIfExists(readModelPath);
        }
    }

    private static InMemoryDomainEventDispatcher CreateDispatcher(PortfolioReadModelProjectionHandler handler)
    {
        var dispatcher = new InMemoryDomainEventDispatcher(
            Mock.Of<IServiceProvider>(),
            NullLogger<InMemoryDomainEventDispatcher>.Instance);
        dispatcher.RegisterHandler<PositionAddedToPortfolio>(handler);
        dispatcher.RegisterHandler<PositionRemovedFromPortfolio>(handler);
        dispatcher.RegisterHandler<PortfolioBalanceChanged>(handler);
        return dispatcher;
    }

    private static string CreateReadModelPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.json");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
