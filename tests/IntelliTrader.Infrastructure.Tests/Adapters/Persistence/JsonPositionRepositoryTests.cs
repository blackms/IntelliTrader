using FluentAssertions;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public class JsonPositionRepositoryTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly JsonPositionRepository _sut;

    public JsonPositionRepositoryTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_positions_{Guid.NewGuid()}.json");
        _sut = new JsonPositionRepository(_testFilePath);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullFilePath_ThrowsArgumentException()
    {
        // Act
        var act = () => new JsonPositionRepository(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public void Constructor_WithEmptyFilePath_ThrowsArgumentException()
    {
        // Act
        var act = () => new JsonPositionRepository("");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public void Constructor_WithValidFilePath_CreatesInstance()
    {
        // Act
        using var repo = new JsonPositionRepository(_testFilePath);

        // Assert
        repo.Should().NotBeNull();
    }

    #endregion

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_NewPosition_PersistsToFile()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");

        // Act
        await _sut.SaveAsync(position);

        // Assert
        File.Exists(_testFilePath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(_testFilePath);
        content.Should().Contain("BTCUSDT");
    }

    [Fact]
    public async Task SaveAsync_UpdateExistingPosition_UpdatesInFile()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Add DCA entry
        position.AddDCAEntry(
            OrderId.From("order2"),
            Price.Create(49000m),
            Quantity.Create(0.5m),
            Money.Create(0.5m, "USDT"));

        // Act
        await _sut.SaveAsync(position);

        // Assert
        var retrieved = await _sut.GetByIdAsync(position.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_WithNullPosition_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.SaveAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var nestedPath = Path.Combine(Path.GetTempPath(), $"nested_{Guid.NewGuid()}", "data", "positions.json");
        using var repo = new JsonPositionRepository(nestedPath);
        var position = CreateTestPosition("BTCUSDT");

        try
        {
            // Act
            await repo.SaveAsync(position);

            // Assert
            File.Exists(nestedPath).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            var dir = Path.GetDirectoryName(nestedPath);
            if (dir != null && Directory.Exists(dir))
            {
                Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nestedPath)!)!, true);
            }
        }
    }

    #endregion

    #region SaveManyAsync Tests

    [Fact]
    public async Task SaveManyAsync_MultiplePositions_PersistsAll()
    {
        // Arrange
        var positions = new[]
        {
            CreateTestPosition("BTCUSDT"),
            CreateTestPosition("ETHUSDT"),
            CreateTestPosition("ADAUSDT")
        };

        // Act
        await _sut.SaveManyAsync(positions);

        // Assert
        var activePositions = await _sut.GetAllActiveAsync();
        activePositions.Should().HaveCount(3);
    }

    [Fact]
    public async Task SaveManyAsync_WithEmptyCollection_DoesNotThrow()
    {
        // Act
        var act = () => _sut.SaveManyAsync(Array.Empty<Position>());

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_ExistingPosition_ReturnsPosition()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Act
        var retrieved = await _sut.GetByIdAsync(position.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(position.Id);
        retrieved.Pair.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingPosition_ReturnsNull()
    {
        // Act
        var retrieved = await _sut.GetByIdAsync(PositionId.Create());

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithNullId_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.GetByIdAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetByIdAsync_PreservesAllPositionData()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT", signalRule: "TestRule");
        await _sut.SaveAsync(position);

        // Act
        var retrieved = await _sut.GetByIdAsync(position.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Currency.Should().Be("USDT");
        retrieved.SignalRule.Should().Be("TestRule");
        retrieved.DCALevel.Should().Be(0);
        retrieved.Entries.Should().HaveCount(1);
        retrieved.Entries[0].Price.Value.Should().Be(50000m);
        retrieved.Entries[0].Quantity.Value.Should().Be(1m);
    }

    #endregion

    #region GetByPairAsync Tests

    [Fact]
    public async Task GetByPairAsync_ExistingPair_ReturnsActivePosition()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Act
        var retrieved = await _sut.GetByPairAsync(TradingPair.Create("BTCUSDT", "USDT"));

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Pair.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task GetByPairAsync_ClosedPosition_ReturnsNull()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        position.Close(
            OrderId.From("sell_order"),
            Price.Create(55000m),
            Money.Create(1m, "USDT"));
        await _sut.SaveAsync(position);

        // Act
        var retrieved = await _sut.GetByPairAsync(TradingPair.Create("BTCUSDT", "USDT"));

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetByPairAsync_NonExistingPair_ReturnsNull()
    {
        // Act
        var retrieved = await _sut.GetByPairAsync(TradingPair.Create("XYZUSDT", "USDT"));

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetByPairAsync_IsCaseInsensitive()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Act
        var retrieved = await _sut.GetByPairAsync(TradingPair.Create("btcusdt", "usdt"));

        // Assert
        retrieved.Should().NotBeNull();
    }

    #endregion

    #region GetAllActiveAsync Tests

    [Fact]
    public async Task GetAllActiveAsync_ReturnsOnlyActivePositions()
    {
        // Arrange
        var activePosition = CreateTestPosition("BTCUSDT");
        var closedPosition = CreateTestPosition("ETHUSDT");
        closedPosition.Close(
            OrderId.From("sell"),
            Price.Create(3000m),
            Money.Create(0.1m, "USDT"));

        await _sut.SaveAsync(activePosition);
        await _sut.SaveAsync(closedPosition);

        // Act
        var activePositions = await _sut.GetAllActiveAsync();

        // Assert
        activePositions.Should().HaveCount(1);
        activePositions.Single().Pair.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task GetAllActiveAsync_WhenNoPositions_ReturnsEmptyList()
    {
        // Act
        var activePositions = await _sut.GetAllActiveAsync();

        // Assert
        activePositions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllActiveAsync_ReturnsMultipleActivePositions()
    {
        // Arrange
        await _sut.SaveAsync(CreateTestPosition("BTCUSDT"));
        await _sut.SaveAsync(CreateTestPosition("ETHUSDT"));
        await _sut.SaveAsync(CreateTestPosition("ADAUSDT"));

        // Act
        var activePositions = await _sut.GetAllActiveAsync();

        // Assert
        activePositions.Should().HaveCount(3);
    }

    #endregion

    #region GetByPortfolioAsync Tests

    [Fact]
    public async Task GetByPortfolioAsync_ReturnsPositionsForPortfolio()
    {
        // Arrange
        var portfolioId = PortfolioId.Create();
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);
        await _sut.AssociateWithPortfolioAsync(position.Id, portfolioId);

        // Act
        var positions = await _sut.GetByPortfolioAsync(portfolioId);

        // Assert
        positions.Should().HaveCount(1);
        positions.Single().Pair.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task GetByPortfolioAsync_ReturnsEmptyForUnknownPortfolio()
    {
        // Arrange
        await _sut.SaveAsync(CreateTestPosition("BTCUSDT"));

        // Act
        var positions = await _sut.GetByPortfolioAsync(PortfolioId.Create());

        // Assert
        positions.Should().BeEmpty();
    }

    #endregion

    #region GetClosedPositionsAsync Tests

    [Fact]
    public async Task GetClosedPositionsAsync_ReturnsClosedPositionsInDateRange()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        position.Close(
            OrderId.From("sell"),
            Price.Create(55000m),
            Money.Create(1m, "USDT"));
        await _sut.SaveAsync(position);

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var closedPositions = await _sut.GetClosedPositionsAsync(from, to);

        // Assert
        closedPositions.Should().HaveCount(1);
        closedPositions.Single().IsClosed.Should().BeTrue();
    }

    [Fact]
    public async Task GetClosedPositionsAsync_ExcludesPositionsOutsideDateRange()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        position.Close(
            OrderId.From("sell"),
            Price.Create(55000m),
            Money.Create(1m, "USDT"));
        await _sut.SaveAsync(position);

        var from = DateTimeOffset.UtcNow.AddDays(1);
        var to = DateTimeOffset.UtcNow.AddDays(2);

        // Act
        var closedPositions = await _sut.GetClosedPositionsAsync(from, to);

        // Assert
        closedPositions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClosedPositionsAsync_ExcludesActivePositions()
    {
        // Arrange
        await _sut.SaveAsync(CreateTestPosition("BTCUSDT"));

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var closedPositions = await _sut.GetClosedPositionsAsync(from, to);

        // Assert
        closedPositions.Should().BeEmpty();
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingPosition_RemovesFromStorage()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Act
        await _sut.DeleteAsync(position.Id);

        // Assert
        var retrieved = await _sut.GetByIdAsync(position.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingPosition_DoesNotThrow()
    {
        // Act
        var act = () => _sut.DeleteAsync(PositionId.Create());

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region ExistsForPairAsync Tests

    [Fact]
    public async Task ExistsForPairAsync_ActivePositionExists_ReturnsTrue()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Act
        var exists = await _sut.ExistsForPairAsync(TradingPair.Create("BTCUSDT", "USDT"));

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsForPairAsync_OnlyClosedPositionExists_ReturnsFalse()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        position.Close(
            OrderId.From("sell"),
            Price.Create(55000m),
            Money.Create(1m, "USDT"));
        await _sut.SaveAsync(position);

        // Act
        var exists = await _sut.ExistsForPairAsync(TradingPair.Create("BTCUSDT", "USDT"));

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsForPairAsync_NoPositionExists_ReturnsFalse()
    {
        // Act
        var exists = await _sut.ExistsForPairAsync(TradingPair.Create("BTCUSDT", "USDT"));

        // Assert
        exists.Should().BeFalse();
    }

    #endregion

    #region GetActiveCountAsync Tests

    [Fact]
    public async Task GetActiveCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _sut.SaveAsync(CreateTestPosition("BTCUSDT"));
        await _sut.SaveAsync(CreateTestPosition("ETHUSDT"));

        var closedPosition = CreateTestPosition("ADAUSDT");
        closedPosition.Close(
            OrderId.From("sell"),
            Price.Create(1m),
            Money.Create(0.01m, "USDT"));
        await _sut.SaveAsync(closedPosition);

        // Act
        var count = await _sut.GetActiveCountAsync();

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetActiveCountAsync_WhenNoPositions_ReturnsZero()
    {
        // Act
        var count = await _sut.GetActiveCountAsync();

        // Assert
        count.Should().Be(0);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public async Task DataPersistence_SurvivesRepositoryRecreation()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);
        _sut.Dispose();

        // Act
        using var newRepo = new JsonPositionRepository(_testFilePath);
        var retrieved = await newRepo.GetByIdAsync(position.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Pair.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task DataPersistence_PreservesEntriesWithDCA()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        position.AddDCAEntry(
            OrderId.From("order2"),
            Price.Create(48000m),
            Quantity.Create(0.5m),
            Money.Create(0.5m, "USDT"));

        await _sut.SaveAsync(position);
        _sut.Dispose();

        // Act
        using var newRepo = new JsonPositionRepository(_testFilePath);
        var retrieved = await newRepo.GetByIdAsync(position.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Entries.Should().HaveCount(2);
        retrieved.DCALevel.Should().Be(1);
    }

    [Fact]
    public async Task DataPersistence_PreservesClosedState()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        position.Close(
            OrderId.From("sell"),
            Price.Create(55000m),
            Money.Create(1m, "USDT"));

        await _sut.SaveAsync(position);
        _sut.Dispose();

        // Act
        using var newRepo = new JsonPositionRepository(_testFilePath);
        var retrieved = await newRepo.GetByIdAsync(position.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.IsClosed.Should().BeTrue();
        retrieved.ClosedAt.Should().NotBeNull();
    }

    #endregion

    #region ReloadAsync Tests

    [Fact]
    public async Task ReloadAsync_RefreshesCacheFromFile()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        // Simulate external file modification by using another repository
        using var otherRepo = new JsonPositionRepository(_testFilePath);
        await otherRepo.ReloadAsync();
        var newPosition = CreateTestPosition("ETHUSDT");
        await otherRepo.SaveAsync(newPosition);

        // Act
        await _sut.ReloadAsync();
        var count = await _sut.GetActiveCountAsync();

        // Assert
        count.Should().Be(2);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentAccess_MultipleWrites_AllSucceed()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _sut.SaveAsync(CreateTestPosition($"PAIR{i}USDT", $"PAIR{i}")))
            .ToArray();

        // Act
        await Task.WhenAll(tasks);

        // Assert
        var count = await _sut.GetActiveCountAsync();
        count.Should().Be(10);
    }

    [Fact]
    public async Task ConcurrentAccess_ReadsDuringWrites_DoesNotThrow()
    {
        // Arrange
        var position = CreateTestPosition("BTCUSDT");
        await _sut.SaveAsync(position);

        var writeTasks = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var pos = CreateTestPosition($"PAIR{i}USDT", $"PAIR{i}");
                return _sut.SaveAsync(pos);
            });

        var readTasks = Enumerable.Range(0, 10)
            .Select(_ => _sut.GetAllActiveAsync());

        // Act
        var allTasks = writeTasks.Cast<Task>().Concat(readTasks);
        var act = () => Task.WhenAll(allTasks);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EmptyFile_DoesNotThrow()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "");

        // Act
        var positions = await _sut.GetAllActiveAsync();

        // Assert
        positions.Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedJson_ThrowsOnLoad()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "{ invalid json }");

        // Act & Assert
        var act = () => _sut.GetAllActiveAsync();

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task NonExistentFile_CreatedOnFirstSave()
    {
        // Arrange
        File.Exists(_testFilePath).Should().BeFalse();

        // Act
        await _sut.SaveAsync(CreateTestPosition("BTCUSDT"));

        // Assert
        File.Exists(_testFilePath).Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static Position CreateTestPosition(string symbol, string? baseCurrency = null, string? signalRule = null)
    {
        baseCurrency ??= symbol.Replace("USDT", "");
        var pair = TradingPair.Create(symbol, "USDT");

        return Position.Open(
            pair,
            OrderId.From($"order_{Guid.NewGuid()}"),
            Price.Create(50000m),
            Quantity.Create(1m),
            Money.Create(1m, "USDT"),
            signalRule);
    }

    #endregion
}
