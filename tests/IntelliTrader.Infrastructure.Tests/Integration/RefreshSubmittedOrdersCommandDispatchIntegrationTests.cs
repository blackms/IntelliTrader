using Autofac;
using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class RefreshSubmittedOrdersCommandDispatchIntegrationTests : IClassFixture<InfrastructureTestFixture>, IDisposable
{
    private readonly Mock<IExchangePort> _exchangePortMock = new();
    private readonly Mock<IAuditService> _auditServiceMock = new();
    private readonly IContainer _container;

    public RefreshSubmittedOrdersCommandDispatchIntegrationTests(InfrastructureTestFixture fixture)
    {
        var positionsPath = fixture.CreateTempFilePath("refresh_submitted_positions");
        var portfoliosPath = fixture.CreateTempFilePath("refresh_submitted_portfolios");
        var ordersPath = fixture.CreateTempFilePath("refresh_submitted_orders");
        var transactionCoordinator = new IntelliTrader.Infrastructure.Transactions.JsonTransactionCoordinator();

        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(transactionCoordinator).SingleInstance();

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
    public async Task DispatchAsync_WhenSubmittedOrdersExist_RefreshesThemInBatch()
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
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateSubmittedOrder(pair, "batch-refresh-order-1")));

        var dispatcher = _container.Resolve<ICommandDispatcher>();
        var positionRepository = _container.Resolve<IPositionRepository>();

        // When
        var submitResult = await dispatcher.DispatchAsync<OpenPositionCommand, OpenPositionResult>(openCommand);

        _exchangePortMock
            .Setup(x => x.GetOrderAsync(pair, "batch-refresh-order-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderInfo>.Success(CreateFilledOrderInfo(pair, "batch-refresh-order-1")));

        var refreshResult = await dispatcher.DispatchAsync<RefreshSubmittedOrdersCommand, RefreshSubmittedOrdersResult>(
            new RefreshSubmittedOrdersCommand { Limit = 10 });

        // Then
        submitResult.IsFailure.Should().BeTrue();
        refreshResult.IsSuccess.Should().BeTrue();
        refreshResult.Value.TotalSubmitted.Should().Be(1);
        refreshResult.Value.AttemptedCount.Should().Be(1);
        refreshResult.Value.RefreshedCount.Should().Be(1);
        refreshResult.Value.AppliedDomainEffectsCount.Should().Be(1);
        refreshResult.Value.FailedCount.Should().Be(0);

        var position = await positionRepository.GetByPairAsync(pair);
        position.Should().NotBeNull();
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

    private static ExchangeOrderResult CreateSubmittedOrder(TradingPair pair, string orderId)
    {
        return new ExchangeOrderResult
        {
            OrderId = orderId,
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
