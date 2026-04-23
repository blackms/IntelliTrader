using FluentAssertions;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Transactions;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class JsonTransactionalUnitOfWorkIntegrationTests
{
    [Fact]
    public async Task CommitAsync_WhenOneResourceFails_RestoresPreviouslyAppliedFiles()
    {
        // Given
        var ordersPath = Path.Combine(Path.GetTempPath(), $"json_uow_orders_{Guid.NewGuid():N}.json");
        var invalidPositionsPath = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"json_uow_invalid_positions_{Guid.NewGuid():N}")).FullName;
        var transactionCoordinator = new JsonTransactionCoordinator();

        using var orderRepository = new JsonOrderRepository(ordersPath, transactionCoordinator);
        using var positionRepository = new JsonPositionRepository(invalidPositionsPath, transactionCoordinator);
        var unitOfWork = new JsonTransactionalUnitOfWork(transactionCoordinator);
        var pair = TradingPair.Create("ADAUSDT", "USDT");

        var order = OrderLifecycle.Submit(
            OrderId.From("json-uow-order-1"),
            pair,
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Create(10m),
            Price.Create(1m),
            signalRule: "Atomicity");
        order.MarkFilled(
            Quantity.Create(10m),
            Price.Create(1m),
            Money.Create(10m, "USDT"),
            Money.Create(0.1m, "USDT"));

        var position = Position.Open(
            pair,
            order.Id,
            Price.Create(1m),
            Quantity.Create(10m),
            Money.Create(0.1m, "USDT"),
            "Atomicity");

        // When
        await unitOfWork.BeginTransactionAsync();
        await orderRepository.SaveAsync(order);
        await positionRepository.SaveAsync(position);
        var commitResult = await unitOfWork.CommitAsync();

        // Then
        commitResult.IsFailure.Should().BeTrue();

        using var reloadedOrders = new JsonOrderRepository(ordersPath);
        var persistedOrder = await reloadedOrders.GetByIdAsync(order.Id);
        persistedOrder.Should().BeNull();
    }

    [Fact]
    public async Task CommitAsync_WhenThreeResourcesAreInvolved_RestoresAllFiles()
    {
        // Given
        var ordersPath = Path.Combine(Path.GetTempPath(), $"json_uow_orders_{Guid.NewGuid():N}.json");
        var portfoliosPath = Path.Combine(Path.GetTempPath(), $"json_uow_portfolios_{Guid.NewGuid():N}.json");
        var invalidPositionsPath = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"json_uow_invalid_positions_{Guid.NewGuid():N}")).FullName;
        var transactionCoordinator = new JsonTransactionCoordinator();

        using var orderRepository = new JsonOrderRepository(ordersPath, transactionCoordinator);
        using var positionRepository = new JsonPositionRepository(invalidPositionsPath, transactionCoordinator);
        using var portfolioRepository = new JsonPortfolioRepository(portfoliosPath, transactionCoordinator);
        var unitOfWork = new JsonTransactionalUnitOfWork(transactionCoordinator);
        var pair = TradingPair.Create("XRPUSDT", "USDT");
        var portfolio = Portfolio.Create("Default", "USDT", 1000m, 5, 10m);
        await portfolioRepository.SaveAsync(portfolio);

        var order = OrderLifecycle.Submit(
            OrderId.From("json-uow-order-portfolio-1"),
            pair,
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Create(10m),
            Price.Create(1m),
            signalRule: "Atomicity");
        order.MarkFilled(
            Quantity.Create(10m),
            Price.Create(1m),
            Money.Create(10m, "USDT"),
            Money.Create(0.1m, "USDT"));

        var position = Position.Open(
            pair,
            order.Id,
            Price.Create(1m),
            Quantity.Create(10m),
            Money.Create(0.1m, "USDT"),
            "Atomicity");
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(10m, "USDT"));

        // When
        await unitOfWork.BeginTransactionAsync();
        await orderRepository.SaveAsync(order);
        await positionRepository.SaveAsync(position);
        await portfolioRepository.SaveAsync(portfolio);
        var commitResult = await unitOfWork.CommitAsync();

        // Then
        commitResult.IsFailure.Should().BeTrue();

        using var reloadedOrders = new JsonOrderRepository(ordersPath);
        using var reloadedPortfolios = new JsonPortfolioRepository(portfoliosPath);

        var persistedOrder = await reloadedOrders.GetByIdAsync(order.Id);
        persistedOrder.Should().BeNull();

        var persistedPortfolio = await reloadedPortfolios.GetDefaultAsync();
        persistedPortfolio.Should().NotBeNull();
        persistedPortfolio!.HasPositionFor(pair).Should().BeFalse();
    }
}
