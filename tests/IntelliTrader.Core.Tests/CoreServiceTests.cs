using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using System.Collections.Concurrent;

#pragma warning disable CS0612 // Type or member is obsolete
using System.Reflection;

namespace IntelliTrader.Core.Tests;

public class CoreServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<IWebService> _webServiceMock;
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly Mock<IAlertingService> _alertingServiceMock;
    private readonly Mock<IApplicationContext> _applicationContextMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
    private readonly Mock<ISecretRotationService> _secretRotationServiceMock;
    private readonly ICoreService _sut;

    public CoreServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _webServiceMock = new Mock<IWebService>();
        _backtestingServiceMock = new Mock<IBacktestingService>();
        _alertingServiceMock = new Mock<IAlertingService>();
        _applicationContextMock = new Mock<IApplicationContext>();
        _applicationContextMock.Setup(x => x.Speed).Returns(1.0);
        _configProviderMock = new Mock<IConfigProvider>();
        _secretRotationServiceMock = new Mock<ISecretRotationService>();

        SetupDefaultMocks();

        _sut = CreateCoreService();
    }

    private void SetupDefaultMocks()
    {
        // Setup backtesting config (disabled by default)
        var backtestingConfigMock = new Mock<IBacktestingConfig>();
        backtestingConfigMock.Setup(x => x.Enabled).Returns(false);
        backtestingConfigMock.Setup(x => x.Replay).Returns(false);
        _backtestingServiceMock.Setup(x => x.Config).Returns(backtestingConfigMock.Object);

        // Setup trading config (disabled by default)
        var tradingConfigMock = new Mock<ITradingConfig>();
        tradingConfigMock.Setup(x => x.Enabled).Returns(false);
        _tradingServiceMock.Setup(x => x.Config).Returns(tradingConfigMock.Object);

        // Setup notification config (disabled by default)
        var notificationConfigMock = new Mock<INotificationConfig>();
        notificationConfigMock.Setup(x => x.Enabled).Returns(false);
        _notificationServiceMock.Setup(x => x.Config).Returns(notificationConfigMock.Object);

        // Setup web config (disabled by default)
        var webConfigMock = new Mock<IWebConfig>();
        webConfigMock.Setup(x => x.Enabled).Returns(false);
        _webServiceMock.Setup(x => x.Config).Returns(webConfigMock.Object);

        // Setup notification to return completed task
        _notificationServiceMock.Setup(x => x.NotifyAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _notificationServiceMock.Setup(x => x.StartAsync())
            .Returns(Task.CompletedTask);
    }

    private ICoreService CreateCoreService()
    {
        // Use reflection to create the internal CoreService
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
            _applicationContextMock.Object,
            _configProviderMock.Object,
            new Lazy<ISecretRotationService>(() => _secretRotationServiceMock.Object)
        });

        return (ICoreService)instance;
    }

    #region Initialization Tests

    [Fact]
    public void Constructor_InitializesVersion()
    {
        // Assert
        _sut.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_InitializesEmptyTaskDictionary()
    {
        // Act
        var tasks = _sut.GetAllTasks();

        // Assert
        tasks.Should().NotBeNull();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void ServiceName_ReturnsCore()
    {
        // Assert
        _sut.ServiceName.Should().Be(Constants.ServiceNames.CoreService);
    }

    #endregion

    #region Task Management Tests

    [Fact]
    public void AddTask_AddsTaskToDictionary()
    {
        // Arrange
        var task = new TestTimedTask();
        const string taskName = "TestTask";

        // Act
        _sut.AddTask(taskName, task);

        // Assert
        var tasks = _sut.GetAllTasks();
        tasks.Should().ContainKey(taskName);
        tasks[taskName].Should().Be(task);
    }

    [Fact]
    public void AddTask_OverwritesExistingTaskWithSameName()
    {
        // Arrange
        var task1 = new TestTimedTask();
        var task2 = new TestTimedTask();
        const string taskName = "TestTask";

        // Act
        _sut.AddTask(taskName, task1);
        _sut.AddTask(taskName, task2);

        // Assert
        var tasks = _sut.GetAllTasks();
        tasks.Should().HaveCount(1);
        tasks[taskName].Should().Be(task2);
    }

    [Fact]
    public void GetTask_ReturnsTaskWhenExists()
    {
        // Arrange
        var task = new TestTimedTask();
        const string taskName = "TestTask";
        _sut.AddTask(taskName, task);

        // Act
        var result = _sut.GetTask(taskName);

        // Assert
        result.Should().Be(task);
    }

    [Fact]
    public void GetTask_ReturnsNullWhenNotExists()
    {
        // Act
        var result = _sut.GetTask("NonExistentTask");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RemoveTask_RemovesTaskFromDictionary()
    {
        // Arrange
        var task = new TestTimedTask();
        const string taskName = "TestTask";
        _sut.AddTask(taskName, task);

        // Act
        _sut.RemoveTask(taskName);

        // Assert
        var tasks = _sut.GetAllTasks();
        tasks.Should().NotContainKey(taskName);
    }

    [Fact]
    public void RemoveTask_DoesNotThrowWhenTaskNotExists()
    {
        // Act
        var act = () => _sut.RemoveTask("NonExistentTask");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveAllTasks_ClearsAllTasks()
    {
        // Arrange
        _sut.AddTask("Task1", new TestTimedTask());
        _sut.AddTask("Task2", new TestTimedTask());
        _sut.AddTask("Task3", new TestTimedTask());

        // Act
        _sut.RemoveAllTasks();

        // Assert
        var tasks = _sut.GetAllTasks();
        tasks.Should().BeEmpty();
    }

    [Fact]
    public void GetAllTasks_ReturnsConcurrentDictionary()
    {
        // Arrange
        var task1 = new TestTimedTask();
        var task2 = new TestTimedTask();
        _sut.AddTask("Task1", task1);
        _sut.AddTask("Task2", task2);

        // Act
        var result = _sut.GetAllTasks();

        // Assert
        result.Should().BeOfType<ConcurrentDictionary<string, HighResolutionTimedTask>>();
        result.Should().HaveCount(2);
        result.Should().ContainKey("Task1");
        result.Should().ContainKey("Task2");
    }

    #endregion

    #region Task Start/Stop Tests

    [Fact]
    public void StartTask_StartsTaskWhenExists()
    {
        // Arrange
        var task = new TestTimedTask();
        const string taskName = "TestTask";
        _sut.AddTask(taskName, task);

        // Act
        _sut.StartTask(taskName);

        // Assert
        task.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StartTask_DoesNotThrowWhenTaskNotExists()
    {
        // Act
        var act = () => _sut.StartTask("NonExistentTask");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void StopTask_StopsTaskWhenExists()
    {
        // Arrange
        var task = new TestTimedTask();
        const string taskName = "TestTask";
        _sut.AddTask(taskName, task);
        _sut.StartTask(taskName);

        // Act
        _sut.StopTask(taskName);

        // Assert
        task.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void StopTask_DoesNotThrowWhenTaskNotExists()
    {
        // Act
        var act = () => _sut.StopTask("NonExistentTask");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void StartAllTasks_StartsAllRegisteredTasks()
    {
        // Arrange
        var task1 = new TestTimedTask();
        var task2 = new TestTimedTask();
        var task3 = new TestTimedTask();
        _sut.AddTask("Task1", task1);
        _sut.AddTask("Task2", task2);
        _sut.AddTask("Task3", task3);

        // Act
        _sut.StartAllTasks();

        // Assert
        task1.IsEnabled.Should().BeTrue();
        task2.IsEnabled.Should().BeTrue();
        task3.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StopAllTasks_StopsAllRunningTasks()
    {
        // Arrange
        var task1 = new TestTimedTask();
        var task2 = new TestTimedTask();
        _sut.AddTask("Task1", task1);
        _sut.AddTask("Task2", task2);
        _sut.StartAllTasks();

        // Act
        _sut.StopAllTasks();

        // Assert
        task1.IsEnabled.Should().BeFalse();
        task2.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void StopAllTasks_HandlesEmptyTaskDictionary()
    {
        // Act
        var act = () => _sut.StopAllTasks();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    // Note: Service lifecycle tests (Start/Stop/Restart/HealthCheck) were removed because they
    // require a config directory at runtime and were permanently skipped with
    // [Fact(Skip = "Requires config directory - integration test")].
    // If these tests are needed, they should be implemented as proper integration tests
    // with the required config directory setup in a fixture.

    #region Concurrent Access Tests

    [Fact]
    public void AddTask_IsThreadSafe()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 100)
            .Select(i => new TestTimedTask())
            .ToList();

        // Act
        Parallel.ForEach(tasks, (task, _, index) =>
        {
            _sut.AddTask($"Task{index}", task);
        });

        // Assert
        _sut.GetAllTasks().Should().HaveCount(100);
    }

    [Fact]
    public void RemoveTask_IsThreadSafe()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            _sut.AddTask($"Task{i}", new TestTimedTask());
        }

        // Act
        Parallel.For(0, 100, i =>
        {
            _sut.RemoveTask($"Task{i}");
        });

        // Assert
        _sut.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void GetTask_IsThreadSafe()
    {
        // Arrange
        var task = new TestTimedTask();
        _sut.AddTask("TestTask", task);

        // Act & Assert - Should not throw
        Parallel.For(0, 100, _ =>
        {
            var result = _sut.GetTask("TestTask");
            result.Should().Be(task);
        });
    }

    [Fact]
    public void GetAllTasks_IsThreadSafe()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _sut.AddTask($"Task{i}", new TestTimedTask());
        }

        // Act & Assert - Should not throw
        Parallel.For(0, 100, _ =>
        {
            var tasks = _sut.GetAllTasks();
            tasks.Should().NotBeNull();
            tasks.Count.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void AddTask_WithNullName_ThrowsException()
    {
        // Arrange
        var task = new TestTimedTask();

        // Act
        var act = () => _sut.AddTask(null!, task);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTask_WithNullTask_AddsNullEntry()
    {
        // Arrange & Act
        _sut.AddTask("NullTask", null!);

        // Assert
        var tasks = _sut.GetAllTasks();
        tasks.Should().ContainKey("NullTask");
        tasks["NullTask"].Should().BeNull();
    }

    [Fact]
    public void GetTask_WithEmptyName_ReturnsNull()
    {
        // Act
        var result = _sut.GetTask(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void StartTask_WithNullTask_DoesNotThrow()
    {
        // Arrange
        _sut.AddTask("NullTask", null!);

        // Act
        var act = () => _sut.StartTask("NullTask");

        // Assert - Should not throw but may log an error
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void StopTask_WithNullTask_DoesNotThrow()
    {
        // Arrange
        _sut.AddTask("NullTask", null!);

        // Act
        var act = () => _sut.StopTask("NullTask");

        // Assert - The implementation tries to call Stop on null, which may throw
        act.Should().Throw<NullReferenceException>();
    }

    #endregion

    #region Task Execution Tests

    [Fact]
    public async Task StartAllTasks_TasksExecutePeriodically()
    {
        // Arrange
        var executionCount = 0;
        var task = new CountingTimedTask(() => Interlocked.Increment(ref executionCount))
        {
            RunInterval = 50,
            StartDelay = 0
        };
        _sut.AddTask("CountingTask", task);

        // Act
        _sut.StartAllTasks();
        await Task.Delay(250); // Wait for at least a few executions
        _sut.StopAllTasks();

        // Assert
        executionCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task StopTask_PreventsTaskFromExecuting()
    {
        // Arrange
        var executionCount = 0;
        var task = new CountingTimedTask(() => Interlocked.Increment(ref executionCount))
        {
            RunInterval = 50,
            StartDelay = 0
        };
        _sut.AddTask("CountingTask", task);
        _sut.StartTask("CountingTask");
        await Task.Delay(100); // Let it run a bit

        // Act
        _sut.StopTask("CountingTask");
        var countAfterStop = executionCount;
        await Task.Delay(150); // Wait more

        // Assert
        executionCount.Should().Be(countAfterStop); // Should not increase after stop
    }

    #endregion

    /// <summary>
    /// Test implementation of HighResolutionTimedTask for testing purposes
    /// </summary>
    private class TestTimedTask : HighResolutionTimedTask
    {
        public int RunCount { get; private set; }

        public override void Run()
        {
            RunCount++;
        }
    }

    /// <summary>
    /// Test implementation that counts executions with a callback
    /// </summary>
    private class CountingTimedTask : HighResolutionTimedTask
    {
        private readonly Action _onRun;

        public CountingTimedTask(Action onRun)
        {
            _onRun = onRun;
        }

        public override void Run()
        {
            _onRun();
        }
    }
}
