using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Queries;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using Moq;

namespace IntelliTrader.Application.Tests.Trading;

public sealed class TradingUseCaseTests
{
    private readonly Mock<ICommandDispatcher> _commandDispatcherMock = new();
    private readonly Mock<IQueryDispatcher> _queryDispatcherMock = new();
    private readonly Mock<IPortfolioRepository> _portfolioRepositoryMock = new();
    private readonly Mock<IPositionRepository> _positionRepositoryMock = new();
    private readonly Mock<IExchangePort> _exchangePortMock = new();

    [Fact]
    public async Task OpenPositionAsync_DispatchesOpenPositionCommand()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new OpenPositionCommand
        {
            Pair = pair,
            Cost = Money.Create(1000m, "USDT"),
            SignalRule = "MomentumBreakout"
        };
        var expected = new OpenPositionResult
        {
            PositionId = PositionId.Create(),
            Pair = pair,
            OrderId = OrderId.From("order-1"),
            EntryPrice = Price.Create(50000m),
            Quantity = Quantity.Create(0.02m),
            Cost = Money.Create(1000m, "USDT"),
            Fees = Money.Create(1m, "USDT"),
            OpenedAt = DateTimeOffset.UtcNow
        };

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<OpenPositionCommand, OpenPositionResult>(
                command,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OpenPositionResult>.Success(expected));

        var useCase = CreateUseCase();

