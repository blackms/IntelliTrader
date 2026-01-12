using FluentAssertions;
using Moq;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Application.Tests.Trading.Handlers;

public class ClosePositionHandlerTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock;
    private readonly Mock<IPositionRepository> _positionRepositoryMock;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<INotificationPort> _notificationPortMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ClosePositionHandler _handler;

    public ClosePositionHandlerTests()
    {
        _portfolioRepositoryMock = new Mock<IPortfolioRepository>();
        _positionRepositoryMock = new Mock<IPositionRepository>();
        _exchangePortMock = new Mock<IExchangePort>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _notificationPortMock = new Mock<INotificationPort>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _constraintValidator = new TradingConstraintValidator();

        _handler = new ClosePositionHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object,
            _eventDispatcherMock.Object,
            _constraintValidator,
            _unitOfWorkMock.Object,
            _notificationPortMock.Object);
    }

    private static Portfolio CreateTestPortfolio(decimal balance = 10000m)
    {
        return Portfolio.Create("Test", "USDT", balance, 5, 10m);
    }

    private static Position CreateTestPosition(
        string pair = "BTCUSDT",
        decimal entryPrice = 50000m,
        decimal quantity = 0.02m,
        decimal fees = 1m)
    {
        return Position.Open(
            TradingPair.Create(pair, "USDT"),
            OrderId.From("buy-order-1"),
            Price.Create(entryPrice),
            Quantity.Create(quantity),
            Money.Create(fees, "USDT"));
    }

    private static ExchangeOrderResult CreateTestSellOrderResult(
        TradingPair pair,
        decimal price = 55000m,
        decimal quantity = 0.02m,
        decimal fees = 1m)
    {
        var proceeds = price * quantity;
        return new ExchangeOrderResult
        {
            OrderId = Guid.NewGuid().ToString(),
            Pair = pair,
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Status = OrderStatus.Filled,
            RequestedQuantity = Quantity.Create(quantity),
            FilledQuantity = Quantity.Create(quantity),
            Price = Price.Create(price),
            AveragePrice = Price.Create(price),
            Cost = Money.Create(proceeds, "USDT"), // For sells, cost = proceeds
            Fees = Money.Create(fees, "USDT"),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private void SetupDefaultMocks(Position position, Portfolio portfolio)
    {
        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(position.Pair, position.TotalQuantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(
                CreateTestSellOrderResult(position.Pair, quantity: position.TotalQuantity.Value)));

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _notificationPortMock
            .Setup(x => x.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
    }

    #region Success Cases

    [Fact]
    public async Task HandleAsync_WithValidCommand_ReturnsSuccess()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id,
            Reason = CloseReason.Manual
        };

        SetupDefaultMocks(position, portfolio);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.Pair.Should().Be(position.Pair);
        result.Value.Reason.Should().Be(CloseReason.Manual);
    }

    [Fact]
    public async Task HandleAsync_WithProfit_ReturnsPositivePnL()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 50000m, quantity: 0.02m, fees: 1m);
        // Cost = 50000 * 0.02 = 1000, plus 1 fee = 1001
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id,
            Reason = CloseReason.TakeProfit
        };

        SetupDefaultMocks(position, portfolio);

        // Sell at higher price: 55000 * 0.02 = 1100 proceeds, minus 1 fee
        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(position.Pair, position.TotalQuantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(
                CreateTestSellOrderResult(position.Pair, price: 55000m, quantity: 0.02m, fees: 1m)));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RealizedPnL.Amount.Should().BeGreaterThan(0);
        result.Value.RealizedMargin.IsProfit.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithLoss_ReturnsNegativePnL()
    {
        // Arrange
        var position = CreateTestPosition(entryPrice: 50000m, quantity: 0.02m, fees: 1m);
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id,
            Reason = CloseReason.StopLoss
        };

        SetupDefaultMocks(position, portfolio);

        // Sell at lower price: 45000 * 0.02 = 900 proceeds
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(45000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(position.Pair, position.TotalQuantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(
                CreateTestSellOrderResult(position.Pair, price: 45000m, quantity: 0.02m, fees: 1m)));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RealizedPnL.Amount.Should().BeLessThan(0);
        result.Value.RealizedMargin.IsLoss.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SavesClosedPosition()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

        Position? savedPosition = null;
        _positionRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<Position, CancellationToken>((p, _) => savedPosition = p);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.IsClosed.Should().BeTrue();
        savedPosition.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_UpdatesPortfolio()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(It.IsAny<Portfolio>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DispatchesDomainEvents()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _eventDispatcherMock.Verify(
            x => x.DispatchManyAsync(It.IsAny<IEnumerable<IDomainEvent>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_SendsNotification()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id,
            Reason = CloseReason.TakeProfit
        };

        SetupDefaultMocks(position, portfolio);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _notificationPortMock.Verify(
            x => x.SendAsync(
                It.Is<Notification>(n => n.Type == NotificationType.Trade && n.Title!.Contains("Closed")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithStopLoss_SendsHighPriorityNotification()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id,
            Reason = CloseReason.StopLoss
        };

        SetupDefaultMocks(position, portfolio);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _notificationPortMock.Verify(
            x => x.SendAsync(
                It.Is<Notification>(n => n.Priority == NotificationPriority.High),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_CalculatesHoldingPeriod()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HoldingPeriod.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    #endregion

    #region Failure Cases

    [Fact]
    public async Task HandleAsync_WhenPositionNotFound_ReturnsFailure()
    {
        // Arrange
        var positionId = PositionId.Create();
        var command = new ClosePositionCommand
        {
            PositionId = positionId
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
    public async Task HandleAsync_WhenPositionAlreadyClosed_ReturnsFailure()
    {
        // Arrange
        var position = CreateTestPosition();
        // Close the position first
        position.Close(OrderId.From("sell-order"), Price.Create(55000m), Money.Create(1m, "USDT"));

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Conflict");
        result.Error.Message.Should().Contain("already closed");
    }

    [Fact]
    public async Task HandleAsync_WhenPortfolioNotFound_ReturnsFailure()
    {
        // Arrange
        var position = CreateTestPosition();
        var command = new ClosePositionCommand
        {
            PositionId = position.Id
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
    public async Task HandleAsync_WhenGetPriceFails_ReturnsFailure()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("Price unavailable")));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ExchangeError");
    }

    [Fact]
    public async Task HandleAsync_WhenSellOrderFails_ReturnsFailure()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(position.Pair, position.TotalQuantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Failure(Error.ExchangeError("Sell failed")));

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
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        var canceledOrder = CreateTestSellOrderResult(position.Pair) with { Status = OrderStatus.Canceled };

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(position.Pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(position.Pair, position.TotalQuantity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(canceledOrder));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not filled");
    }

    [Fact]
    public async Task HandleAsync_WhenCommitFails_ReturnsFailure()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExchangeError("Commit failed")));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrows_RollsBack()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

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

    #region Limit Orders

    [Fact]
    public async Task HandleAsync_WithLimitOrder_PlacesLimitSell()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var minPrice = Price.Create(56000m);
        var command = new ClosePositionCommand
        {
            PositionId = position.Id,
            UseLimitOrder = true,
            MinPrice = minPrice
        };

        SetupDefaultMocks(position, portfolio);

        _exchangePortMock
            .Setup(x => x.PlaceLimitSellAsync(position.Pair, position.TotalQuantity, minPrice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(
                CreateTestSellOrderResult(position.Pair, price: 56000m)));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _exchangePortMock.Verify(
            x => x.PlaceLimitSellAsync(position.Pair, position.TotalQuantity, minPrice, It.IsAny<CancellationToken>()),
            Times.Once);
        _exchangePortMock.Verify(
            x => x.PlaceMarketSellAsync(It.IsAny<TradingPair>(), It.IsAny<Quantity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Notification Handling

    [Fact]
    public async Task HandleAsync_WhenNotificationFails_StillReturnsSuccess()
    {
        // Arrange
        var position = CreateTestPosition();
        var portfolio = CreateTestPortfolio();
        portfolio.RecordPositionOpened(position.Id, position.Pair, position.TotalCost);

        var command = new ClosePositionCommand
        {
            PositionId = position.Id
        };

        SetupDefaultMocks(position, portfolio);

        _notificationPortMock
            .Setup(x => x.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Notification failed"));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}

public class ClosePositionByPairHandlerTests
{
    private readonly Mock<IPositionRepository> _positionRepositoryMock;
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly ClosePositionHandler _closePositionHandler;

    public ClosePositionByPairHandlerTests()
    {
        _positionRepositoryMock = new Mock<IPositionRepository>();
        _portfolioRepositoryMock = new Mock<IPortfolioRepository>();
        _exchangePortMock = new Mock<IExchangePort>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _constraintValidator = new TradingConstraintValidator();

        _closePositionHandler = new ClosePositionHandler(
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
        var command = new ClosePositionByPairCommand
        {
            Pair = pair
        };

        _positionRepositoryMock
            .Setup(x => x.GetByPairAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var handler = new ClosePositionByPairHandler(
            _positionRepositoryMock.Object,
            _closePositionHandler);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task HandleAsync_WhenPositionFound_DelegatesToClosePositionHandler()
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

        var command = new ClosePositionByPairCommand
        {
            Pair = pair,
            Reason = CloseReason.Manual
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
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketSellAsync(pair, It.IsAny<Quantity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(new ExchangeOrderResult
            {
                OrderId = "sell-order-123",
                Pair = pair,
                Side = OrderSide.Sell,
                Type = OrderType.Market,
                Status = OrderStatus.Filled,
                RequestedQuantity = Quantity.Create(0.1m),
                FilledQuantity = Quantity.Create(0.1m),
                Price = Price.Create(55000m),
                AveragePrice = Price.Create(55000m),
                Cost = Money.Create(5500m, "USDT"),
                Fees = Money.Create(5.5m, "USDT"),
                Timestamp = DateTimeOffset.UtcNow
            }));

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new ClosePositionByPairHandler(
            _positionRepositoryMock.Object,
            _closePositionHandler);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Pair.Should().Be(pair);
    }
}
