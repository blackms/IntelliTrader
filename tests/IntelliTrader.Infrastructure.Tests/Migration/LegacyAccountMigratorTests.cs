using System.Text.Json;
using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Migration;
using IntelliTrader.Infrastructure.Migration.LegacyModels;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Migration;

public class LegacyAccountMigratorTests : IDisposable
{
    private readonly string _testDataDir;
    private readonly Mock<IPositionRepository> _mockRepository;
    private readonly LegacyAccountMigrator _migrator;

    public LegacyAccountMigratorTests()
    {
        _testDataDir = Path.Combine(Path.GetTempPath(), $"LegacyMigratorTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDir);

        _mockRepository = new Mock<IPositionRepository>();
        _mockRepository
            .Setup(r => r.SaveManyAsync(It.IsAny<IEnumerable<Position>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _migrator = new LegacyAccountMigrator(_mockRepository.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataDir))
        {
            Directory.Delete(_testDataDir, true);
        }
    }

    [Fact]
    public async Task MigrateAsync_WithValidLegacyData_MigratesSuccessfully()
    {
        // Arrange
        var legacyData = new LegacyTradingAccountData
        {
            Balance = 1000.50m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["BTCUSDT"] = new LegacyTradingPair
                {
                    Pair = "BTCUSDT",
                    OrderIds = new List<string> { "order1", "order2" },
                    OrderDates = new List<DateTimeOffset>
                    {
                        new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero)
                    },
                    TotalAmount = 0.01m,
                    AveragePricePaid = 42000m,
                    FeesMarketCurrency = 0.84m,
                    Metadata = new LegacyOrderMetadata
                    {
                        SignalRule = "TradingView_5m"
                    }
                }
            }
        };

        var filePath = Path.Combine(_testDataDir, "test-account.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();

        // Act
        var result = await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalLegacyPositions.Should().Be(1);
        result.MigratedPositions.Should().Be(1);
        result.LegacyBalance.Should().Be(1000.50m);
        result.Errors.Should().BeEmpty();

        _mockRepository.Verify(r => r.SaveManyAsync(
            It.Is<IEnumerable<Position>>(positions =>
                positions.Count() == 1 &&
                positions.First().Pair.Symbol == "BTCUSDT" &&
                positions.First().Entries.Count == 2 &&
                positions.First().SignalRule == "TradingView_5m"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_WithMultiplePositions_MigratesAll()
    {
        // Arrange
        var legacyData = new LegacyTradingAccountData
        {
            Balance = 500m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["BTCUSDT"] = CreateLegacyPair("BTCUSDT", 0.01m, 42000m),
                ["ETHUSDT"] = CreateLegacyPair("ETHUSDT", 0.5m, 2500m),
                ["ADAUSDT"] = CreateLegacyPair("ADAUSDT", 1000m, 0.45m)
            }
        };

        var filePath = Path.Combine(_testDataDir, "multi-position.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();

        // Act
        var result = await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalLegacyPositions.Should().Be(3);
        result.MigratedPositions.Should().Be(3);

        _mockRepository.Verify(r => r.SaveManyAsync(
            It.Is<IEnumerable<Position>>(positions => positions.Count() == 3),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_WithFileNotFound_ReturnsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDataDir, "does-not-exist.json");
        var portfolioId = PortfolioId.Create();

        // Act
        var result = await _migrator.MigrateAsync(nonExistentPath, "USDT", portfolioId);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("File not found"));

        _mockRepository.Verify(r => r.SaveManyAsync(
            It.IsAny<IEnumerable<Position>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MigrateAsync_WithEmptyPosition_SkipsIt()
    {
        // Arrange
        var legacyData = new LegacyTradingAccountData
        {
            Balance = 100m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["BTCUSDT"] = new LegacyTradingPair
                {
                    Pair = "BTCUSDT",
                    OrderIds = new List<string>(),
                    OrderDates = new List<DateTimeOffset>(),
                    TotalAmount = 0m,
                    AveragePricePaid = 0m
                }
            }
        };

        var filePath = Path.Combine(_testDataDir, "empty-position.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();

        // Act
        var result = await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalLegacyPositions.Should().Be(1);
        result.SkippedPositions.Should().Be(1);
        result.MigratedPositions.Should().Be(0);
        result.Warnings.Should().ContainSingle(w => w.Contains("Skipped"));
    }

    [Fact]
    public async Task MigrateAsync_WithDCAPosition_PreservesDCALevel()
    {
        // Arrange
        var legacyData = new LegacyTradingAccountData
        {
            Balance = 200m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["BTCUSDT"] = new LegacyTradingPair
                {
                    Pair = "BTCUSDT",
                    OrderIds = new List<string> { "order1", "order2", "order3" },
                    OrderDates = new List<DateTimeOffset>
                    {
                        new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2024, 1, 10, 10, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2024, 1, 20, 10, 0, 0, TimeSpan.Zero)
                    },
                    TotalAmount = 0.03m,
                    AveragePricePaid = 40000m,
                    FeesMarketCurrency = 1.20m
                }
            }
        };

        var filePath = Path.Combine(_testDataDir, "dca-position.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();

        // Act
        var result = await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        result.Success.Should().BeTrue();

        _mockRepository.Verify(r => r.SaveManyAsync(
            It.Is<IEnumerable<Position>>(positions =>
                positions.First().Entries.Count == 3 &&
                positions.First().DCALevel == 2), // 3 entries = DCA level 2
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MigrateAsync_MarksEntriesAsMigrated()
    {
        // Arrange
        var legacyData = new LegacyTradingAccountData
        {
            Balance = 100m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["BTCUSDT"] = CreateLegacyPair("BTCUSDT", 0.01m, 42000m)
            }
        };

        var filePath = Path.Combine(_testDataDir, "migrated-entries.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();
        Position? capturedPosition = null;

        _mockRepository
            .Setup(r => r.SaveManyAsync(It.IsAny<IEnumerable<Position>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Position>, CancellationToken>((positions, _) =>
            {
                capturedPosition = positions.FirstOrDefault();
            })
            .Returns(Task.CompletedTask);

        // Act
        await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        capturedPosition.Should().NotBeNull();
        capturedPosition!.Entries.Should().AllSatisfy(entry =>
        {
            entry.IsMigrated.Should().BeTrue("all migrated entries should be marked as migrated");
        });
    }

    [Fact]
    public async Task MigrateAsync_PreservesOrderTimestamps()
    {
        // Arrange
        var firstDate = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var secondDate = new DateTimeOffset(2024, 2, 15, 14, 30, 0, TimeSpan.Zero);

        var legacyData = new LegacyTradingAccountData
        {
            Balance = 100m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["ETHUSDT"] = new LegacyTradingPair
                {
                    Pair = "ETHUSDT",
                    OrderIds = new List<string> { "order1", "order2" },
                    OrderDates = new List<DateTimeOffset> { firstDate, secondDate },
                    TotalAmount = 1m,
                    AveragePricePaid = 2500m,
                    FeesMarketCurrency = 5m
                }
            }
        };

        var filePath = Path.Combine(_testDataDir, "timestamps.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();
        Position? capturedPosition = null;

        _mockRepository
            .Setup(r => r.SaveManyAsync(It.IsAny<IEnumerable<Position>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Position>, CancellationToken>((positions, _) =>
            {
                capturedPosition = positions.FirstOrDefault();
            })
            .Returns(Task.CompletedTask);

        // Act
        await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        capturedPosition.Should().NotBeNull();
        capturedPosition!.OpenedAt.Should().Be(firstDate);
        capturedPosition.LastBuyAt.Should().Be(secondDate);
        capturedPosition.Entries[0].Timestamp.Should().Be(firstDate);
        capturedPosition.Entries[1].Timestamp.Should().Be(secondDate);
    }

    [Fact]
    public async Task MigrateAsync_WithZeroAmount_SkipsPosition()
    {
        // Arrange
        var legacyData = new LegacyTradingAccountData
        {
            Balance = 100m,
            TradingPairs = new Dictionary<string, LegacyTradingPair>
            {
                ["BTCUSDT"] = new LegacyTradingPair
                {
                    Pair = "BTCUSDT",
                    OrderIds = new List<string> { "order1" },
                    OrderDates = new List<DateTimeOffset> { DateTimeOffset.UtcNow },
                    TotalAmount = 0m, // Zero amount = closed or empty
                    AveragePricePaid = 42000m
                }
            }
        };

        var filePath = Path.Combine(_testDataDir, "zero-amount.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(legacyData));

        var portfolioId = PortfolioId.Create();

        // Act
        var result = await _migrator.MigrateAsync(filePath, "USDT", portfolioId);

        // Assert
        result.SkippedPositions.Should().Be(1);
        result.MigratedPositions.Should().Be(0);
    }

    private static LegacyTradingPair CreateLegacyPair(string pair, decimal amount, decimal avgPrice)
    {
        return new LegacyTradingPair
        {
            Pair = pair,
            OrderIds = new List<string> { $"{pair}_order1" },
            OrderDates = new List<DateTimeOffset> { DateTimeOffset.UtcNow.AddDays(-5) },
            TotalAmount = amount,
            AveragePricePaid = avgPrice,
            FeesMarketCurrency = amount * avgPrice * 0.001m,
            Metadata = new LegacyOrderMetadata()
        };
    }
}