        var result = await useCase.OpenPositionAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
        _commandDispatcherMock.Verify(
            x => x.DispatchAsync<OpenPositionCommand, OpenPositionResult>(
                command,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ClosePositionAsync_DispatchesClosePositionCommand()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var positionId = PositionId.Create();
        var command = new ClosePositionCommand
        {
            PositionId = positionId,
            Reason = CloseReason.TakeProfit
        };
        var expected = CreateClosePositionResult(positionId, pair);

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
                command,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClosePositionResult>.Success(expected));

        var result = await CreateUseCase().ClosePositionAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task ClosePositionByPairAsync_DispatchesClosePositionByPairCommand()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new ClosePositionByPairCommand
        {
            Pair = pair,
            Reason = CloseReason.Manual
        };
        var expected = CreateClosePositionResult(PositionId.Create(), pair);

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<ClosePositionByPairCommand, ClosePositionResult>(
                command,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClosePositionResult>.Success(expected));

        var result = await CreateUseCase().ClosePositionByPairAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteDCAAsync_DispatchesExecuteDcaCommand()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var positionId = PositionId.Create();
        var command = new ExecuteDCACommand
        {
            PositionId = positionId,
            Cost = Money.Create(500m, "USDT")
        };
        var expected = CreateExecuteDcaResult(positionId, pair);

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<ExecuteDCACommand, ExecuteDCAResult>(
                command,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExecuteDCAResult>.Success(expected));

        var result = await CreateUseCase().ExecuteDCAAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteDCAByPairAsync_DispatchesExecuteDcaByPairCommand()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var command = new ExecuteDCAByPairCommand
        {
            Pair = pair,
            Cost = Money.Create(500m, "USDT")
        };
        var expected = CreateExecuteDcaResult(PositionId.Create(), pair);

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<ExecuteDCAByPairCommand, ExecuteDCAResult>(
                command,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ExecuteDCAResult>.Success(expected));

        var result = await CreateUseCase().ExecuteDCAByPairAsync(command);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task CloseAllPositionsAsync_DispatchesCloseForEachActivePosition()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var first = CreatePosition(pair, "order-1");
        var second = CreatePosition(TradingPair.Create("ETHUSDT", "USDT"), "order-2");

        _positionRepositoryMock
            .Setup(x => x.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { first, second });

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
                It.IsAny<ClosePositionCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClosePositionCommand command, CancellationToken _) =>
                Result<ClosePositionResult>.Success(CreateClosePositionResult(command.PositionId, pair)));

        var result = await CreateUseCase().CloseAllPositionsAsync(CloseReason.SystemShutdown);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        _commandDispatcherMock.Verify(
            x => x.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
                It.Is<ClosePositionCommand>(command =>
                    command.PositionId == first.Id &&
                    command.Reason == CloseReason.SystemShutdown),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _commandDispatcherMock.Verify(
            x => x.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
                It.Is<ClosePositionCommand>(command =>
                    command.PositionId == second.Id &&
                    command.Reason == CloseReason.SystemShutdown),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CloseAllPositionsAsync_WhenCloseFails_ReturnsFailure()
    {
        var position = CreatePosition(TradingPair.Create("BTCUSDT", "USDT"), "order-1");

        _positionRepositoryMock
            .Setup(x => x.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { position });

        _commandDispatcherMock
            .Setup(x => x.DispatchAsync<ClosePositionCommand, ClosePositionResult>(
                It.IsAny<ClosePositionCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ClosePositionResult>.Failure(Error.ExchangeError("close failed")));

        var result = await CreateUseCase().CloseAllPositionsAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("close failed");
    }

    [Fact]
    public async Task QueryMethods_DispatchToQueryDispatcher()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var positionView = CreatePositionView("BTCUSDT", 1000m, 1100m, 99m, 9.89m);
        var portfolioView = CreatePortfolioView();
        var statistics = CreatePortfolioStatistics();
        var history = new[] { CreateTradeHistoryEntry(positionView.Id, pair) };

        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetPositionQuery, PositionView>(
                It.IsAny<GetPositionQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PositionView>.Success(positionView));
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetActivePositionsQuery, IReadOnlyList<PositionView>>(
                It.Is<GetActivePositionsQuery>(query => query.Market == "USDT"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PositionView>>.Success(new[] { positionView }));
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetClosedPositionsQuery, IReadOnlyList<PositionView>>(
                It.IsAny<GetClosedPositionsQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PositionView>>.Success(new[] { positionView }));
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetPortfolioQuery, PortfolioView>(
                It.IsAny<GetPortfolioQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PortfolioView>.Success(portfolioView));
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetPortfolioStatisticsQuery, PortfolioStatistics>(
                It.IsAny<GetPortfolioStatisticsQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PortfolioStatistics>.Success(statistics));
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetTradingHistoryQuery, IReadOnlyList<TradeHistoryEntry>>(
                It.IsAny<GetTradingHistoryQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TradeHistoryEntry>>.Success(history));

        var useCase = CreateUseCase();

        (await useCase.GetPositionAsync(new GetPositionQuery { Pair = pair })).Value.Should().Be(positionView);
        (await useCase.GetActivePositionsAsync(new GetActivePositionsQuery { Market = "USDT" })).Value.Should().ContainSingle();
        (await useCase.GetClosedPositionsAsync(new GetClosedPositionsQuery { Pair = pair })).Value.Should().ContainSingle();
        (await useCase.GetPortfolioAsync(new GetPortfolioQuery())).Value.Should().Be(portfolioView);
        (await useCase.GetPortfolioStatisticsAsync(new GetPortfolioStatisticsQuery())).Value.Should().Be(statistics);
        (await useCase.GetTradingHistoryAsync(new GetTradingHistoryQuery { Pair = pair })).Value.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPositionsSummaryAsync_ComposesSummaryFromActivePositionQuery()
    {
        var btc = CreatePositionView("BTCUSDT", 1000m, 1100m, 99m, 9.89m);
        var eth = CreatePositionView("ETHUSDT", 1000m, 900m, -101m, -10.09m);

        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetActivePositionsQuery, IReadOnlyList<PositionView>>(
                It.IsAny<GetActivePositionsQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PositionView>>.Success(new[] { btc, eth }));

        var useCase = CreateUseCase();

        var result = await useCase.GetPositionsSummaryAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalPositions.Should().Be(2);
        result.Value.ProfitablePositions.Should().Be(1);
        result.Value.LosingPositions.Should().Be(1);
        result.Value.TotalInvested.Amount.Should().Be(2000m);
        result.Value.TotalCurrentValue.Amount.Should().Be(2000m);
        result.Value.TotalUnrealizedPnL.Amount.Should().Be(-2m);
        result.Value.BestPerformer.Should().Be(btc.Pair);
        result.Value.WorstPerformer.Should().Be(eth.Pair);
    }

    [Fact]
    public async Task GetPositionsSummaryAsync_WhenNoActivePositions_ReturnsZeroSummary()
    {
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetActivePositionsQuery, IReadOnlyList<PositionView>>(
                It.IsAny<GetActivePositionsQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PositionView>>.Success(Array.Empty<PositionView>()));

        var result = await CreateUseCase().GetPositionsSummaryAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalPositions.Should().Be(0);
        result.Value.TotalInvested.Amount.Should().Be(0m);
        result.Value.AverageMargin.Should().Be(Margin.Zero);
    }

    [Fact]
    public async Task GetPositionsSummaryAsync_WhenActivePositionQueryFails_ReturnsFailure()
    {
        _queryDispatcherMock
            .Setup(x => x.DispatchAsync<GetActivePositionsQuery, IReadOnlyList<PositionView>>(
                It.IsAny<GetActivePositionsQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PositionView>>.Failure(Error.ExchangeError("price failed")));

        var result = await CreateUseCase().GetPositionsSummaryAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("price failed");
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenPortfolioHasCapacityAndFunds_AllowsOpen()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = Portfolio.Create("Default", "USDT", 5000m, 5, 100m);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var useCase = CreateUseCase();

        var result = await useCase.CanOpenPositionAsync(pair, Money.Create(1000m, "USDT"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanOpen.Should().BeTrue();
        result.Value.Pair.Should().Be(pair);
        result.Value.AvailableBalance.Amount.Should().Be(5000m);
        result.Value.CurrentPositionCount.Should().Be(0);
        result.Value.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenPairAlreadyExists_ReturnsValidationErrors()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = Portfolio.Create("Default", "USDT", 5000m, 5, 100m);
        portfolio.RecordPositionOpened(PositionId.Create(), pair, Money.Create(1000m, "USDT"));
        portfolio.ClearDomainEvents();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var useCase = CreateUseCase();

        var result = await useCase.CanOpenPositionAsync(pair, Money.Create(1000m, "USDT"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanOpen.Should().BeFalse();
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenPortfolioMissing_ReturnsFailure()
    {
        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        var result = await CreateUseCase().CanOpenPositionAsync(
            TradingPair.Create("BTCUSDT", "USDT"),
            Money.Create(1000m, "USDT"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenCostViolatesConstraints_ReturnsAllValidationErrors()
    {
        var existingPair = TradingPair.Create("ETHUSDT", "USDT");
        var requestedPair = TradingPair.Create("BTCUSDT", "USDT");
        var portfolio = Portfolio.Create("Default", "USDT", 1000m, 1, 100m);
        portfolio.RecordPositionOpened(PositionId.Create(), existingPair, Money.Create(900m, "USDT"));
        portfolio.ClearDomainEvents();

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var result = await CreateUseCase().CanOpenPositionAsync(requestedPair, Money.Create(200m, "USDT"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanOpen.Should().BeFalse();
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("Insufficient funds", StringComparison.Ordinal));
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("Maximum positions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CanOpenPositionAsync_WhenCurrencyDoesNotMatch_ReturnsValidationError()
    {
        var portfolio = Portfolio.Create("Default", "USDT", 1000m, 5, 100m);

        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var result = await CreateUseCase().CanOpenPositionAsync(
            TradingPair.Create("BTCEUR", "EUR"),
            Money.Create(200m, "EUR"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanOpen.Should().BeFalse();
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CanExecuteDCAAsync_WhenPositionCanDca_ReturnsAllowed()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, "order-btc-1");
        var portfolio = Portfolio.Create("Default", "USDT", 5000m, 5, 100m);
        portfolio.RecordPositionOpened(position.Id, pair, Money.Create(1000m, "USDT"));
        portfolio.ClearDomainEvents();

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        var result = await CreateUseCase().CanExecuteDCAAsync(position.Id, Money.Create(500m, "USDT"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanDCA.Should().BeTrue();
        result.Value.PositionId.Should().Be(position.Id);
        result.Value.CurrentDCALevel.Should().Be(0);
        result.Value.MaxDCALevels.Should().Be(int.MaxValue);
        result.Value.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task CanExecuteDCAAsync_WhenPositionMissing_ReturnsFailure()
    {
        var positionId = PositionId.Create();
        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var result = await CreateUseCase().CanExecuteDCAAsync(positionId, Money.Create(500m, "USDT"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task CanExecuteDCAAsync_WhenExchangePriceFails_ReturnsFailure()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, "order-btc-1");
        var portfolio = Portfolio.Create("Default", "USDT", 5000m, 5, 100m);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Failure(Error.ExchangeError("price failed")));

        var result = await CreateUseCase().CanExecuteDCAAsync(position.Id, Money.Create(500m, "USDT"));

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("price failed");
    }

    [Fact]
    public async Task CanExecuteDCAAsync_WhenPositionCannotDca_ReturnsValidationErrors()
    {
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = CreatePosition(pair, "order-btc-1");
        position.Close(OrderId.From("sell-btc-1"), Price.Create(55000m), Money.Create(1m, "USDT"));
        position.ClearDomainEvents();
        var portfolio = Portfolio.Create("Default", "USDT", 100m, 5, 100m);

        _positionRepositoryMock
            .Setup(x => x.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _portfolioRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);
        _exchangePortMock
            .Setup(x => x.GetCurrentPriceAsync(pair, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Price>.Success(Price.Create(55000m)));

        var result = await CreateUseCase().CanExecuteDCAAsync(position.Id, Money.Create(500m, "EUR"));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanDCA.Should().BeFalse();
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("closed", StringComparison.Ordinal));
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("does not contain active position", StringComparison.Ordinal));
        result.Value.ValidationErrors.Should().Contain(error => error.Contains("does not match", StringComparison.Ordinal));
    }

    private TradingUseCase CreateUseCase()
    {
        return new TradingUseCase(
            _commandDispatcherMock.Object,
            _queryDispatcherMock.Object,
            _portfolioRepositoryMock.Object,
            _positionRepositoryMock.Object,
            _exchangePortMock.Object);
    }

    private static PositionView CreatePositionView(
        string symbol,
        decimal invested,
        decimal currentValue,
        decimal pnl,
        decimal margin)
    {
        var pair = TradingPair.Create(symbol, "USDT");
        return new PositionView
        {
            Id = PositionId.Create(),
            Pair = pair,
            AveragePrice = Price.Create(invested),
            CurrentPrice = Price.Create(currentValue),
            TotalQuantity = Quantity.Create(1m),
            TotalCost = Money.Create(invested, "USDT"),
            TotalFees = Money.Zero("USDT"),
            CurrentValue = Money.Create(currentValue, "USDT"),
            UnrealizedPnL = Money.Create(pnl, "USDT"),
            CurrentMargin = Margin.FromPercentage(margin),
            DCALevel = 0,
            EntryCount = 1,
            OpenedAt = DateTimeOffset.UtcNow.AddHours(-2),
            HoldingPeriod = TimeSpan.FromHours(2),
            IsClosed = false
        };
    }

    private static Position CreatePosition(TradingPair pair, string orderId)
    {
        var position = Position.Open(
            pair,
            OrderId.From(orderId),
            Price.Create(50000m),
            Quantity.Create(0.02m),
            Money.Create(1m, pair.QuoteCurrency));

        position.ClearDomainEvents();
        return position;
    }

    private static ClosePositionResult CreateClosePositionResult(PositionId positionId, TradingPair pair)
    {
        return new ClosePositionResult
        {
            PositionId = positionId,
            Pair = pair,
            SellOrderId = OrderId.From($"sell-{positionId.Value:N}"),
            SellPrice = Price.Create(55000m),
            Quantity = Quantity.Create(0.02m),
            Proceeds = Money.Create(1100m, pair.QuoteCurrency),
            Fees = Money.Create(1m, pair.QuoteCurrency),
            TotalCost = Money.Create(1000m, pair.QuoteCurrency),
            RealizedPnL = Money.Create(99m, pair.QuoteCurrency),
            RealizedMargin = Margin.FromPercentage(9.89m),
            HoldingPeriod = TimeSpan.FromHours(2),
            Reason = CloseReason.Manual,
            ClosedAt = DateTimeOffset.UtcNow
        };
    }

    private static ExecuteDCAResult CreateExecuteDcaResult(PositionId positionId, TradingPair pair)
    {
        return new ExecuteDCAResult
        {
            PositionId = positionId,
            Pair = pair,
            OrderId = OrderId.From($"dca-{positionId.Value:N}"),
            EntryPrice = Price.Create(49000m),
            Quantity = Quantity.Create(0.01m),
            Cost = Money.Create(500m, pair.QuoteCurrency),
            Fees = Money.Create(0.5m, pair.QuoteCurrency),
            NewDCALevel = 1,
            NewAveragePrice = Price.Create(49666.66666667m),
            NewTotalQuantity = Quantity.Create(0.03m),
            NewTotalCost = Money.Create(1500m, pair.QuoteCurrency),
            ExecutedAt = DateTimeOffset.UtcNow
        };
    }

    private static PortfolioView CreatePortfolioView()
    {
        return new PortfolioView
        {
            Id = PortfolioId.Create(),
            Name = "Default",
            Market = "USDT",
            TotalBalance = Money.Create(5000m, "USDT"),
            AvailableBalance = Money.Create(4000m, "USDT"),
            ReservedBalance = Money.Create(1000m, "USDT"),
            ActivePositionCount = 1,
            MaxPositions = 5,
            MinPositionCost = Money.Create(100m, "USDT"),
            CanOpenNewPosition = true,
            AvailablePercentage = 80m,
            ReservedPercentage = 20m,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static PortfolioStatistics CreatePortfolioStatistics()
    {
        return new PortfolioStatistics
        {
            PortfolioId = PortfolioId.Create(),
            TotalBalance = Money.Create(5000m, "USDT"),
            AvailableBalance = Money.Create(4000m, "USDT"),
            InvestedBalance = Money.Create(1000m, "USDT"),
            ActivePositions = 1,
            MaxPositions = 5,
            PositionSlotsAvailable = 4,
            TotalUnrealizedPnL = Money.Create(99m, "USDT"),
            TotalRealizedPnL = Money.Zero("USDT"),
            OverallMargin = Margin.FromPercentage(9.89m),
            TotalTradesCount = 1,
            WinningTradesCount = 1,
            LosingTradesCount = 0,
            WinRate = 100m,
            AverageWin = Money.Create(99m, "USDT"),
            AverageLoss = Money.Zero("USDT"),
            ProfitFactor = 0m,
            MaxDrawdown = Money.Zero("USDT"),
            MaxDrawdownPercent = 0m,
            Sharpe = 0m,
            StatisticsAsOf = DateTimeOffset.UtcNow,
            AverageHoldingPeriod = TimeSpan.FromHours(2)
        };
    }

    private static TradeHistoryEntry CreateTradeHistoryEntry(PositionId positionId, TradingPair pair)
    {
        return new TradeHistoryEntry
        {
            PositionId = positionId,
            Pair = pair,
            Type = TradeType.Buy,
            Price = Price.Create(50000m),
            Quantity = Quantity.Create(0.02m),
            Cost = Money.Create(1000m, pair.QuoteCurrency),
            Fees = Money.Create(1m, pair.QuoteCurrency),
            Timestamp = DateTimeOffset.UtcNow,
            OrderId = "order-1"
        };
    }
}
