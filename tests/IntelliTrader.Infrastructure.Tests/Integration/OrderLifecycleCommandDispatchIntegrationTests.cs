using Autofac;
using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
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

public sealed class OrderLifecycleCommandDispatchIntegrationTests : IClassFixture<InfrastructureTestFixture>, IDisposable
{
    private readonly Mock<IExchangePort> _exchangePortMock = new();
    private readonly Mock<IAuditService> _auditServiceMock = new();
    private readonly IContainer _container;

    public OrderLifecycleCommandDispatchIntegrationTests(InfrastructureTestFixture fixture)
    {
        var positionsPath = fixture.CreateTempFilePath("order_lifecycle_positions");
        var portfoliosPath = fixture.CreateTempFilePath("order_lifecycle_portfolios");
        var ordersPath = fixture.CreateTempFilePath("order_lifecycle_orders");
        var transactionCoordinator = new IntelliTrader.Infrastructure.Transactions.JsonTransactionCoordinator();

        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(transactionCoordinator).SingleInstance();
        fixture.RegisterIsolatedEventPersistence(builder, transactionCoordinator, "order_lifecycle");

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
    }

    public void Dispose()
    {
        _container.Dispose();
    }

    [Fact]
    public async Task DispatchAsync_WithValidClosePositionCommand_PersistsFilledSellOrder()
    {
        // Given
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("buy-order-seed-1"),
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, "USDT"),
            "MomentumBreakout");

        await SeedOpenPositionAsync(position, 10000m);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(pair, position.TotalQuantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateFilledSellOrder(pair, position.TotalQuantity.Value)));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var queryDispatcher = _container.Resolve<IQueryDispatcher>();
        var positionRepository = _container.Resolve<IPositionRepository>();
        var portfolioRepository = _container.Resolve<IPortfolioRepository>();

        // When
        var result = await dispatcher.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
            new ClosePositionCommand
            {
                PositionId = position.Id,
                Reason = CloseReason.Manual
            });

        // Then
        result.IsSuccess.Should().BeTrue();

        var persistedPosition = await positionRepository.GetByIdAsync(position.Id);
        persistedPosition.Should().NotBeNull();
        persistedPosition!.IsClosed.Should().BeTrue();

        var portfolio = await portfolioRepository.GetDefaultAsync();
        portfolio.Should().NotBeNull();
        portfolio!.HasPositionFor(pair).Should().BeFalse();

        var persistedOrder = await queryDispatcher.DispatchAsync<GetOrderQuery, OrderView>(
            new GetOrderQuery { OrderId = OrderId.From("sell-order-close-1") });

        persistedOrder.IsSuccess.Should().BeTrue();
        persistedOrder.Value.Status.Should().Be(OrderLifecycleStatus.Filled);
        persistedOrder.Value.Side.Should().Be(IntelliTrader.Domain.Events.OrderSide.Sell);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderPlaced",
                It.Is<string>(details => details.Contains("sell-order-close-1", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderFilled",
                It.Is<string>(details => details.Contains("sell-order-close-1", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WithValidExecuteDcaCommand_PersistsFilledBuyOrder()
    {
        // Given
        var pair = TradingPair.Create("ETHUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("buy-order-seed-2"),
            Price.Create(2500m),
            Quantity.Create(0.2m),
            Money.Create(0.5m, "USDT"),
            "Breakout");

        await SeedOpenPositionAsync(position, 10000m);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(2400m)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTradingRules(pair)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateFilledDcaOrder(pair)));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var queryDispatcher = _container.Resolve<IQueryDispatcher>();
        var positionRepository = _container.Resolve<IPositionRepository>();

        // When
        var result = await dispatcher.DispatchAsync<ExecuteDCACommand, ExecuteDCAResult>(
            new ExecuteDCACommand
            {
                PositionId = position.Id,
                Cost = Money.Create(500m, "USDT")
            });

        // Then
        result.IsSuccess.Should().BeTrue();

        var persistedPosition = await positionRepository.GetByIdAsync(position.Id);
        persistedPosition.Should().NotBeNull();
        persistedPosition!.DCALevel.Should().Be(1);

        var persistedOrder = await queryDispatcher.DispatchAsync<GetOrderQuery, OrderView>(
            new GetOrderQuery { OrderId = OrderId.From("buy-order-dca-1") });

        persistedOrder.IsSuccess.Should().BeTrue();
        persistedOrder.Value.Status.Should().Be(OrderLifecycleStatus.Filled);
        persistedOrder.Value.Side.Should().Be(IntelliTrader.Domain.Events.OrderSide.Buy);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderPlaced",
                It.Is<string>(details => details.Contains("buy-order-dca-1", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderFilled",
                It.Is<string>(details => details.Contains("buy-order-dca-1", StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    private async Task SeedOpenPositionAsync(Position position, decimal balance)
    {
        var positionRepository = _container.Resolve<IPositionRepository>();
        var portfolioRepository = _container.Resolve<IPortfolioRepository>();

        var portfolio = Portfolio.Create("Default", "USDT", balance, 5, 10m);
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        await positionRepository.SaveAsync(position);
        await portfolioRepository.SaveAsync(portfolio);
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

    private static ExchangeOrderResult CreateFilledSellOrder(TradingPair pair, decimal quantity)
    {
        return new ExchangeOrderResult
        {
            OrderId = "sell-order-close-1",
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Sell,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.Filled,
            RequestedQuantity = Quantity.Create(quantity),
            FilledQuantity = Quantity.Create(quantity),
            Price = Price.Create(55000m),
            AveragePrice = Price.Create(55000m),
            Cost = Money.Create(1100m, "USDT"),
            Fees = Money.Create(1m, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ExchangeOrderResult CreateFilledDcaOrder(TradingPair pair)
    {
        return new ExchangeOrderResult
        {
            OrderId = "buy-order-dca-1",
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.Filled,
            RequestedQuantity = Quantity.Create(0.20833m),
            FilledQuantity = Quantity.Create(0.20833m),
            Price = Price.Create(2400m),
            AveragePrice = Price.Create(2400m),
            Cost = Money.Create(500m, "USDT"),
            Fees = Money.Create(0.5m, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
