using System.Text.Json;
using FluentAssertions;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.Trading.Orders;
using IntelliTrader.Domain.Trading.ValueObjects;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Transactions;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class JsonOrderRepositoryTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly JsonOrderRepository _sut;

    public JsonOrderRepositoryTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_orders_{Guid.NewGuid():N}.json");
        _sut = new JsonOrderRepository(_testFilePath);
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
        var act = () => new JsonOrderRepository(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public void Constructor_WithEmptyFilePath_ThrowsArgumentException()
    {
        var act = () => new JsonOrderRepository(string.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public async Task SaveAsync_NewOrder_PersistsToFile()
    {
        var order = CreateSubmittedOrder("order-001", "BTCUSDT", submittedAt: new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));

        await _sut.SaveAsync(order);

        File.Exists(_testFilePath).Should().BeTrue();
        var persisted = await _sut.GetByIdAsync(order.Id);
        persisted.Should().NotBeNull();
        persisted!.Pair.Symbol.Should().Be("BTCUSDT");
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfItDoesNotExist()
    {
        var nestedDirectory = Path.Combine(Path.GetTempPath(), $"nested_orders_{Guid.NewGuid():N}", "data");
        var nestedFilePath = Path.Combine(nestedDirectory, "orders.json");
        using var repository = new JsonOrderRepository(nestedFilePath);

        await repository.SaveAsync(CreateSubmittedOrder("order-nested", "ETHUSDT"));

        File.Exists(nestedFilePath).Should().BeTrue();

        Directory.Delete(Path.GetDirectoryName(nestedDirectory)!, recursive: true);
    }

    [Fact]
    public async Task GetByIdAsync_IsCaseInsensitive()
    {
        var order = CreateSubmittedOrder("Order-Case", "ADAUSDT");
        await _sut.SaveAsync(order);

        var retrieved = await _sut.GetByIdAsync(OrderId.From("order-case"));

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOrdersSortedBySubmittedAtDescending()
    {
        var oldest = CreateSubmittedOrder("order-oldest", "BTCUSDT", submittedAt: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newest = CreateSubmittedOrder("order-newest", "ETHUSDT", submittedAt: new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero));
        var middle = CreateSubmittedOrder("order-middle", "ADAUSDT", submittedAt: new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero));

        await _sut.SaveAsync(oldest);
        await _sut.SaveAsync(newest);
        await _sut.SaveAsync(middle);

        var orders = await _sut.GetAllAsync();

        orders.Select(order => order.Id.Value).Should().Equal("order-newest", "order-middle", "order-oldest");
    }

    [Fact]
    public async Task ReloadAsync_RefreshesCacheFromExternalChanges()
    {
        var original = CreateSubmittedOrder("order-original", "BTCUSDT");
        await _sut.SaveAsync(original);

        using var otherRepository = new JsonOrderRepository(_testFilePath);
        await otherRepository.SaveAsync(CreateSubmittedOrder("order-external", "ETHUSDT"));

        await _sut.ReloadAsync();

        var orders = await _sut.GetAllAsync();
        orders.Should().HaveCount(2);
        orders.Select(order => order.Id.Value).Should().Contain(["order-original", "order-external"]);
    }

    [Fact]
    public async Task SaveAsync_WithinTransaction_ExposesStagedOrderWithoutPersistingUntilCommit()
    {
        var transactionCoordinator = new JsonTransactionCoordinator();
        var unitOfWork = new JsonTransactionalUnitOfWork(transactionCoordinator);
        using var repository = new JsonOrderRepository(_testFilePath, transactionCoordinator);
        var staged = CreateSubmittedOrder("order-staged", "XRPUSDT");

        await unitOfWork.BeginTransactionAsync();
        await repository.SaveAsync(staged);

        File.Exists(_testFilePath).Should().BeFalse();

        var readDuringTransaction = await repository.GetByIdAsync(staged.Id);
        readDuringTransaction.Should().NotBeNull();
        readDuringTransaction!.Pair.Symbol.Should().Be("XRPUSDT");

        await unitOfWork.RollbackAsync();

        var readAfterRollback = await repository.GetByIdAsync(staged.Id);
        readAfterRollback.Should().BeNull();
        File.Exists(_testFilePath).Should().BeFalse();
    }

    [Fact]
    public async Task EmptyFile_DoesNotThrowAndReturnsNoOrders()
    {
        await File.WriteAllTextAsync(_testFilePath, string.Empty);

        var orders = await _sut.GetAllAsync();

        orders.Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedJson_ThrowsOnLoad()
    {
        await File.WriteAllTextAsync(_testFilePath, "{ invalid json }");

        var act = () => _sut.GetAllAsync();

        await act.Should().ThrowAsync<JsonException>();
    }

    private static OrderLifecycle CreateSubmittedOrder(
        string orderId,
        string symbol,
        DateTimeOffset? submittedAt = null)
    {
        return OrderLifecycle.Submit(
            OrderId.From(orderId),
            TradingPair.Create(symbol, "USDT"),
            OrderSide.Buy,
            OrderType.Market,
            Quantity.Create(1m),
            Price.Create(1m),
            signalRule: "TestRule",
            timestamp: submittedAt);
    }
}
