using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using System.Reflection;

#pragma warning disable CS0612 // Type or member is obsolete

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Additional tests for CoreService covering Running property, Version format,
/// and task interaction patterns not covered by existing tests.
/// </summary>
public class CoreServiceAdditionalTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<IWebService> _webServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly Mock<IAlertingService> _alertingServiceMock;
    private readonly Mock<IActiveOrderRefreshService> _activeOrderRefreshServiceMock;
    private readonly Mock<IApplicationContext> _applicationContextMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
    private readonly Mock<ISecretRotationService> _secretRotationServiceMock;
    private readonly ICoreService _sut;

    public CoreServiceAdditionalTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _webServiceMock = new Mock<IWebService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();
        _alertingServiceMock = new Mock<IAlertingService>();
        _activeOrderRefreshServiceMock = new Mock<IActiveOrderRefreshService>();
        _applicationContextMock = new Mock<IApplicationContext>();
        _applicationContextMock.Setup(x => x.Speed).Returns(1.0);
        _configProviderMock = new Mock<IConfigProvider>();
        _secretRotationServiceMock = new Mock<ISecretRotationService>();

        SetupDefaultMocks();

        _sut = CreateCoreService();
    }

    private void SetupDefaultMocks()
    {
        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        _backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        var tradingConfigMock = new Mock<ITradingConfig>();
        tradingConfigMock.Setup(x => x.Enabled).Returns(false);
        _tradingServiceMock.Setup(x => x.Config).Returns(tradingConfigMock.Object);

        var notificationConfigMock = new Mock<INotificationConfig>();
        notificationConfigMock.Setup(x => x.Enabled).Returns(false);
        _notificationServiceMock.Setup(x => x.Config).Returns(notificationConfigMock.Object);

        var webConfigMock = new Mock<IWebConfig>();
        webConfigMock.Setup(x => x.Enabled).Returns(false);
        _webServiceMock.Setup(x => x.Config).Returns(webConfigMock.Object);

        _notificationServiceMock.Setup(x => x.NotifyAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _notificationServiceMock.Setup(x => x.StartAsync())
            .Returns(Task.CompletedTask);
    }

    private ICoreService CreateCoreService()
    {
        var coreServiceType = typeof(ICoreService).Assembly
            .GetTypes()
            .First(t => t.Name == "CoreService" && !t.IsInterface);

        var constructor = coreServiceType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .First();

        var instance = constructor.Invoke(new object[]
        {
            _loggingServiceMock.Object,
            _notificationServiceMock.Object,
            _healthCheckServiceMock.Object,
            _tradingServiceMock.Object,
            _webServiceMock.Object,
            _backtestingServiceMock.Object,
            _alertingServiceMock.Object,
            _activeOrderRefreshServiceMock.Object,
            _applicationContextMock.Object,
            _configProviderMock.Object,
            new Lazy<ISecretRotationService>(() => _secretRotationServiceMock.Object)
        });

        return (ICoreService)instance;
    }

    #region Version Tests

    [Fact]
    public void Version_IsNotNullOrEmpty()
    {
        _sut.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Version_ContainsDotSeparatedNumbers()
    {
        // Version format should be like "1.0.0.0" or similar
        _sut.Version.Should().Contain(".");
        var parts = _sut.Version.Split('.');
        parts.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Running Property Tests

    [Fact]
    public void Running_InitiallyFalse()
    {
        _sut.Running.Should().BeFalse();
    }

    #endregion

    #region Multiple Task Operations Tests

    [Fact]
    public void AddTask_MultipleTasks_AllRetrievable()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 5)
            .Select(i => (Name: $"Task{i}", Task: new TestTimedTaskForAdditional()))
            .ToList();

        // Act
        foreach (var (name, task) in tasks)
        {
            _sut.AddTask(name, task);
        }

        // Assert
        foreach (var (name, task) in tasks)
        {
            _sut.GetTask(name).Should().BeSameAs(task);
        }
    }

    [Fact]
    public void StartAllTasks_ThenStopAllTasks_AllTasksToggle()
    {
        // Arrange
        var task1 = new TestTimedTaskForAdditional();
        var task2 = new TestTimedTaskForAdditional();
        var task3 = new TestTimedTaskForAdditional();
        _sut.AddTask("A", task1);
        _sut.AddTask("B", task2);
        _sut.AddTask("C", task3);

        // Act & Assert - Start
        _sut.StartAllTasks();
        task1.IsEnabled.Should().BeTrue();
        task2.IsEnabled.Should().BeTrue();
        task3.IsEnabled.Should().BeTrue();

        // Act & Assert - Stop
        _sut.StopAllTasks();
        task1.IsEnabled.Should().BeFalse();
        task2.IsEnabled.Should().BeFalse();
        task3.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void RemoveAllTasks_AfterAdding_ClearsAll()
    {
        // Arrange
        _sut.AddTask("X", new TestTimedTaskForAdditional());
        _sut.AddTask("Y", new TestTimedTaskForAdditional());
        _sut.GetAllTasks().Should().HaveCount(2);

        // Act
        _sut.RemoveAllTasks();

        // Assert
        _sut.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void StartTask_ThenStop_TaskIsDisabled()
    {
        // Arrange
        var task = new TestTimedTaskForAdditional();
        _sut.AddTask("SingleTask", task);

        // Act
        _sut.StartTask("SingleTask");
        task.IsEnabled.Should().BeTrue();
        _sut.StopTask("SingleTask");

        // Assert
        task.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void GetTask_NonExistentTask_ReturnsNull()
    {
        _sut.GetTask("DoesNotExist").Should().BeNull();
    }

    [Fact]
    public void RemoveTask_NonExistentTask_DoesNotThrow()
    {
        var action = () => _sut.RemoveTask("DoesNotExist");
        action.Should().NotThrow();
    }

    #endregion

    private class TestTimedTaskForAdditional : HighResolutionTimedTask
    {
        public override void Run() { }
    }
}
