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

public sealed class RefreshOrderStatusCommandDispatchIntegrationTests : IClassFixture<InfrastructureTestFixture>, IDisposable
{
    private readonly Mock<IExchangePort> _exchangePortMock = new();
    private readonly Mock<IAuditService> _auditServiceMock = new();
    private readonly IContainer _container;

    public RefreshOrderStatusCommandDispatchIntegrationTests(InfrastructureTestFixture fixture)
    {
        var positionsPath = fixture.CreateTempFilePath("refresh_order_status_positions");
        var portfoliosPath = fixture.CreateTempFilePath("refresh_order_status_portfolios");
        var ordersPath = fixture.CreateTempFilePath("refresh_order_status_orders");
        var transactionCoordinator = new IntelliTrader.Infrastructure.Transactions.JsonTransactionCoordinator();

        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(transactionCoordinator).SingleInstance();
        fixture.RegisterIsolatedEventPersistence(builder, transactionCoordinator, "refresh_order_status");

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
    public async Task DispatchAsync_WhenSubmittedOpenOrderIsRefreshedToFilled_PersistsFilledOrderAndCreatesPosition()
    {
        // Given
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var openCommand = new OpenPositionCommand
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
            .Setup(x => x.PlaceMarketBuyAsync(pair, openCommand.Cost, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateSubmittedOrder(pair)));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var queryDispatcher = _container.Resolve<IQueryDispatcher>();
        var positionRepository = _container.Resolve<IPositionRepository>();
        var orderId = OrderId.From("refresh-open-position-order-1");

        // When
        var submitResult = await dispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(openCommand);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, orderId.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateFilledOrderInfo(pair, orderId.Value)));

        var refreshResult = await dispatcher.DispatchAsync<RefreshOrderStatusCommand, RefreshOrderStatusResult>(
            new RefreshOrderStatusCommand { OrderId = orderId });

        // Then
        submitResult.IsFailure.Should().BeTrue();
        refreshResult.IsSuccess.Should().BeTrue();
        refreshResult.Value.CurrentStatus.Should().Be(OrderLifecycleStatus.Filled);
        refreshResult.Value.AppliedDomainEffects.Should().BeTrue();

        var position = await positionRepository.GetByPairAsync(pair);
        position.Should().NotBeNull();
        position!.SignalRule.Should().Be("MomentumBreakout");

        var persistedOrder = await queryDispatcher.DispatchAsync<GetOrderQuery, OrderView>(
            new GetOrderQuery { OrderId = orderId });

        persistedOrder.IsSuccess.Should().BeTrue();
        persistedOrder.Value.Status.Should().Be(OrderLifecycleStatus.Filled);
        persistedOrder.Value.CanAffectPosition.Should().BeTrue();

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderPlaced",
                It.Is<string>(details => details.Contains(orderId.Value, StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "OrderFilled",
                It.Is<string>(details => details.Contains(orderId.Value, StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);

        _auditServiceMock.Verify(
            x => x.LogAudit(
                "PositionOpened",
                It.Is<string>(details => details.Contains(pair.Symbol, StringComparison.Ordinal)),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
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

    private static ExchangeOrderResult CreateSubmittedOrder(TradingPair pair)
    {
        return new ExchangeOrderResult
        {
            OrderId = "refresh-open-position-order-1",
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.New,
            RequestedQuantity = Quantity.Create(0.02m),
            FilledQuantity = Quantity.Zero,
            Price = Price.Create(50000m),
            AveragePrice = Price.Zero,
            Cost = Money.Zero("USDT"),
            Fees = Money.Zero("USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static ExchangeOrderInfo CreateFilledOrderInfo(TradingPair pair, string orderId)
    {
        return new ExchangeOrderInfo
        {
            OrderId = orderId,
            Pair = pair,
            Side = IntelliTrader.Application.Ports.Driven.OrderSide.Buy,
            Type = IntelliTrader.Application.Ports.Driven.OrderType.Market,
            Status = IntelliTrader.Application.Ports.Driven.OrderStatus.Filled,
            OriginalQuantity = Quantity.Create(0.02m),
            FilledQuantity = Quantity.Create(0.02m),
            Price = Price.Create(50000m),
            AveragePrice = Price.Create(50000m),
            Fees = Money.Create(1m, "USDT"),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
