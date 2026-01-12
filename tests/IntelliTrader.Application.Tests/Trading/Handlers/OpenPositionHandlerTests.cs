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

public class OpenPositionHandlerTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock;
    private readonly Mock<IPositionRepository> _positionRepositoryMock;
    private readonly Mock<IExchangePort> _exchangePortMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<INotificationPort> _notificationPortMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly TradingConstraintValidator _constraintValidator;
    private readonly OpenPositionHandler _handler;

    public OpenPositionHandlerTests()
    {
        _portfolioRepositoryMock = new Mock<IPortfolioRepository>();
        _positionRepositoryMock = new Mock<IPositionRepository>();
        _exchangePortMock = new Mock<IExchangePort>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _notificationPortMock = new Mock<INotificationPort>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _constraintValidator = new TradingConstraintValidator();

        _handler = new OpenPositionHandler(
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object,
            _eventDispatcherMock.Object,
            _constraintValidator,
            _unitOfWorkMock.Object,
            _notificationPortMock.Object);
    }

    private static Portfolio CreateTestPortfolio(
        decimal balance = 10000m,
        int maxPositions = 5,
        decimal minPositionCost = 10m)
    {
        return Portfolio.Create("Test", "USDT", balance, maxPositions, minPositionCost);
    }

    private static TradingPairRules CreateTestRules(string pair = "BTCUSDT")
    {
        return new TradingPairRules
        {
            Pair = TradingPair.Create(pair, "USDT"),
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

    private static ExchangeOrderResult CreateTestOrderResult(
        TradingPair pair,
        decimal price = 50000m,
        decimal quantity = 0.02m,
        decimal fees = 1m)
    {
        var cost = price * quantity;
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

    private void SetupDefaultMocks(Portfolio portfolio, TradingPair pair)
    {
        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTestRules(pair.Symbol)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(50000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateTestOrderResult(pair)));

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
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Pair.Should().Be(pair);
        result.Value.PositionId.Should().NotBeNull();
        result.Value.Cost.Amount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_SavesPosition()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

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
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _portfolioRepositoryMock.Verify(
            x => x.SaveAsync(It.IsAny<Portfolio>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_CommitsUnitOfWork()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _unitOfWorkMock.Verify(
            x => x.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_DispatchesDomainEvents()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

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
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        _notificationPortMock.Verify(
            x => x.SendAsync(It.Is<Notification>(n => n.Type == NotificationType.Trade), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithSignalRule_IncludesRuleInPosition()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT"),
            SignalRule = "BuySignal1"
        };

        SetupDefaultMocks(portfolio, pair);
        Position? savedPosition = null;
        _positionRepositoryMock
            .Setup(x => x.SaveAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<Position, CancellationToken>((p, _) => savedPosition = p);

        // Act
        await _handler.HandleAsync(command);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.SignalRule.Should().Be("BuySignal1");
    }

    #endregion

    #region Validation Failures

    [Fact]
    public async Task HandleAsync_WhenPortfolioNotFound_ReturnsFailure()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

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
    public async Task HandleAsync_WithInsufficientFunds_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(balance: 100m);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT") // More than available
        };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("Insufficient funds");
    }

    [Fact]
    public async Task HandleAsync_AtMaxPositions_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(maxPositions: 1);
        var existingPair = TradingPair.Create("ETHUSDT", "USDT");
        portfolio.RecordPositionOpened(PositionId.Create(), existingPair, Money.Create(500m, "USDT"));

        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT")
        };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("Maximum positions");
    }

    [Fact]
    public async Task HandleAsync_WithExistingPosition_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        portfolio.RecordPositionOpened(PositionId.Create(), pair, Money.Create(500m, "USDT"));

        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT")
        };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("Position already exists");
    }

    [Fact]
    public async Task HandleAsync_BelowMinimumCost_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio(minPositionCost: 100m);
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(50m, "USDT") // Below minimum
        };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        result.Error.Message.Should().Contain("below minimum");
    }

    #endregion

    #region Exchange Failures

    [Fact]
    public async Task HandleAsync_WhenGetTradingRulesFails_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Failure(Error.ExchangeError("Exchange unavailable")));

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
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        var rules = CreateTestRules(pair.Symbol) with { IsTradingEnabled = false };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(rules));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not enabled");
    }

    [Fact]
    public async Task HandleAsync_WhenOrderFails_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTestRules(pair.Symbol)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(50000m)));

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
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        var orderResult = CreateTestOrderResult(pair) with { Status = OrderStatus.Canceled };

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _exchangePortMock
            .Setup(x => x.GetTradingRulesAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TradingPairRules>.Success(CreateTestRules(pair.Symbol)));

        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(50000m)));

        _exchangePortMock
            .Setup(x => x.PlaceMarketBuyAsync(pair, It.IsAny<Money>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(orderResult));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("not filled");
    }

    #endregion

    #region Persistence Failures

    [Fact]
    public async Task HandleAsync_WhenCommitFails_ReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

        _unitOfWorkMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExchangeError("Database error")));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrows_RollsBackAndReturnsFailure()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

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

    #region Notification Handling

    [Fact]
    public async Task HandleAsync_WhenNotificationFails_StillReturnsSuccess()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT")
        };

        SetupDefaultMocks(portfolio, pair);

        _notificationPortMock
            .Setup(x => x.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Notification failed"));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue(); // Notification failure shouldn't fail the operation
    }

    #endregion

    #region Limit Orders

    [Fact]
    public async Task HandleAsync_WithLimitOrder_PlacesLimitOrder()
    {
        // Arrange
        var portfolio = CreateTestPortfolio();
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var maxPrice = Price.Create(49000m);
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT"),
            UseLimitOrder = true,
            MaxPrice = maxPrice
        };

        SetupDefaultMocks(portfolio, pair);

        _exchangePortMock
            .Setup(x => x.PlaceLimitBuyAsync(pair, It.IsAny<Quantity>(), maxPrice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExchangeOrderResult>.Success(CreateTestOrderResult(pair, price: 49000m)));

        // Act
        var result = await _handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _exchangePortMock.Verify(
            x => x.PlaceLimitBuyAsync(pair, It.IsAny<Quantity>(), maxPrice, It.IsAny<CancellationToken>()),
            Times.Once);
        _exchangePortMock.Verify(
            x => x.PlaceMarketBuyAsync(It.IsAny<TradingPair>(), It.IsAny<Money>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
