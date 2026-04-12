using FluentAssertions;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Base;
using Moq;
using System.Reflection;
using Xunit;

namespace IntelliTrader.Exchange.Tests;

/// <summary>
/// Tests for the SecretRotationService (internal class in Exchange.Base).
/// Uses reflection to instantiate the internal type since InternalsVisibleTo
/// is not set for this assembly from Exchange.Base.
/// </summary>
public class SecretRotationServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IExchangeService> _exchangeServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly string _tempDir;

    public SecretRotationServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _exchangeServiceMock = new Mock<IExchangeService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _notificationServiceMock
            .Setup(n => n.NotifyAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _tempDir = Path.Combine(Path.GetTempPath(), $"intellitrader_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private ISecretRotationService CreateService(SecretRotationConfig? config = null)
    {
        config ??= new SecretRotationConfig
        {
            Enabled = true,
            KeysFilePath = Path.Combine(_tempDir, "keys.bin"),
            VerificationTimeoutSeconds = 5
        };

        var serviceType = typeof(ExchangeService).Assembly
            .GetType("IntelliTrader.Exchange.Base.SecretRotationService")!;

        var instance = Activator.CreateInstance(
            serviceType,
            _loggingServiceMock.Object,
            new Lazy<IExchangeService>(() => _exchangeServiceMock.Object),
            new Lazy<INotificationService>(() => _notificationServiceMock.Object),
            config);

        return (ISecretRotationService)instance!;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLoggingService_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceType = typeof(ExchangeService).Assembly
            .GetType("IntelliTrader.Exchange.Base.SecretRotationService")!;

        // Act
        var act = () => Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object?[]
            {
                null!,
                new Lazy<IExchangeService>(() => _exchangeServiceMock.Object),
                new Lazy<INotificationService>(() => _notificationServiceMock.Object),
                new SecretRotationConfig()
            },
            null);

        // Assert
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be("loggingService");
    }

    [Fact]
    public void Constructor_WithNullExchangeService_ThrowsArgumentNullException()
    {
        var serviceType = typeof(ExchangeService).Assembly
            .GetType("IntelliTrader.Exchange.Base.SecretRotationService")!;

        var act = () => Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object?[]
            {
                _loggingServiceMock.Object,
                null!,
                new Lazy<INotificationService>(() => _notificationServiceMock.Object),
                new SecretRotationConfig()
            },
            null);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be("exchangeService");
    }

    [Fact]
    public void Constructor_WithNullNotificationService_ThrowsArgumentNullException()
    {
        var serviceType = typeof(ExchangeService).Assembly
            .GetType("IntelliTrader.Exchange.Base.SecretRotationService")!;

        var act = () => Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object?[]
            {
                _loggingServiceMock.Object,
                new Lazy<IExchangeService>(() => _exchangeServiceMock.Object),
                null!,
                new SecretRotationConfig()
            },
            null);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be("notificationService");
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        var serviceType = typeof(ExchangeService).Assembly
            .GetType("IntelliTrader.Exchange.Base.SecretRotationService")!;

        var act = () => Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object?[]
            {
                _loggingServiceMock.Object,
                new Lazy<IExchangeService>(() => _exchangeServiceMock.Object),
                new Lazy<INotificationService>(() => _notificationServiceMock.Object),
                null!
            },
            null);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .Which.ParamName.Should().Be("config");
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Act
        var service = CreateService();

        // Assert
        service.Should().NotBeNull();
        service.Should().BeAssignableTo<ISecretRotationService>();
    }

    #endregion

    #region Start Tests

    [Fact]
    public void Start_WhenDisabled_LogsInfoAndReturns()
    {
        // Arrange
        var config = new SecretRotationConfig
        {
            Enabled = false,
            KeysFilePath = Path.Combine(_tempDir, "keys.bin")
        };
        var service = CreateService(config);

        // Act
        service.Start();

        // Assert
        _loggingServiceMock.Verify(
            l => l.Info(It.Is<string>(s => s.Contains("disabled")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Start_WhenEnabled_LogsStartMessage()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.Start();

        // Assert
        _loggingServiceMock.Verify(
            l => l.Info(It.Is<string>(s => s.Contains("started")), It.IsAny<Exception>()),
            Times.Once);

        // Cleanup
        service.Stop();
    }

    [Fact]
    public void Start_WhenDirectoryDoesNotExist_CreatesDirectory()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "nonexistent_subdir");
        var config = new SecretRotationConfig
        {
            Enabled = true,
            KeysFilePath = Path.Combine(subDir, "keys.bin")
        };
        var service = CreateService(config);

        // Act
        service.Start();

        // Assert
        Directory.Exists(subDir).Should().BeTrue();

        // Cleanup
        service.Stop();
    }

    #endregion

    #region Stop Tests

    [Fact]
    public void Stop_AfterStart_LogsStopMessage()
    {
        // Arrange
        var service = CreateService();
        service.Start();

        // Act
        service.Stop();

        // Assert
        _loggingServiceMock.Verify(
            l => l.Info(It.Is<string>(s => s.Contains("stopped")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Stop_WithoutStart_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => service.Stop();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region RotateCredentials Tests

    [Fact]
    public async Task RotateCredentialsAsync_WhenKeysFileNotFound_ReturnsFalse()
    {
        // Arrange - keys file doesn't exist
        var config = new SecretRotationConfig
        {
            Enabled = true,
            KeysFilePath = Path.Combine(_tempDir, "nonexistent_keys.bin")
        };
        var service = CreateService(config);

        // Act
        var result = await service.RotateCredentialsAsync();

        // Assert
        result.Should().BeFalse();
        _loggingServiceMock.Verify(
            l => l.Error(It.Is<string>(s => s.Contains("not found")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public async Task RotateCredentialsAsync_WhenKeysFileNotFound_SendsNotification()
    {
        // Arrange
        var config = new SecretRotationConfig
        {
            Enabled = true,
            KeysFilePath = Path.Combine(_tempDir, "nonexistent_keys.bin")
        };
        var service = CreateService(config);

        // Act
        await service.RotateCredentialsAsync();

        // Assert
        _notificationServiceMock.Verify(
            n => n.NotifyAsync(It.Is<string>(s => s.Contains("FAILED") && s.Contains("not found"))),
            Times.Once);
    }

    [Fact]
    public async Task RotateCredentialsAsync_ConcurrentCalls_SkipsSecondCall()
    {
        // Arrange - create a keys file so the first call gets past the file check
        var keysPath = Path.Combine(_tempDir, "keys.bin");
        await File.WriteAllTextAsync(keysPath, "dummy-keys");

        var config = new SecretRotationConfig
        {
            Enabled = true,
            KeysFilePath = keysPath
        };
        var service = CreateService(config);

        // Use reflection to set _isRotating to true to simulate concurrent call
        var serviceType = service.GetType();
        var isRotatingField = serviceType.GetField("_isRotating", BindingFlags.NonPublic | BindingFlags.Instance)!;
        isRotatingField.SetValue(service, true);

        // Act
        var result = await service.RotateCredentialsAsync();

        // Assert
        result.Should().BeFalse();
        _loggingServiceMock.Verify(
            l => l.Warning(It.Is<string>(s => s.Contains("already in progress")), It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_AfterStart_StopsService()
    {
        // Arrange
        var service = CreateService();
        service.Start();

        // Act
        ((IDisposable)service).Dispose();

        // Assert
        _loggingServiceMock.Verify(
            l => l.Info(It.Is<string>(s => s.Contains("stopped")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => ((IDisposable)service).Dispose();

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}

/// <summary>
/// Tests for the SecretRotationConfig model.
/// </summary>
public class SecretRotationConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Act
        var config = new SecretRotationConfig();

        // Assert
        config.Enabled.Should().BeFalse();
        config.VerificationTimeoutSeconds.Should().Be(30);
        config.KeysFilePath.Should().Be("keys.bin");
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange & Act
        var config = new SecretRotationConfig
        {
            Enabled = true,
            VerificationTimeoutSeconds = 60,
            KeysFilePath = "/custom/path/keys.bin"
        };

        // Assert
        config.Enabled.Should().BeTrue();
        config.VerificationTimeoutSeconds.Should().Be(60);
        config.KeysFilePath.Should().Be("/custom/path/keys.bin");
    }
}

/// <summary>
/// Tests for ExchangeConfig model.
/// </summary>
public class ExchangeConfigTests
{
    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange & Act
        var config = new ExchangeConfig
        {
            KeysPath = "/path/to/keys.bin",
            RateLimitOccurences = 10,
            RateLimitTimeframe = 60
        };

        // Assert
        config.KeysPath.Should().Be("/path/to/keys.bin");
        config.RateLimitOccurences.Should().Be(10);
        config.RateLimitTimeframe.Should().Be(60);
    }

    [Fact]
    public void DefaultValues_AreZeroAndNull()
    {
        // Act
        var config = new ExchangeConfig();

        // Assert
        config.KeysPath.Should().BeNull();
        config.RateLimitOccurences.Should().Be(0);
        config.RateLimitTimeframe.Should().Be(0);
    }
}

/// <summary>
/// Additional tests for ExchangeService base class behavior: default virtual
/// member implementations, TickersUpdated event, and UpdateCredentials/ReconnectWebSocket.
/// </summary>
public class ExchangeServiceBaseTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<IConfigProvider> _configProviderMock;

    public ExchangeServiceBaseTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _coreServiceMock = new Mock<ICoreService>();
        _configProviderMock = new Mock<IConfigProvider>();
    }

    private TestableExchangeService CreateService()
    {
        return new TestableExchangeService(
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _coreServiceMock.Object,
            _configProviderMock.Object);
    }

    [Fact]
    public void IsWebSocketConnected_DefaultsToFalse()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.IsWebSocketConnected.Should().BeFalse();
    }

    [Fact]
    public void IsRestFallbackActive_DefaultsToFalse()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.IsRestFallbackActive.Should().BeFalse();
    }

    [Fact]
    public void TimeSinceLastTickerUpdate_DefaultsToZero()
    {
        // Arrange
        var service = CreateService();

        // Assert
        service.TimeSinceLastTickerUpdate.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ReconnectWebSocketAsync_DefaultImplementation_CompletesSuccessfully()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.ReconnectWebSocketAsync();

        // Assert - should log debug message
        _loggingServiceMock.Verify(
            l => l.Debug(It.Is<string>(s => s.Contains("not implemented")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void UpdateCredentials_DefaultImplementation_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.UpdateCredentials("/some/path");

        // Assert
        result.Should().BeFalse();
        _loggingServiceMock.Verify(
            l => l.Debug(It.Is<string>(s => s.Contains("not implemented")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public void TickersUpdated_WhenSubscribed_ReceivesTickerData()
    {
        // Arrange
        var service = CreateService();
        IReadOnlyCollection<ITicker>? receivedTickers = null;
        service.TickersUpdated += tickers => receivedTickers = tickers;

        var tickerList = new List<ITicker>
        {
            new Ticker { Pair = "BTCUSDT", LastPrice = 50000m },
            new Ticker { Pair = "ETHUSDT", LastPrice = 3000m }
        };

        // Act - use reflection to call OnTickersUpdated
        var method = typeof(ExchangeService).GetMethod(
            "OnTickersUpdated",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, new object[] { tickerList });

        // Assert
        receivedTickers.Should().NotBeNull();
        receivedTickers.Should().HaveCount(2);
    }

    [Fact]
    public void TickersUpdated_WhenNotSubscribed_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        var tickerList = new List<ITicker>
        {
            new Ticker { Pair = "BTCUSDT", LastPrice = 50000m }
        };

        // Act
        var method = typeof(ExchangeService).GetMethod(
            "OnTickersUpdated",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var act = () => method.Invoke(service, new object[] { tickerList });

        // Assert
        act.Should().NotThrow();
    }
}
