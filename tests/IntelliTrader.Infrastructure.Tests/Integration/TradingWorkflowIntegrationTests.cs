using FluentAssertions;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Trailing;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

/// <summary>
/// Integration tests for complete trading workflows.
/// Tests the interaction between repositories, trailing manager, and background services.
/// </summary>
public class TradingWorkflowIntegrationTests : IClassFixture<InfrastructureTestFixture>, IDisposable
{
    private readonly InfrastructureTestFixture _fixture;
    private readonly JsonPositionRepository _repository;
    private readonly TrailingManager _trailingManager;
    private readonly string _repositoryPath;

    public TradingWorkflowIntegrationTests(InfrastructureTestFixture fixture)
    {
        _fixture = fixture;
        _repositoryPath = fixture.CreateTempFilePath("trading_workflow");
        _repository = new JsonPositionRepository(_repositoryPath);
        _trailingManager = fixture.CreateTrailingManager();
    }

    public void Dispose()
    {
        _repository.Dispose();
    }

    #region Position Lifecycle Tests

    [Fact]
    public async Task CompletePositionLifecycle_OpenDCAAndClose_PersistsCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var initialPrice = Price.Create(50000m);
        var quantity = Quantity.Create(0.1m);
        var fees = Money.Create(5m, "USDT");

        // Act - Open position
        var position = Position.Open(pair, OrderId.From("order1"), initialPrice, quantity, fees, "TestRule");
        await _repository.SaveAsync(position);

        // Verify initial state
        var savedPosition = await _repository.GetByIdAsync(position.Id);
        savedPosition.Should().NotBeNull();
        savedPosition!.DCALevel.Should().Be(0);
        savedPosition.Entries.Should().HaveCount(1);

        // Act - Add DCA
        var dcaPrice = Price.Create(45000m);
        var dcaQuantity = Quantity.Create(0.15m);
        var dcaFees = Money.Create(6.75m, "USDT");
        position.AddDCAEntry(OrderId.From("order2"), dcaPrice, dcaQuantity, dcaFees);
        await _repository.SaveAsync(position);

        // Verify DCA state
        savedPosition = await _repository.GetByIdAsync(position.Id);
        savedPosition.Should().NotBeNull();
        savedPosition!.DCALevel.Should().Be(1);
        savedPosition.Entries.Should().HaveCount(2);
        savedPosition.TotalQuantity.Value.Should().Be(0.25m);

        // Act - Close position
        var sellPrice = Price.Create(52000m);
        var sellFees = Money.Create(13m, "USDT");
        position.Close(OrderId.From("sell1"), sellPrice, sellFees);
        await _repository.SaveAsync(position);

        // Verify closed state
        savedPosition = await _repository.GetByIdAsync(position.Id);
        savedPosition.Should().NotBeNull();
        savedPosition!.IsClosed.Should().BeTrue();
        savedPosition.ClosedAt.Should().NotBeNull();

