using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public class ExecuteDCAHandlerTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock;
    private readonly Mock<IPositionRepository> _positionRepositoryMock;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<INotificationPort> _notificationPortMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ExecuteDCAHandler _handler;

    public ExecuteDCAHandlerTests()
    {
        _portfolioRepositoryMock = new Mock<IPortfolioRepository>();
        _positionRepositoryMock = new Mock<IPositionRepository>();
        _exchangePortMock = new Mock<IExchangePort>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _notificationPortMock = new Mock<INotificationPort>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _constraintValidator = new TradingConstraintValidator();

        _handler = new ExecuteDCAHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object,
            _eventDispatcherMock.Object,
            _constraintValidator,
            _unitOfWorkMock.Object,
            _notificationPortMock.Object);
    }

    #region Helper Methods

    private static Portfolio CreatePortfolio(decimal balance = 10000m, int maxPositions = 5)
    {
        return Portfolio.Create("Test", "USDT", balance, maxPositions, 10m);
    }

    private static Position CreateOpenPosition(
        TradingPair pair,
        decimal entryPrice = 50000m,
        decimal quantity = 0.1m,
        decimal fees = 5m)
    {
        return Position.Open(
            pair,
            OrderId.From(Guid.NewGuid().ToString()),
            Price.Create(entryPrice),
            Quantity.Create(quantity),
            Money.Create(fees, "USDT"),
            "Test");
    }

    private static ExchangeOrderResult CreateFilledBuyOrder(
        TradingPair pair,
        decimal price,
        decimal quantity,
        decimal cost,
        decimal fees)
    {
        return new ExchangeOrderResult
        {
            OrderId = Guid.NewGuid().ToString(),
            Pair = pair,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Status = OrderStatus.Filled,
            RequestedQuantity = Quantity.Create(quantity),
            FilledQuantity = Quantity.Create(quantity),
            Price = Price.Create(price),
            AveragePrice = Price.Create(price),
            Cost = Money.Create(cost, "USDT"),
            Fees = Money.Create(fees, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static TradingPairRules CreateDefaultRules(TradingPair? pair = null)
    {
        return new TradingPairRules
        {
            Pair = pair ?? TradingPair.Create("BTCUSDT", "USDT"),
            IsTradingEnabled = true,
            MinQuantity = 0.001m,
            MaxQuantity = 1000m,
            QuantityStepSize = 0.00001m,
            QuantityPrecision = 5,
            MinPrice = 0.01m,
            MaxPrice = 1000000m,
            PricePrecision = 2,
            MinOrderValue = 10m
        };
    }

    private static TradingPairRules CreateDisabledRules(TradingPair? pair = null)
    {
        return new TradingPairRules
        {
            Pair = pair ?? TradingPair.Create("BTCUSDT", "USDT"),
            IsTradingEnabled = false,
            MinQuantity = 0.001m,
            MaxQuantity = 1000m,
            QuantityStepSize = 0.00001m,
            QuantityPrecision = 5,
            MinPrice = 0.01m,
            MaxPrice = 1000000m,
            PricePrecision = 2,
            MinOrderValue = 10m
        };
    }

    private void SetupDefaultMocks(
        Position position,
        Portfolio portfolio,
        decimal currentPrice = 45000m,
        decimal dcaCost = 500m)
    {
        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(currentPrice)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateDefaultRules()));

        var filledQuantity = dcaCost / currentPrice;
        var orderResult = CreateFilledBuyOrder(position.Pair, currentPrice, filledQuantity, dcaCost, dcaCost * 0.001m);

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(position.Pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(orderResult));

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    #endregion

    #region Success Cases

    [Fact]
    public async Task HandleAsync_WithValidCommand_ReturnsSuccess()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.Pair.Should().Be(pair);
        result.Value.NewDCALevel.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_UpdatesPositionDCALevel()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewDCALevel.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_UpdatesAveragePrice()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // DCA at lower price should lower the average price
        result.Value.NewAveragePrice.Value.Should().BeLessThan(50000m);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_IncreasesTotalQuantity()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var initialQuantity = position.TotalQuantity.Value;
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewTotalQuantity.Value.Should().BeGreaterThan(initialQuantity);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_SavesPosition()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _positionRepositoryMock.Verify(
            x => x.SaveAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_SavesPortfolio()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(It.IsAny<Portfolio>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_DispatchesDomainEvents()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _eventDispatcherMock.Verify(
            x => x.DispatchManyAsync(It.IsAny<IEnumerable<IDomainEvent>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_SendsNotification()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _notificationPortMock.Verify(
            x => x.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithLimitOrder_PlacesLimitBuyOrder()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var maxPrice = Price.Create(44000m);
        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT"),
            UseLimitOrder = true,
            MaxPrice = maxPrice
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        var limitOrderResult = CreateFilledBuyOrder(pair, 44000m, 500m / 44000m, 500m, 0.5m);
        _exchangePortMock
            .Setup(x => x.PlaceLimitBuyAsync(pair, It.IsAny<Quantity>(), maxPrice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(limitOrderResult));

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _exchangePortMock.Verify(
            x => x.PlaceLimitBuyAsync(pair, It.IsAny<Quantity>(), maxPrice, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Failure Cases

    [Fact]
    public async Task HandleAsync_WhenPositionNotFound_ReturnsFailure()
    {
        // Arrange
        var positionId = PositionId.Create();
        var command = new ExecuteDCACommand
        {
            PositionId = positionId,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task HandleAsync_WhenPositionIsClosed_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        position.Close(OrderId.From("sell-order"), Price.Create(55000m), Money.Create(5.5m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Conflict");
    }

    [Fact]
    public async Task HandleAsync_WhenPortfolioNotFound_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task HandleAsync_WhenPriceFetchFails_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("Failed to get price")));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task HandleAsync_WhenTradingDisabled_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(45000m)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateDisabledRules(pair)));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("not enabled");
    }

    [Fact]
    public async Task HandleAsync_WhenOrderBelowMinimum_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(5m, "USDT") // Below minimum
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(45000m)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateDefaultRules()));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("below minimum");
    }

    [Fact]
    public async Task HandleAsync_WhenMarketBuyFails_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(45000m)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateDefaultRules()));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Failure(Error.ExchangeError("Order rejected")));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task HandleAsync_WhenOrderNotFilled_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(45000m)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateDefaultRules()));

        var unfilledOrder = new ExchangeOrderResult
        {
            OrderId = Guid.NewGuid().ToString(),
            Pair = pair,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Status = OrderStatus.Rejected,
            RequestedQuantity = Quantity.Create(0.01m),
            FilledQuantity = Quantity.Zero,
            Price = Price.Create(45000m),
            AveragePrice = Price.Zero,
            Cost = Money.Zero("USDT"),
            Fees = Money.Zero("USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(unfilledOrder));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
        result.Error.Message.Should().Contain("not filled");
    }

    [Fact]
    public async Task HandleAsync_WhenCommitFails_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExchangeError("Commit failed")));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrows_RollsBackAndReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        _positionRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Save failed"));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        _unitOfWorkMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Notification Cases

    [Fact]
    public async Task HandleAsync_WithoutNotificationPort_DoesNotThrow()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        var handlerWithoutNotification = new ExecuteDCAHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object,
            _eventDispatcherMock.Object,
            _constraintValidator,
            _unitOfWorkMock.Object);

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        // Act
        var result = await handlerWithoutNotification.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenNotificationFails_StillReturnsSuccess()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);
        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 45000m, 500m);

        _notificationPortMock
            .Setup(x => x.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Notification failed"));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Multiple DCA Entries

    [Fact]
    public async Task HandleAsync_MultipleDCAExecutions_IncrementsDCALevel()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreateOpenPosition(pair, 50000m, 0.1m);

        // Add first DCA entry
        position.AddDCAEntry(
            OrderId.From("dca-1"),
            Price.Create(45000m),
            Quantity.Create(0.011m),
            Money.Create(0.5m, "USDT"));

        var portfolio = CreatePortfolio(10000m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));
        portfolio.RecordPositionCostIncreased(position.Id, pair, Money.Create(500m, "USDT"));

        var command = new ExecuteDCACommand
        {
            PositionId = position.Id,
            Cost = Money.Create(500m, "USDT")
        };

        SetupDefaultMocks(position, portfolio, 40000m, 500m);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewDCALevel.Should().Be(2); // Initial + first DCA + second DCA
    }

    #endregion
}

