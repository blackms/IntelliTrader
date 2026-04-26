using Autofac;
using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests for dispatching OpenPositionCommand through the real CQRS pipeline.
/// This verifies the new Application/Domain path is actually usable from the container,
/// rather than only through isolated unit tests.
/// </summary>
public sealed class OpenPositionCommandDispatchIntegrationTests : IClassFixture<InfrastructureTestFixture>, IDisposable
{
    private readonly Mock<IExchangePort> _exchangePortMock = new();
    private readonly Mock<IAuditService> _auditServiceMock = new();
    private readonly InfrastructureTestFixture _fixture;
    private readonly IContainer _container;

    public OpenPositionCommandDispatchIntegrationTests(InfrastructureTestFixture fixture)
    {
        _fixture = fixture;
        var positionsPath = fixture.CreateTempFilePath("open_position_positions");
        var portfoliosPath = fixture.CreateTempFilePath("open_position_portfolios");
        var ordersPath = fixture.CreateTempFilePath("open_position_orders");
        var transactionCoordinator = new IntelliTrader.Infrastructure.Transactions.JsonTransactionCoordinator();

        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(transactionCoordinator).SingleInstance();
        fixture.RegisterIsolatedEventPersistence(
            builder,
            transactionCoordinator,
            "open_position",
            portfoliosPath);

        // Test-specific concrete adapters.
        builder.Register(c => new JsonPositionRepository(positionsPath, transactionCoordinator))
            .As<IPositionRepository>()
            .AsSelf()
            .SingleInstance();

        builder.Register(c => new JsonPortfolioRepository(portfoliosPath, transactionCoordinator))
            .As<IPortfolioRepository>()
            .AsSelf()
            .SingleInstance();

        builder.Register(c => new JsonOrderRepository(ordersPath, transactionCoordinator))
            .As<IOrderRepository>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterInstance(_exchangePortMock.Object)
            .As<IExchangePort>()
            .SingleInstance();

        builder.RegisterInstance(_auditServiceMock.Object)
            .As<IAuditService>()
            .SingleInstance();

        _container = builder.Build();

        SeedDefaultPortfolioAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _container.Dispose();
    }