        // Verify it's not returned as active
        var activePositions = await _repository.GetAllActiveAsync();
        activePositions.Should().NotContain(p => p.Id == position.Id);
    }

    [Fact]
    public async Task MultiplePositions_ConcurrentOperations_MaintainsConsistency()
    {
        // Arrange
        var pairs = new[]
        {
            TradingPair.Create("BTCUSDT", "USDT"),
            TradingPair.Create("ETHUSDT", "USDT"),
            TradingPair.Create("ADAUSDT", "USDT"),
            TradingPair.Create("DOTUSDT", "USDT"),
            TradingPair.Create("SOLUSDT", "USDT")
        };

        // Act - Create positions concurrently
        var createTasks = pairs.Select(pair =>
        {
            var position = Position.Open(
                pair,
                OrderId.From($"order_{pair.Symbol}"),
                Price.Create(100m),
                Quantity.Create(10m),
                Money.Create(1m, "USDT"));
            return _repository.SaveAsync(position);
        });

        await Task.WhenAll(createTasks);

        // Verify all positions saved
        var activePositions = await _repository.GetAllActiveAsync();
        activePositions.Should().HaveCount(5);

        // Act - Read positions concurrently
        var readTasks = pairs.Select(pair => _repository.GetByPairAsync(pair));
        var positions = await Task.WhenAll(readTasks);

        // Verify all reads successful
        positions.Should().AllSatisfy(p => p.Should().NotBeNull());

        // Act - Close some positions concurrently
        var closeTasks = positions.Take(3).Select(p =>
        {
            p!.Close(OrderId.From($"sell_{p.Pair.Symbol}"), Price.Create(110m), Money.Create(1.1m, "USDT"));
            return _repository.SaveAsync(p);
        });

        await Task.WhenAll(closeTasks);

        // Verify final state
        activePositions = await _repository.GetAllActiveAsync();
        activePositions.Should().HaveCount(2);

        var count = await _repository.GetActiveCountAsync();
        count.Should().Be(2);
    }

    [Fact]
    public async Task PositionWithMultipleDCALevels_CalculatesAveragePriceCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Initial buy: 1 BTC at $50,000
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        // DCA 1: 1 BTC at $45,000
        position.AddDCAEntry(
            OrderId.From("order2"),
            Price.Create(45000m),
            Quantity.Create(1m),
            Money.Create(45m, "USDT"));

        // DCA 2: 2 BTC at $40,000
        position.AddDCAEntry(
            OrderId.From("order3"),
            Price.Create(40000m),
            Quantity.Create(2m),
            Money.Create(80m, "USDT"));

        await _repository.SaveAsync(position);

        // Act
        var savedPosition = await _repository.GetByIdAsync(position.Id);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.TotalQuantity.Value.Should().Be(4m);
        savedPosition.TotalCost.Amount.Should().Be(175000m); // 50000 + 45000 + 80000
        savedPosition.AveragePrice.Value.Should().Be(43750m); // 175000 / 4
        savedPosition.DCALevel.Should().Be(3);
    }

    #endregion

    #region Trailing Integration Tests

    [Fact]
    public async Task TrailingWithRepository_SellTrailingTriggersAndClosesPosition()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        var config = new TrailingConfig
        {
            TrailingPercentage = 1m,
            StopMargin = 0.5m,
            StopAction = TrailingStopAction.Execute
        };

        // Act - Start trailing at 5% margin
        var currentPrice = Price.Create(52500m); // 5% profit
        var currentMargin = 5m;
        _trailingManager.InitiateSellTrailing(position.Id, pair, currentPrice, currentMargin, 2m, config);

        // Verify trailing is active
        _trailingManager.HasSellTrailing(pair).Should().BeTrue();

        // Price goes up - update trailing
        var higherPrice = Price.Create(54000m); // ~8% profit
        var higherMargin = 8m;
        var result1 = _trailingManager.UpdateSellTrailing(pair, higherPrice, higherMargin);
        result1.Result.Should().Be(TrailingCheckResult.Continue);

        // Price drops - should trigger
        var lowerPrice = Price.Create(53000m); // ~6% profit, dropped from 8%
        var lowerMargin = 6m;
        var result2 = _trailingManager.UpdateSellTrailing(pair, lowerPrice, lowerMargin);
        result2.Result.Should().Be(TrailingCheckResult.Triggered);

        // Close the position
        position.Close(OrderId.From("sell1"), lowerPrice, Money.Create(53m, "USDT"));
        await _repository.SaveAsync(position);

        // Verify position is closed
        var savedPosition = await _repository.GetByIdAsync(position.Id);
        savedPosition!.IsClosed.Should().BeTrue();
        _trailingManager.HasSellTrailing(pair).Should().BeFalse();
    }

    [Fact]
    public async Task TrailingWithRepository_BuyTrailingTriggersAndCreatesDCA()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        var config = new TrailingConfig
        {
            TrailingPercentage = 1m,
            StopMargin = 5m,
            StopAction = TrailingStopAction.Execute
        };

        var cost = Money.Create(50000m, "USDT");

        // Act - Start trailing at current price
        var currentPrice = Price.Create(47500m); // Price dropped 5%
        _trailingManager.InitiateBuyTrailing(pair, currentPrice, cost, config, position.Id);

        // Verify trailing is active
        _trailingManager.HasBuyTrailing(pair).Should().BeTrue();

        // Price goes down more - update trailing
        var lowerPrice = Price.Create(45000m); // Price dropped 10%
        var result1 = _trailingManager.UpdateBuyTrailing(pair, lowerPrice);
        result1.Result.Should().Be(TrailingCheckResult.Continue);

        // Price bounces up - should trigger buy
        var bouncePrice = Price.Create(46500m); // Bounced up from bottom
        var result2 = _trailingManager.UpdateBuyTrailing(pair, bouncePrice);
        result2.Result.Should().Be(TrailingCheckResult.Triggered);

        // Add DCA entry
        position.AddDCAEntry(
            OrderId.From("order2"),
            bouncePrice,
            Quantity.Create(1.075m), // ~50000 / 46500
            Money.Create(50m, "USDT"));

        await _repository.SaveAsync(position);

        // Verify DCA was added
        var savedPosition = await _repository.GetByIdAsync(position.Id);
        savedPosition!.DCALevel.Should().Be(1);
        savedPosition.Entries.Should().HaveCount(2);
        _trailingManager.HasBuyTrailing(pair).Should().BeFalse();
    }

    #endregion

    #region Repository Persistence Tests

    [Fact]
    public async Task RepositoryPersistence_DataSurvivesRepositoryRecreation()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"),
            "TestSignalRule");

        position.AddDCAEntry(
            OrderId.From("order2"),
            Price.Create(45000m),
            Quantity.Create(0.5m),
            Money.Create(22.5m, "USDT"));

        var positionId = position.Id;
        await _repository.SaveAsync(position);
        _repository.Dispose();

        // Act - Create new repository instance
        using var newRepository = new JsonPositionRepository(_repositoryPath);
        var loadedPosition = await newRepository.GetByIdAsync(positionId);

        // Assert
        loadedPosition.Should().NotBeNull();
        loadedPosition!.Id.Should().Be(positionId);
        loadedPosition.Pair.Symbol.Should().Be("BTCUSDT");
        loadedPosition.SignalRule.Should().Be("TestSignalRule");
        loadedPosition.DCALevel.Should().Be(1);
        loadedPosition.Entries.Should().HaveCount(2);
        loadedPosition.TotalQuantity.Value.Should().Be(1.5m);
    }

    [Fact]
    public async Task RepositoryPersistence_ClosedPositionsQueryByDateRange()
    {
        // Arrange
        var positions = new List<Position>();
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < 5; i++)
        {
            var pair = TradingPair.Create($"COIN{i}USDT", "USDT");
            var position = Position.Open(
                pair,
                OrderId.From($"order_{i}"),
                Price.Create(100m),
                Quantity.Create(10m),
                Money.Create(1m, "USDT"));

            position.Close(
                OrderId.From($"sell_{i}"),
                Price.Create(110m),
                Money.Create(1.1m, "USDT"));

            await _repository.SaveAsync(position);
            positions.Add(position);
        }

        // Act
        var from = baseTime.AddMinutes(-1);
        var to = baseTime.AddMinutes(1);
        var closedPositions = await _repository.GetClosedPositionsAsync(from, to);

        // Assert
        closedPositions.Should().HaveCount(5);
        closedPositions.Should().AllSatisfy(p => p.IsClosed.Should().BeTrue());
    }

    #endregion

    #region Portfolio Association Tests

    [Fact]
    public async Task PortfolioAssociation_PositionsGroupedByPortfolio()
    {
        // Arrange
        var portfolio1 = PortfolioId.Create();
        var portfolio2 = PortfolioId.Create();

        // Create positions for portfolio 1
        for (int i = 0; i < 3; i++)
        {
            var pair = TradingPair.Create($"P1COIN{i}USDT", "USDT");
            var position = Position.Open(
                pair,
                OrderId.From($"p1_order_{i}"),
                Price.Create(100m),
                Quantity.Create(10m),
                Money.Create(1m, "USDT"));

            await _repository.SaveAsync(position);
            await _repository.AssociateWithPortfolioAsync(position.Id, portfolio1);
        }

        // Create positions for portfolio 2
        for (int i = 0; i < 2; i++)
        {
            var pair = TradingPair.Create($"P2COIN{i}USDT", "USDT");
            var position = Position.Open(
                pair,
                OrderId.From($"p2_order_{i}"),
                Price.Create(200m),
                Quantity.Create(5m),
                Money.Create(1m, "USDT"));

            await _repository.SaveAsync(position);
            await _repository.AssociateWithPortfolioAsync(position.Id, portfolio2);
        }

        // Act
        var portfolio1Positions = await _repository.GetByPortfolioAsync(portfolio1);
        var portfolio2Positions = await _repository.GetByPortfolioAsync(portfolio2);

        // Assert
        portfolio1Positions.Should().HaveCount(3);
        portfolio2Positions.Should().HaveCount(2);

        portfolio1Positions.Should().AllSatisfy(p =>
            p.Pair.Symbol.Should().StartWith("P1"));
        portfolio2Positions.Should().AllSatisfy(p =>
            p.Pair.Symbol.Should().StartWith("P2"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EdgeCase_PositionWithZeroFees_HandledCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var zeroFees = Money.Create(0m, "USDT");

        // Act
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            Quantity.Create(1m),
            zeroFees);

        await _repository.SaveAsync(position);
        var savedPosition = await _repository.GetByIdAsync(position.Id);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.TotalFees.Amount.Should().Be(0m);
        savedPosition.Entries[0].Fees.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task EdgeCase_VerySmallQuantities_PreservesPrecision()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var smallQuantity = Quantity.Create(0.00000001m); // 1 satoshi equivalent

        // Act
        var position = Position.Open(
            pair,
            OrderId.From("order1"),
            Price.Create(50000m),
            smallQuantity,
            Money.Create(0.0005m, "USDT"));

        await _repository.SaveAsync(position);
        var savedPosition = await _repository.GetByIdAsync(position.Id);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.TotalQuantity.Value.Should().Be(0.00000001m);
    }

    [Fact]
    public async Task EdgeCase_LargeNumberOfDCALevels_HandledCorrectly()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");
        var position = Position.Open(
            pair,
            OrderId.From("order0"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(50m, "USDT"));

        // Add 20 DCA levels
        for (int i = 1; i <= 20; i++)
        {
            var dcaPrice = Price.Create(50000m - (i * 1000m));
            position.AddDCAEntry(
                OrderId.From($"order{i}"),
                dcaPrice,
                Quantity.Create(0.5m),
                Money.Create(25m, "USDT"));
        }

        // Act
        await _repository.SaveAsync(position);
        var savedPosition = await _repository.GetByIdAsync(position.Id);

        // Assert
        savedPosition.Should().NotBeNull();
        savedPosition!.DCALevel.Should().Be(20);
        savedPosition.Entries.Should().HaveCount(21);
        savedPosition.TotalQuantity.Value.Should().Be(11m); // 1 + (20 * 0.5)
    }

    #endregion
}
