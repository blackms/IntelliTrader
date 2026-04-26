using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Trailing;
using IntelliTrader.Core;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;
using IntelliTrader.Infrastructure.Transactions;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;

namespace IntelliTrader.Infrastructure.Tests.Integration;

/// <summary>
/// Shared test fixture for infrastructure integration tests.
/// Provides common setup and teardown for file-based repositories and mock services.
/// </summary>
public class InfrastructureTestFixture : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public Mock<IExchangeService> ExchangeServiceMock { get; }
    public Mock<ISignalsService> SignalsServiceMock { get; }
    public Mock<IExchangePort> ExchangePortMock { get; }
    public Mock<ISignalProviderPort> SignalProviderMock { get; }

    public InfrastructureTestFixture()
    {
        ExchangeServiceMock = new Mock<IExchangeService>();
        SignalsServiceMock = new Mock<ISignalsService>();
        ExchangePortMock = new Mock<IExchangePort>();
        SignalProviderMock = new Mock<ISignalProviderPort>();
    }

    /// <summary>
    /// Creates a temporary file path for testing.
    /// </summary>
    public string CreateTempFilePath(string prefix = "test")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}.json");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Creates a temporary directory for testing.
    /// </summary>
    public string CreateTempDirectory(string prefix = "test")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    /// <summary>
    /// Creates a JsonPositionRepository with a temporary file.
    /// </summary>
    public JsonPositionRepository CreatePositionRepository(string? filePath = null)
    {
        filePath ??= CreateTempFilePath("positions");
        return new JsonPositionRepository(filePath);
    }

    public void RegisterIsolatedEventPersistence(
        ContainerBuilder builder,
        JsonTransactionCoordinator transactionCoordinator,
        string prefix,
        string? portfolioBootstrapPath = null)
    {
        var outboxPath = CreateTempFilePath($"{prefix}_outbox");
        var inboxPath = CreateTempFilePath($"{prefix}_handler_inbox");
        var readModelPath = CreateTempFilePath($"{prefix}_order_read_model");
        var portfolioReadModelPath = CreateTempFilePath($"{prefix}_portfolio_read_model");
        var positionReadModelPath = CreateTempFilePath($"{prefix}_position_read_model");
        var tradingStateReadModelPath = CreateTempFilePath($"{prefix}_trading_state_read_model");

        builder.Register(_ => new JsonDomainEventOutbox(outboxPath, transactionCoordinator))
            .As<IDomainEventOutbox>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ => new JsonDomainEventHandlerInbox(inboxPath))
            .As<IDomainEventHandlerInbox>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ => new JsonOrderReadModel(readModelPath))
            .As<IOrderReadModel>()
            .As<IOrderReadModelProjectionWriter>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ => new JsonPortfolioReadModel(portfolioReadModelPath, portfolioBootstrapPath))
            .As<IPortfolioReadModel>()
            .As<IPortfolioReadModelProjectionWriter>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ => new JsonPositionReadModel(positionReadModelPath))
            .As<IPositionReadModel>()
            .As<IPositionReadModelProjectionWriter>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ => new JsonTradingStateReadModel(tradingStateReadModelPath))
            .As<ITradingStateReadModel>()
            .As<ITradingStateReadModelProjectionWriter>()
            .AsSelf()
            .SingleInstance();
    }

    /// <summary>
    /// Creates a TrailingManager with a mock exchange port.
    /// </summary>
    public TrailingManager CreateTrailingManager()
    {
        return new TrailingManager(ExchangePortMock.Object);
    }

    /// <summary>
    /// Creates a logger mock for a specific type.
    /// </summary>
    public Mock<ILogger<T>> CreateLoggerMock<T>()
    {
        return new Mock<ILogger<T>>();
    }

    public void Dispose()
    {
        // Clean up temp files
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Clean up temp directories
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