    [Fact]
    public async Task DispatchAsync_WithValidOpenPositionCommand_PersistsStateAndAuditsPositionOpened()
    {
        // Given
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT"),
            SignalRule = "MomentumBreakout"
        };

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTradingRules(pair)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(50000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, command.Cost, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateFilledOrder(pair)));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var queryDispatcher = _container.Resolve<IQueryDispatcher>();
        var positionRepository = _container.Resolve<IPositionRepository>();
        var portfolioRepository = _container.Resolve<IPortfolioRepository>();

        // When
        var result = await dispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(command);

        // Then
        result.IsSuccess.Should().BeTrue();

        var persistedPosition = await positionRepository.GetByPairAsync(pair);
        persistedPosition.Should().NotBeNull();
        persistedPosition!.SignalRule.Should().Be("MomentumBreakout");
        persistedPosition.TotalQuantity.Value.Should().Be(0.02m);

        var portfolio = await portfolioRepository.GetDefaultAsync();
        portfolio.Should().NotBeNull();
        portfolio!.HasPositionFor(pair).Should().BeTrue();
        portfolio.GetPositionId(pair).Should().Be(result.Value.PositionId);

        var persistedOrder = await queryDispatcher.DispatchAsync<GetOrderQuery, OrderView>(
            new GetOrderQuery { OrderId = OrderId.From("order-open-position-1") });

        persistedOrder.IsSuccess.Should().BeTrue();
        persistedOrder.Value.Status.Should().Be(OrderLifecycleStatus.Filled);
        persistedOrder.Value.CanAffectPosition.Should().BeTrue();

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderPlaced",
                It.Is<string>(details => details.Contains(pair.Symbol, StringComparison.Ordinal) &&
                                         details.Contains("Buy", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderFilled",
                It.Is<string>(details => details.Contains(pair.Symbol, StringComparison.Ordinal) &&
                                         details.Contains("order-open-position-1", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "PositionOpened",
                It.Is<string>(details => details.Contains(pair.Symbol, StringComparison.Ordinal) &&
                                         details.Contains(result.Value.PositionId.Value.ToString(), StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenOrderRemainsSubmitted_PersistsOrderForQueryReadSide()
    {
        // Given
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT"),
            SignalRule = "BreakoutPending"
        };

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTradingRules(pair)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(2500m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, command.Cost, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateSubmittedOrder(pair)));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var queryDispatcher = _container.Resolve<IQueryDispatcher>();
        var positionRepository = _container.Resolve<IPositionRepository>();

        // When
        var result = await dispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(command);

        // Then
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not filled");

        var position = await positionRepository.GetByPairAsync(pair);
        position.Should().BeNull();

        var persistedOrder = await queryDispatcher.DispatchAsync<GetOrderQuery, OrderView>(
            new GetOrderQuery { OrderId = OrderId.From("order-open-position-pending-1") });

        persistedOrder.IsSuccess.Should().BeTrue();
        persistedOrder.Value.Status.Should().Be(OrderLifecycleStatus.Submitted);
        persistedOrder.Value.SignalRule.Should().Be("BreakoutPending");
        persistedOrder.Value.CanAffectPosition.Should().BeFalse();

        var recentOrders = await queryDispatcher.DispatchAsync<GetRecentOrdersQuery, IReadOnlyList<OrderView>>(
            new GetRecentOrdersQuery
            {
                Pair = pair,
                Status = OrderLifecycleStatus.Submitted,
                Limit = 10
            });

        recentOrders.IsSuccess.Should().BeTrue();
        recentOrders.Value.Should().ContainSingle(order => order.Id.Value == "order-open-position-pending-1");

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderPlaced",
                It.Is<string>(details => details.Contains(pair.Symbol, StringComparison.Ordinal) &&
                                         details.Contains("order-open-position-pending-1", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderFilled",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_WhenOrderIsPartiallyFilled_ExposesItThroughActiveOrdersQuery()
    {
        // Given
        var pair = TradingPair.Create("SOLUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT"),
            SignalRule = "DipBuy"
        };

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTradingRules(pair)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(2500m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, command.Cost, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreatePartiallyFilledOrder(pair)));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var queryDispatcher = _container.Resolve<IQueryDispatcher>();

        // When
        var result = await dispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(command);
        var activeOrders = await queryDispatcher.DispatchAsync<GetActiveOrdersQuery, IReadOnlyList<OrderView>>(
            new GetActiveOrdersQuery
            {
                Pair = pair,
                Limit = 10
            });

        // Then
        result.IsSuccess.Should().BeTrue();
        activeOrders.IsSuccess.Should().BeTrue();
        activeOrders.Value.Should().ContainSingle();
        activeOrders.Value[0].Id.Should().Be(OrderId.From("order-open-position-partial-1"));
        activeOrders.Value[0].Status.Should().Be(OrderLifecycleStatus.PartiallyFilled);
        activeOrders.Value[0].CanAffectPosition.Should().BeTrue();
        activeOrders.Value[0].IsTerminal.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_WhenPersistenceFails_DoesNotLeaveOrderPersistedOnDisk()
    {
        // Given
        var invalidPositionsPath = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"open_position_invalid_{Guid.NewGuid():N}")).FullName;
        var ordersPath = Path.Combine(Path.GetTempPath(), $"open_position_atomic_orders_{Guid.NewGuid():N}.json");
        var portfoliosPath = Path.Combine(Path.GetTempPath(), $"open_position_atomic_portfolios_{Guid.NewGuid():N}.json");
        var transactionCoordinator = new IntelliTrader.Infrastructure.Transactions.JsonTransactionCoordinator();

        using var invalidPositionRepository = new JsonPositionRepository(invalidPositionsPath, transactionCoordinator);
        using var orderRepository = new JsonOrderRepository(ordersPath, transactionCoordinator);
        using var portfolioRepository = new JsonPortfolioRepository(portfoliosPath, transactionCoordinator);

        await portfolioRepository.SaveAsync(Portfolio.Create("Default", "USDT", 10000m, 5, 10m));

        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(transactionCoordinator).SingleInstance();
        _fixture.RegisterIsolatedEventPersistence(
            builder,
            transactionCoordinator,
            "open_position_failure",
            portfoliosPath);
        builder.RegisterInstance(invalidPositionRepository)
            .As<IPositionRepository>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterInstance(portfolioRepository)
            .As<IPortfolioRepository>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterInstance(orderRepository)
            .As<IOrderRepository>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterInstance(_exchangePortMock.Object)
            .As<IExchangePort>()
            .SingleInstance();
        builder.RegisterInstance(_auditServiceMock.Object)
            .As<IAuditService>()
            .SingleInstance();

        using var container = builder.Build();
        var dispatcher = container.Resolve<ICommandDispatcher>();
        var pair = TradingPair.Create("SOLUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(100m, "USDT"),
            SignalRule = "AtomicityCheck"
        };

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTradingRules(pair)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(150m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, command.Cost, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(new ExchangeOrderResult
            {
                OrderId = "order-open-position-atomicity-1",
                Pair = pair,
                Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
                Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
                Status = IntelliTrader.Application.Ports.Driven.OrderStatus.Filled,
                RequestedQuantity = Quantity.Create(0.666666m),
                FilledQuantity = Quantity.Create(0.666666m),
                Price = Price.Create(150m),
                AveragePrice = Price.Create(150m),
                Cost = Money.Create(100m, "USDT"),
                Fees = Money.Create(0.1m, "USDT"),
                Timestamp = DateTimeOffset.UtcNow
            }));

        // When
        var result = await dispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(command);

        // Then
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Failed");

        using var reloadedOrders = new JsonOrderRepository(ordersPath);
        using var reloadedPortfolios = new JsonPortfolioRepository(portfoliosPath);

        var persistedOrder = await reloadedOrders.GetByIdAsync(OrderId.From("order-open-position-atomicity-1"));
        persistedOrder.Should().BeNull();

        var reloadedPortfolio = await reloadedPortfolios.GetDefaultAsync();
        reloadedPortfolio.Should().NotBeNull();
        reloadedPortfolio!.HasPositionFor(pair).Should().BeFalse();
    }

    private async Task SeedDefaultPortfolioAsync()
    {
        var portfolioRepository = _container.Resolve<IPortfolioRepository>();
        await portfolioRepository.SaveAsync(Portfolio.Create("Default", "USDT", 10000m, 5, 10m));
    }

    private static TradingPairRules CreateTradingRules(TradingPair pair)
    {
        return new TradingPairRules
        {
            Pair = pair,
            MinOrderValue = 10m,
            MinQuantity = 0.00001m,
            MaxQuantity = 10000m,
            QuantityStepSize = 0.00001m,
            PricePrecision = 2,
            QuantityPrecision = 5,
            MinPrice = 0.01m,
            MaxPrice = 1000000m,
            IsTradingEnabled = true
        };
    }

    private static ExchangeOrderResult CreateFilledOrder(TradingPair pair)
    {
        return new ExchangeOrderResult
        {
            OrderId = "order-open-position-1",
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.Filled,
            RequestedQuantity = Quantity.Create(0.02m),
            FilledQuantity = Quantity.Create(0.02m),
            Price = Price.Create(50000m),
            AveragePrice = Price.Create(50000m),
            Cost = Money.Create(1000m, "USDT"),
            Fees = Money.Create(1m, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ExchangeOrderResult CreateSubmittedOrder(TradingPair pair)
    {
        return new ExchangeOrderResult
        {
            OrderId = "order-open-position-pending-1",
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.New,
            RequestedQuantity = Quantity.Create(0.2m),
            FilledQuantity = Quantity.Zero,
            Price = Price.Create(2500m),
            AveragePrice = Price.Zero,
            Cost = Money.Zero("USDT"),
            Fees = Money.Zero("USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ExchangeOrderResult CreatePartiallyFilledOrder(TradingPair pair)
    {
        return new ExchangeOrderResult
        {
            OrderId = "order-open-position-partial-1",
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.PartiallyFilled,
            RequestedQuantity = Quantity.Create(0.2m),
            FilledQuantity = Quantity.Create(0.1m),
            Price = Price.Create(2500m),
            AveragePrice = Price.Create(2500m),
            Cost = Money.Create(250m, "USDT"),
            Fees = Money.Create(0.25m, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
