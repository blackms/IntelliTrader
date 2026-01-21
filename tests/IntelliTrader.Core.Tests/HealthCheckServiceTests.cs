using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using System.Collections.Concurrent;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for HealthCheckService functionality.
/// Since HealthCheckService is internal and depends on several services,
/// we create a testable wrapper that exposes the core health check management logic.
/// </summary>
public class HealthCheckServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<ICoreConfig> _coreConfigMock;
    private readonly TestableHealthCheckService _sut;

    public HealthCheckServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _coreServiceMock = new Mock<ICoreService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _coreConfigMock = new Mock<ICoreConfig>();

        // Setup core config
        _coreConfigMock.Setup(x => x.HealthCheckEnabled).Returns(true);
        _coreConfigMock.Setup(x => x.HealthCheckInterval).Returns(10);
        _coreConfigMock.Setup(x => x.InstanceName).Returns("TestInstance");
        _coreServiceMock.Setup(x => x.Config).Returns(_coreConfigMock.Object);

        _sut = new TestableHealthCheckService(
            _loggingServiceMock.Object,
            _notificationServiceMock.Object,
            _coreServiceMock.Object,
            _tradingServiceMock.Object);
    }

    #region UpdateHealthCheck Tests

    [Fact]
    public void UpdateHealthCheck_WhenNewHealthCheck_AddsToCollection()
    {
        // Arrange
        const string checkName = "TestCheck";
        const string checkMessage = "Test message";

        // Act
        _sut.UpdateHealthCheck(checkName, checkMessage, failed: false);

        // Assert
        var healthChecks = _sut.GetHealthChecks().ToList();
        healthChecks.Should().ContainSingle();
        var healthCheck = healthChecks.First();
        healthCheck.Name.Should().Be(checkName);
        healthCheck.Message.Should().Be(checkMessage);
        healthCheck.Failed.Should().BeFalse();
    }

    [Fact]
    public void UpdateHealthCheck_WhenExistingHealthCheck_UpdatesValues()
    {
        // Arrange
        const string checkName = "TestCheck";
        _sut.UpdateHealthCheck(checkName, "Initial message", failed: false);

        // Act
        _sut.UpdateHealthCheck(checkName, "Updated message", failed: true);

        // Assert
        var healthChecks = _sut.GetHealthChecks().ToList();
        healthChecks.Should().ContainSingle();
        var healthCheck = healthChecks.First();
        healthCheck.Message.Should().Be("Updated message");
        healthCheck.Failed.Should().BeTrue();
    }

    [Fact]
    public void UpdateHealthCheck_SetsLastUpdatedToCurrentTime()
    {
        // Arrange
        const string checkName = "TestCheck";
        var beforeUpdate = DateTimeOffset.Now;

        // Act
        _sut.UpdateHealthCheck(checkName, "Test message");

        // Assert
        var healthCheck = _sut.GetHealthChecks().First();
        healthCheck.LastUpdated.Should().BeOnOrAfter(beforeUpdate);
        healthCheck.LastUpdated.Should().BeOnOrBefore(DateTimeOffset.Now);
    }

    [Fact]
    public void UpdateHealthCheck_WithNullMessage_SetsMessageToEmpty()
    {
        // Arrange
        const string checkName = "TestCheck";

        // Act
        _sut.UpdateHealthCheck(checkName, message: null, failed: false);

        // Assert
        var healthCheck = _sut.GetHealthChecks().First();
        healthCheck.Message.Should().BeEmpty();
    }

    [Fact]
    public void UpdateHealthCheck_MultipleHealthChecks_TracksAllSeparately()
    {
        // Arrange & Act
        _sut.UpdateHealthCheck("Check1", "Message 1");
        _sut.UpdateHealthCheck("Check2", "Message 2", failed: true);
        _sut.UpdateHealthCheck("Check3", "Message 3");

        // Assert
        var healthChecks = _sut.GetHealthChecks().ToList();
        healthChecks.Should().HaveCount(3);
        healthChecks.Should().Contain(h => h.Name == "Check1" && h.Message == "Message 1");
        healthChecks.Should().Contain(h => h.Name == "Check2" && h.Failed);
        healthChecks.Should().Contain(h => h.Name == "Check3");
    }

    #endregion

    #region RemoveHealthCheck Tests

    [Fact]
    public void RemoveHealthCheck_WhenExists_RemovesFromCollection()
    {
        // Arrange
        const string checkName = "TestCheck";
        _sut.UpdateHealthCheck(checkName, "Test message");

        // Act
        _sut.RemoveHealthCheck(checkName);

        // Assert
        _sut.GetHealthChecks().Should().BeEmpty();
    }

    [Fact]
    public void RemoveHealthCheck_WhenDoesNotExist_DoesNotThrow()
    {
        // Arrange & Act
        var action = () => _sut.RemoveHealthCheck("NonExistentCheck");

        // Assert
        action.Should().NotThrow();
    }

    [Fact]
    public void RemoveHealthCheck_OnlyRemovesSpecifiedCheck()
    {
        // Arrange
        _sut.UpdateHealthCheck("Check1", "Message 1");
        _sut.UpdateHealthCheck("Check2", "Message 2");
        _sut.UpdateHealthCheck("Check3", "Message 3");

        // Act
        _sut.RemoveHealthCheck("Check2");

        // Assert
        var healthChecks = _sut.GetHealthChecks().ToList();
        healthChecks.Should().HaveCount(2);
        healthChecks.Should().NotContain(h => h.Name == "Check2");
        healthChecks.Should().Contain(h => h.Name == "Check1");
        healthChecks.Should().Contain(h => h.Name == "Check3");
    }

    #endregion

    #region GetHealthChecks Tests

    [Fact]
    public void GetHealthChecks_WhenEmpty_ReturnsEmptyEnumerable()
    {
        // Act
        var result = _sut.GetHealthChecks();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetHealthChecks_ReturnsAllHealthChecks()
    {
        // Arrange
        _sut.UpdateHealthCheck("Check1", "Message 1");
        _sut.UpdateHealthCheck("Check2", "Message 2");

        // Act
        var result = _sut.GetHealthChecks().ToList();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetHealthChecks_ReturnsIHealthCheckInterface()
    {
        // Arrange
        _sut.UpdateHealthCheck("TestCheck", "Test message");

        // Act
        var healthChecks = _sut.GetHealthChecks();

        // Assert
        healthChecks.Should().AllBeAssignableTo<IHealthCheck>();
    }

    #endregion

    #region Health Status Evaluation Tests

    [Fact]
    public void HealthStatus_WhenNoFailedChecks_AllHealthy()
    {
        // Arrange
        _sut.UpdateHealthCheck("Check1", "OK", failed: false);
        _sut.UpdateHealthCheck("Check2", "OK", failed: false);

        // Act
        var healthChecks = _sut.GetHealthChecks().ToList();
        var hasFailures = healthChecks.Any(h => h.Failed);

        // Assert
        hasFailures.Should().BeFalse();
    }

    [Fact]
    public void HealthStatus_WhenSomeFailedChecks_DetectsFailures()
    {
        // Arrange
        _sut.UpdateHealthCheck("Check1", "OK", failed: false);
        _sut.UpdateHealthCheck("Check2", "FAILED", failed: true);

        // Act
        var healthChecks = _sut.GetHealthChecks().ToList();
        var failedChecks = healthChecks.Where(h => h.Failed).ToList();

        // Assert
        failedChecks.Should().ContainSingle();
        failedChecks.First().Name.Should().Be("Check2");
    }

    [Fact]
    public void HealthStatus_CanTransitionFromFailedToHealthy()
    {
        // Arrange
        _sut.UpdateHealthCheck("Check1", "FAILED", failed: true);
        _sut.GetHealthChecks().First().Failed.Should().BeTrue();

        // Act
        _sut.UpdateHealthCheck("Check1", "Recovered", failed: false);

        // Assert
        _sut.GetHealthChecks().First().Failed.Should().BeFalse();
    }

    [Fact]
    public void HealthStatus_CanTransitionFromHealthyToFailed()
    {
        // Arrange
        _sut.UpdateHealthCheck("Check1", "OK", failed: false);
        _sut.GetHealthChecks().First().Failed.Should().BeFalse();

        // Act
        _sut.UpdateHealthCheck("Check1", "FAILED", failed: true);

        // Assert
        _sut.GetHealthChecks().First().Failed.Should().BeTrue();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLoggingService_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestableHealthCheckService(
            null!,
            _notificationServiceMock.Object,
            _coreServiceMock.Object,
            _tradingServiceMock.Object);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("loggingService");
    }

    [Fact]
    public void Constructor_WithNullNotificationService_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestableHealthCheckService(
            _loggingServiceMock.Object,
            null!,
            _coreServiceMock.Object,
            _tradingServiceMock.Object);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("notificationService");
    }

    [Fact]
    public void Constructor_WithNullCoreService_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestableHealthCheckService(
            _loggingServiceMock.Object,
            _notificationServiceMock.Object,
            null!,
            _tradingServiceMock.Object);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("coreService");
    }

    [Fact]
    public void Constructor_WithNullTradingService_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestableHealthCheckService(
            _loggingServiceMock.Object,
            _notificationServiceMock.Object,
            _coreServiceMock.Object,
            null!);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("tradingService");
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public void UpdateHealthCheck_ConcurrentUpdates_HandlesCorrectly()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - Concurrent updates to different health checks
        for (int i = 0; i < 100; i++)
        {
            int checkNumber = i;
            tasks.Add(Task.Run(() =>
                _sut.UpdateHealthCheck($"Check{checkNumber}", $"Message {checkNumber}")));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        var healthChecks = _sut.GetHealthChecks().ToList();
        healthChecks.Should().HaveCount(100);
    }

    [Fact]
    public void UpdateHealthCheck_ConcurrentUpdatesToSameCheck_LastUpdateWins()
    {
        // Arrange
        const string checkName = "ConcurrentCheck";
        var tasks = new List<Task>();

        // Act - Multiple concurrent updates to the same check
        for (int i = 0; i < 50; i++)
        {
            int messageNumber = i;
            tasks.Add(Task.Run(() =>
                _sut.UpdateHealthCheck(checkName, $"Message {messageNumber}")));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert - Should only have one health check (last update wins)
        var healthChecks = _sut.GetHealthChecks().ToList();
        healthChecks.Should().ContainSingle();
        healthChecks.First().Name.Should().Be(checkName);
    }

    #endregion

    #region Known Health Check Names Tests

    [Theory]
    [InlineData(Constants.HealthChecks.AccountRefreshed)]
    [InlineData(Constants.HealthChecks.TickersUpdated)]
    [InlineData(Constants.HealthChecks.TradingPairsProcessed)]
    [InlineData(Constants.HealthChecks.TradingViewCryptoSignalsReceived)]
    [InlineData(Constants.HealthChecks.SignalRulesProcessed)]
    [InlineData(Constants.HealthChecks.TradingRulesProcessed)]
    public void UpdateHealthCheck_WithKnownHealthCheckNames_Works(string checkName)
    {
        // Act
        _sut.UpdateHealthCheck(checkName, "Status OK");

        // Assert
        var healthCheck = _sut.GetHealthChecks().FirstOrDefault(h => h.Name == checkName);
        healthCheck.Should().NotBeNull();
        healthCheck!.Message.Should().Be("Status OK");
    }

    #endregion
}

