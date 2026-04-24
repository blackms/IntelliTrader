using System.Text.Json;
using FluentAssertions;
using IntelliTrader.Domain.Trading.Aggregates;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Transactions;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class JsonPortfolioRepositoryTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly JsonPortfolioRepository _sut;

    public JsonPortfolioRepositoryTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_portfolios_{Guid.NewGuid():N}.json");
        _sut = new JsonPortfolioRepository(_testFilePath);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }

    [Fact]
    public void Constructor_WithNullFilePath_ThrowsArgumentException()
    {
        var act = () => new JsonPortfolioRepository(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public void Constructor_WithEmptyFilePath_ThrowsArgumentException()
    {
        var act = () => new JsonPortfolioRepository(string.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public async Task SaveAsync_FirstPortfolio_PersistsAsDefaultPortfolio()
    {
        var portfolio = CreateTestPortfolio("Primary");

        await _sut.SaveAsync(portfolio);

        var persisted = await _sut.GetDefaultAsync();
        persisted.Should().NotBeNull();
        persisted!.Id.Should().Be(portfolio.Id);
        persisted.Name.Should().Be("Primary");

        var json = await File.ReadAllTextAsync(_testFilePath);
        using var document = JsonDocument.Parse(json);
        document.RootElement[0].GetProperty("isDefault").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfItDoesNotExist()
    {
        var nestedDirectory = Path.Combine(Path.GetTempPath(), $"nested_portfolios_{Guid.NewGuid():N}", "data");
        var nestedFilePath = Path.Combine(nestedDirectory, "portfolios.json");
        using var repository = new JsonPortfolioRepository(nestedFilePath);

        await repository.SaveAsync(CreateTestPortfolio("Nested"));

        File.Exists(nestedFilePath).Should().BeTrue();

        Directory.Delete(Path.GetDirectoryName(nestedDirectory)!, recursive: true);
    }

    [Fact]
    public async Task GetDefaultAsync_WhenPersistedPortfoliosHaveNoDefault_PromotesOldestPortfolio()
    {
        var oldestId = Guid.NewGuid();
        var newestId = Guid.NewGuid();
        var json = $$"""
        [
          {
            "id": "{{oldestId}}",
            "name": "Oldest",
            "market": "USDT",
            "balance": { "currency": "USDT", "total": 1000, "available": 1000, "reserved": 0 },
            "maxPositions": 5,
            "minPositionCost": 10,
            "createdAt": "2024-01-01T00:00:00+00:00",
            "isDefault": false,
            "activePositions": [],
            "positionCosts": []
          },
          {
            "id": "{{newestId}}",
            "name": "Newest",
            "market": "USDT",
            "balance": { "currency": "USDT", "total": 1000, "available": 1000, "reserved": 0 },
            "maxPositions": 5,
            "minPositionCost": 10,
            "createdAt": "2024-01-02T00:00:00+00:00",
            "isDefault": false,
            "activePositions": [],
            "positionCosts": []
          }
        ]
        """;
        await File.WriteAllTextAsync(_testFilePath, json);

        var defaultPortfolio = await _sut.GetDefaultAsync();
        var allPortfolios = await _sut.GetAllAsync();

        defaultPortfolio.Should().NotBeNull();
        defaultPortfolio!.Id.Value.Should().Be(oldestId);
        allPortfolios.First().Id.Should().Be(defaultPortfolio.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenDeletingDefaultPortfolio_PromotesRemainingPortfolio()
    {
        var defaultPortfolio = CreateTestPortfolio("Default");
        var secondaryPortfolio = CreateTestPortfolio("Secondary");

        await _sut.SaveAsync(defaultPortfolio);
        await _sut.SaveAsync(secondaryPortfolio);

        await _sut.DeleteAsync(defaultPortfolio.Id);

        var promoted = await _sut.GetDefaultAsync();
        promoted.Should().NotBeNull();
        promoted!.Id.Should().Be(secondaryPortfolio.Id);
        promoted.Name.Should().Be("Secondary");

        var allPortfolios = await _sut.GetAllAsync();
        allPortfolios.Should().ContainSingle()
            .Which.Id.Should().Be(secondaryPortfolio.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenPortfolioDoesNotExist_LeavesPersistedPortfoliosUnchanged()
    {
        var existing = CreateTestPortfolio("Existing");
        await _sut.SaveAsync(existing);

        await _sut.DeleteAsync(PortfolioId.Create());

        var allPortfolios = await _sut.GetAllAsync();
        allPortfolios.Should().ContainSingle()
            .Which.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenDeletingOnlyDefaultPortfolio_RemovesAllPortfolios()
    {
        var onlyPortfolio = CreateTestPortfolio("Only");
        await _sut.SaveAsync(onlyPortfolio);

        await _sut.DeleteAsync(onlyPortfolio.Id);

        var defaultPortfolio = await _sut.GetDefaultAsync();
        var allPortfolios = await _sut.GetAllAsync();

        defaultPortfolio.Should().BeNull();
        allPortfolios.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByNameAsync_WhenNameMatchesIgnoringCase_ReturnsPortfolio()
    {
        var portfolio = CreateTestPortfolio("Primary");
        await _sut.SaveAsync(portfolio);

        var result = await _sut.GetByNameAsync("primary");

        result.Should().NotBeNull();
        result!.Id.Should().Be(portfolio.Id);
        result.Name.Should().Be("Primary");
    }

    [Fact]
    public async Task GetByNameAsync_WhenNameDoesNotMatch_ReturnsNull()
    {
        await _sut.SaveAsync(CreateTestPortfolio("Primary"));

        var result = await _sut.GetByNameAsync("Missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByNameAsync_WithEmptyName_ThrowsArgumentException()
    {
        var act = () => _sut.GetByNameAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public async Task ExistsWithNameAsync_WhenNameMatchesIgnoringCase_ReturnsTrue()
    {
        await _sut.SaveAsync(CreateTestPortfolio("Primary"));

        var exists = await _sut.ExistsWithNameAsync("PRIMARY");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsWithNameAsync_WhenNameDoesNotMatch_ReturnsFalse()
    {
        await _sut.SaveAsync(CreateTestPortfolio("Primary"));

        var exists = await _sut.ExistsWithNameAsync("Missing");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsWithNameAsync_WithEmptyName_ThrowsArgumentException()
    {
        var act = () => _sut.ExistsWithNameAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public async Task ReloadAsync_RefreshesCacheFromExternalChanges()
    {
        var original = CreateTestPortfolio("Original");
        await _sut.SaveAsync(original);

        using var otherRepository = new JsonPortfolioRepository(_testFilePath);
        await otherRepository.SaveAsync(CreateTestPortfolio("External"));

        await _sut.ReloadAsync();

        var allPortfolios = await _sut.GetAllAsync();
        allPortfolios.Should().HaveCount(2);
        allPortfolios.Select(portfolio => portfolio.Name).Should().Contain(["Original", "External"]);
    }

    [Fact]
    public async Task SaveAsync_WithinTransaction_ExposesStagedPortfolioWithoutPersistingUntilCommit()
    {
        var transactionCoordinator = new JsonTransactionCoordinator();
        var unitOfWork = new JsonTransactionalUnitOfWork(transactionCoordinator);
        using var repository = new JsonPortfolioRepository(_testFilePath, transactionCoordinator);
        var staged = CreateTestPortfolio("Staged");

        await unitOfWork.BeginTransactionAsync();
        await repository.SaveAsync(staged);

        File.Exists(_testFilePath).Should().BeFalse();

        var readDuringTransaction = await repository.GetByIdAsync(staged.Id);
        readDuringTransaction.Should().NotBeNull();
        readDuringTransaction!.Name.Should().Be("Staged");

        await unitOfWork.RollbackAsync();

        var readAfterRollback = await repository.GetByIdAsync(staged.Id);
        readAfterRollback.Should().BeNull();
        File.Exists(_testFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task EmptyFile_DoesNotThrowAndReturnsNoPortfolios()
    {
        await File.WriteAllTextAsync(_testFilePath, string.Empty);

        var portfolios = await _sut.GetAllAsync();

        portfolios.Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedJson_ThrowsOnLoad()
    {
        await File.WriteAllTextAsync(_testFilePath, "{ invalid json }");

        var act = () => _sut.GetAllAsync();

        await act.Should().ThrowAsync<JsonException>();
    }

    private static Portfolio CreateTestPortfolio(string name)
    {
        return Portfolio.Create(name, "USDT", 1000m, 5, 10m);
    }
}