public class ExecuteDCAByPairHandlerTests
{
    private readonly Mock<IPositionRepository> _positionRepositoryMock;
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ExecuteDCAHandler _executeDCAHandler;

    public ExecuteDCAByPairHandlerTests()
    {
        _positionRepositoryMock = new Mock<IPositionRepository>();
        _portfolioRepositoryMock = new Mock<IPortfolioRepository>();
        _exchangePortMock = new Mock<IExchangePort>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _constraintValidator = new TradingConstraintValidator();

        _executeDCAHandler = new ExecuteDCAHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object,
            _eventDispatcherMock.Object,
            _constraintValidator,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task HandleAsync_WhenPositionNotFound_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new ExecuteDCAByPairCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByPairAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var handler = new ExecuteDCAByPairHandler(
            _positionRepositoryMock.Object,
            _executeDCAHandler);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task HandleAsync_WhenPositionFound_DelegatesToExecuteDCAHandler()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order-123"),
            Price.Create(50000m),
            Quantity.Create(0.1m),
            Money.Create(5m, "USDT"),
            "Test");
        var portfolio = Portfolio.Create("Test", "USDT", 10000m, 5, 10m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(5000m, "USDT"));

        var command = new ExecuteDCAByPairCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT")
        };

        _positionRepositoryMock
            .Setup(x => x.GetByPairAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(45000m)));

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(new TradingPairRules
            {
                Pair = pair,
                IsTradingEnabled = true,
                MinQuantity = 0.001m,
                MaxQuantity = 1000m,
                QuantityStepSize = 0.00001m,
                QuantityPrecision = 5,
                MinPrice = 0.01m,
                MaxPrice = 1000000m,
                PricePrecision = 2,
                MinOrderValue = 10m
            }));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(new ExchangeOrderResult
            {
                OrderId = "dca-order-123",
                Pair = pair,
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Status = OrderStatus.Filled,
                RequestedQuantity = Quantity.Create(0.011m),
                FilledQuantity = Quantity.Create(0.011m),
                Price = Price.Create(45000m),
                AveragePrice = Price.Create(45000m),
                Cost = Money.Create(500m, "USDT"),
                Fees = Money.Create(0.5m, "USDT"),
                Timestamp = DateTimeOffset.UtcNow
            }));

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ExecuteDCAByPairHandler(
            _positionRepositoryMock.Object,
            _executeDCAHandler);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Pair.Should().Be(pair);
    }
}
