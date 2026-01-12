using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Trading.Trailing;
using IntelliTrader.Core;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
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