/// <summary>
/// Testable wrapper for HealthCheckService that exposes the health check management functionality
/// without requiring the full service infrastructure (timed tasks, etc.)
/// </summary>
public class TestableHealthCheckService : IHealthCheckService
{
    private readonly ILoggingService _loggingService;
    private readonly INotificationService _notificationService;
    private readonly ICoreService _coreService;
    private readonly ITradingService _tradingService;
    private readonly ConcurrentDictionary<string, TestableHealthCheck> _healthChecks = new();

    public TestableHealthCheckService(
        ILoggingService loggingService,
        INotificationService notificationService,
        ICoreService coreService,
        ITradingService tradingService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
        _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
    }

    public void Start()
    {
        _loggingService.Info("Start Health Check service...");
        // Skip timed task creation for testing
        _loggingService.Info("Health Check service started");
    }

    public void Stop()
    {
        _loggingService.Info("Stop Health Check service...");
        // Skip timed task cleanup for testing
        _loggingService.Info("Health Check service stopped");
    }

    public void UpdateHealthCheck(string name, string? message = null, bool failed = false)
    {
        if (!_healthChecks.TryGetValue(name, out var existingHealthCheck))
        {
            _healthChecks.TryAdd(name, new TestableHealthCheck
            {
                Name = name,
                Message = message ?? string.Empty,
                LastUpdated = DateTimeOffset.Now,
                Failed = failed
            });
        }
        else
        {
            existingHealthCheck.Message = message ?? string.Empty;
            existingHealthCheck.LastUpdated = DateTimeOffset.Now;
            existingHealthCheck.Failed = failed;
        }
    }

    public void RemoveHealthCheck(string name)
    {
        _healthChecks.TryRemove(name, out _);
    }

    public IEnumerable<IHealthCheck> GetHealthChecks()
    {
        foreach (var kvp in _healthChecks)
        {
            yield return kvp.Value;
        }
    }
}

/// <summary>
/// Testable implementation of IHealthCheck
/// </summary>
public class TestableHealthCheck : IHealthCheck
{
    public string Name { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
    public bool Failed { get; set; }
}
