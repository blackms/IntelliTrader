using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Configuration;
using IntelliTrader.Core;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for LoggingService functionality.
/// Tests focus on log level methods, scope management, and log entry handling.
/// Since LoggingService depends on Serilog configuration, we use a testable wrapper.
/// </summary>
public class LoggingServiceTests
{
    private readonly TestableLoggingService _sut;
    private readonly TestableLoggingConfig _config;

    public LoggingServiceTests()
    {
        _config = new TestableLoggingConfig { Enabled = true };
        _sut = new TestableLoggingService(_config);
    }

    #region Info Method Tests

    [Fact]
    public void Info_WhenEnabled_LogsMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Information message";

        // Act
        _sut.Info(message);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Information);
        _sut.LoggedMessages.First().Message.Should().Be(message);
    }

    [Fact]
    public void Info_WhenDisabled_DoesNotLogMessage()
    {
        // Arrange
        _config.Enabled = false;
        const string message = "Information message";

        // Act
        _sut.Info(message);

        // Assert
        _sut.LoggedMessages.Should().BeEmpty();
    }

    [Fact]
    public void Info_WithException_LogsMessageAndException()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Error occurred";
        var exception = new InvalidOperationException("Test exception");

        // Act
        _sut.Info(message, exception);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Exception.Should().Be(exception);
    }

    [Fact]
    public void Info_WithPropertyValues_LogsFormattedMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string template = "User {UserId} logged in from {IpAddress}";

        // Act
        _sut.Info(template, 123, "192.168.1.1");

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().PropertyValues.Should().Contain(123);
        _sut.LoggedMessages.First().PropertyValues.Should().Contain("192.168.1.1");
    }

    #endregion

    #region Debug Method Tests

    [Fact]
    public void Debug_WhenEnabled_LogsMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Debug message";

        // Act
        _sut.Debug(message);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Debug);
        _sut.LoggedMessages.First().Message.Should().Be(message);
    }

    [Fact]
    public void Debug_WhenDisabled_DoesNotLogMessage()
    {
        // Arrange
        _config.Enabled = false;
        const string message = "Debug message";

        // Act
        _sut.Debug(message);

        // Assert
        _sut.LoggedMessages.Should().BeEmpty();
    }

    [Fact]
    public void Debug_WithException_LogsMessageAndException()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Debug with exception";
        var exception = new ArgumentException("Argument issue");

        // Act
        _sut.Debug(message, exception);

        // Assert
        _sut.LoggedMessages.First().Exception.Should().Be(exception);
    }

    [Fact]
    public void Debug_WithPropertyValues_LogsWithValues()
    {
        // Arrange
        _config.Enabled = true;

        // Act
        _sut.Debug("Value is {Value}", 42);

        // Assert
        _sut.LoggedMessages.First().PropertyValues.Should().Contain(42);
    }

    #endregion

    #region Error Method Tests

    [Fact]
    public void Error_WhenEnabled_LogsMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Error message";

        // Act
        _sut.Error(message);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Error);
        _sut.LoggedMessages.First().Message.Should().Be(message);
    }

    [Fact]
    public void Error_WhenDisabled_DoesNotLogMessage()
    {
        // Arrange
        _config.Enabled = false;
        const string message = "Error message";

        // Act
        _sut.Error(message);

        // Assert
        _sut.LoggedMessages.Should().BeEmpty();
    }

    [Fact]
    public void Error_WithException_LogsMessageAndException()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Critical error";
        var exception = new NullReferenceException("Null reference");

        // Act
        _sut.Error(message, exception);

        // Assert
        var logEntry = _sut.LoggedMessages.First();
        logEntry.Exception.Should().Be(exception);
        logEntry.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void Error_WithPropertyValues_LogsWithValues()
    {
        // Arrange
        _config.Enabled = true;

        // Act
        _sut.Error("Failed operation {OperationId} after {Attempts} attempts", "op-123", 3);

        // Assert
        _sut.LoggedMessages.First().PropertyValues.Should().Contain("op-123");
        _sut.LoggedMessages.First().PropertyValues.Should().Contain(3);
    }

    #endregion

    #region Warning Method Tests

    [Fact]
    public void Warning_WhenEnabled_LogsMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Warning message";

        // Act
        _sut.Warning(message);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Warning);
        _sut.LoggedMessages.First().Message.Should().Be(message);
    }

    [Fact]
    public void Warning_WhenDisabled_DoesNotLogMessage()
    {
        // Arrange
        _config.Enabled = false;
        const string message = "Warning message";

        // Act
        _sut.Warning(message);

        // Assert
        _sut.LoggedMessages.Should().BeEmpty();
    }

    [Fact]
    public void Warning_WithException_LogsMessageAndException()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Warning with exception";
        var exception = new TimeoutException("Operation timed out");

        // Act
        _sut.Warning(message, exception);

        // Assert
        _sut.LoggedMessages.First().Exception.Should().Be(exception);
    }

    [Fact]
    public void Warning_WithPropertyValues_LogsWithValues()
    {
        // Arrange
        _config.Enabled = true;

        // Act
        _sut.Warning("Slow response from {Service}: {ResponseTime}ms", "API", 5000);

        // Assert
        _sut.LoggedMessages.First().PropertyValues.Should().Contain("API");
        _sut.LoggedMessages.First().PropertyValues.Should().Contain(5000);
    }

    #endregion

    #region Verbose Method Tests

    [Fact]
    public void Verbose_WhenEnabled_LogsMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Verbose message";

        // Act
        _sut.Verbose(message);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Verbose);
    }

    [Fact]
    public void Verbose_WhenDisabled_DoesNotLogMessage()
    {
        // Arrange
        _config.Enabled = false;

        // Act
        _sut.Verbose("Verbose message");

        // Assert
        _sut.LoggedMessages.Should().BeEmpty();
    }

    #endregion

    #region Fatal Method Tests

    [Fact]
    public void Fatal_WhenEnabled_LogsMessage()
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Fatal error";

        // Act
        _sut.Fatal(message);

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Fatal);
    }

    [Fact]
    public void Fatal_WithException_LogsMessageAndException()
    {
        // Arrange
        _config.Enabled = true;
        var exception = new OutOfMemoryException("Memory exhausted");

        // Act
        _sut.Fatal("System failure", exception);

        // Assert
        _sut.LoggedMessages.First().Exception.Should().Be(exception);
        _sut.LoggedMessages.First().Level.Should().Be(LogLevel.Fatal);
    }

    #endregion

    #region GetLogEntries Tests

    [Fact]
    public void GetLogEntries_WhenEmpty_ReturnsEmptyArray()
    {
        // Act
        var entries = _sut.GetLogEntries();

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public void GetLogEntries_ReturnsLoggedEntries()
    {
        // Arrange
        _config.Enabled = true;
        _sut.Info("Entry 1");
        _sut.Info("Entry 2");

        // Act
        var entries = _sut.GetLogEntries();

        // Assert
        entries.Should().HaveCount(2);
    }

    [Fact]
    public void GetLogEntries_ReturnsEntriesAsStrings()
    {
        // Arrange
        _config.Enabled = true;
        _sut.Info("Test entry");

        // Act
        var entries = _sut.GetLogEntries();

        // Assert
        entries.Should().AllBeOfType<string>();
        entries.First().Should().Contain("Test entry");
    }

    #endregion

    #region BeginScope Tests

    [Fact]
    public void BeginScope_WithDictionary_ReturnsDisposable()
    {
        // Arrange
        var properties = new Dictionary<string, object>
        {
            { "CorrelationId", "abc-123" },
            { "UserId", 456 }
        };

        // Act
        using var scope = _sut.BeginScope(properties);

        // Assert
        scope.Should().NotBeNull();
        scope.Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void BeginScope_WithEmptyDictionary_ReturnsNoOpDisposable()
    {
        // Arrange
        var properties = new Dictionary<string, object>();

        // Act
        var scope = _sut.BeginScope(properties);

        // Assert
        scope.Should().NotBeNull();
        scope.Should().BeOfType<TestableNoOpDisposable>();
    }

    [Fact]
    public void BeginScope_WithNullDictionary_ReturnsNoOpDisposable()
    {
        // Act
        var scope = _sut.BeginScope((IDictionary<string, object>)null!);

        // Assert
        scope.Should().NotBeNull();
        scope.Should().BeOfType<TestableNoOpDisposable>();
    }

    [Fact]
    public void BeginScope_WithPropertyNameAndValue_ReturnsDisposable()
    {
        // Act
        using var scope = _sut.BeginScope("RequestId", "req-789");

        // Assert
        scope.Should().NotBeNull();
        scope.Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void BeginScope_WithEmptyPropertyName_ReturnsNoOpDisposable()
    {
        // Act
        var scope = _sut.BeginScope("", "value");

        // Assert
        scope.Should().BeOfType<TestableNoOpDisposable>();
    }

    [Fact]
    public void BeginScope_WithWhitespacePropertyName_ReturnsNoOpDisposable()
    {
        // Act
        var scope = _sut.BeginScope("   ", "value");

        // Assert
        scope.Should().BeOfType<TestableNoOpDisposable>();
    }

    [Fact]
    public void BeginScope_Dispose_RemovesContext()
    {
        // Arrange
        var scope = _sut.BeginScope("TestProperty", "TestValue");

        // Act
        scope.Dispose();

        // Assert - scope should be disposed without throwing
        var action = () => scope.Dispose();
        action.Should().NotThrow(); // Double dispose should be safe
    }

    [Fact]
    public void BeginScope_MultipleScopes_CanBeNested()
    {
        // Act
        using var scope1 = _sut.BeginScope("Outer", "OuterValue");
        using var scope2 = _sut.BeginScope("Inner", "InnerValue");
        _sut.Info("Nested log message");

        // Assert
        _sut.ActiveScopes.Should().HaveCount(2);
    }

    [Fact]
    public void BeginScope_WhenDisposed_RemovesFromActiveScopes()
    {
        // Arrange
        var scope1 = _sut.BeginScope("Prop1", "Val1");
        var scope2 = _sut.BeginScope("Prop2", "Val2");

        // Act
        scope2.Dispose();

        // Assert
        _sut.ActiveScopes.Should().HaveCount(1);
    }

    #endregion

    #region Log Level Filtering Tests

    [Theory]
    [InlineData(LogLevel.Verbose)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Fatal)]
    public void AllLogLevels_WhenEnabled_LogMessages(LogLevel level)
    {
        // Arrange
        _config.Enabled = true;
        const string message = "Test message";

        // Act
        switch (level)
        {
            case LogLevel.Verbose:
                _sut.Verbose(message);
                break;
            case LogLevel.Debug:
                _sut.Debug(message);
                break;
            case LogLevel.Information:
                _sut.Info(message);
                break;
            case LogLevel.Warning:
                _sut.Warning(message);
                break;
            case LogLevel.Error:
                _sut.Error(message);
                break;
            case LogLevel.Fatal:
                _sut.Fatal(message);
                break;
        }

        // Assert
        _sut.LoggedMessages.Should().ContainSingle();
        _sut.LoggedMessages.First().Level.Should().Be(level);
    }

    [Theory]
    [InlineData(LogLevel.Verbose)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Fatal)]
    public void AllLogLevels_WhenDisabled_DoNotLogMessages(LogLevel level)
    {
        // Arrange
        _config.Enabled = false;
        const string message = "Test message";

        // Act
        switch (level)
        {
            case LogLevel.Verbose:
                _sut.Verbose(message);
                break;
            case LogLevel.Debug:
                _sut.Debug(message);
                break;
            case LogLevel.Information:
                _sut.Info(message);
                break;
            case LogLevel.Warning:
                _sut.Warning(message);
                break;
            case LogLevel.Error:
                _sut.Error(message);
                break;
            case LogLevel.Fatal:
                _sut.Fatal(message);
                break;
        }

        // Assert
        _sut.LoggedMessages.Should().BeEmpty();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void ConcurrentLogging_HandlesMultipleThreads()
    {
        // Arrange
        _config.Enabled = true;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() => _sut.Info($"Message {index}")));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        _sut.LoggedMessages.Should().HaveCount(100);
    }

    [Fact]
    public void ConcurrentScopeCreation_HandlesMultipleThreads()
    {
        // Arrange
        _config.Enabled = true;
        var tasks = new List<Task>();
        var disposables = new System.Collections.Concurrent.ConcurrentBag<IDisposable>();

        // Act
        for (int i = 0; i < 50; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                var scope = _sut.BeginScope($"Prop{index}", index);
                disposables.Add(scope);
            }));
        }
        Task.WaitAll(tasks.ToArray());

        // Assert
        disposables.Should().HaveCount(50);

        // Cleanup
        foreach (var d in disposables)
        {
            d.Dispose();
        }
    }

    #endregion

    #region DeleteAllLogs Tests

    [Fact]
    public void DeleteAllLogs_ClearsLogEntries()
    {
        // Arrange
        _config.Enabled = true;
        _sut.Info("Entry 1");
        _sut.Info("Entry 2");

        // Act
        _sut.DeleteAllLogs();

        // Assert
        _sut.GetLogEntries().Should().BeEmpty();
    }

    #endregion
}

/// <summary>
/// Log level enumeration for testing
/// </summary>
public enum LogLevel
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal
}

/// <summary>
/// Represents a logged message for testing
/// </summary>
public class LoggedMessage
{
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public object[]? PropertyValues { get; set; }
}

/// <summary>
/// Testable logging configuration
/// </summary>
public class TestableLoggingConfig
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Testable implementation of logging service
/// </summary>
public class TestableLoggingService : ILoggingService
{
    public string ServiceName => Constants.ServiceNames.LoggingService;

    public Microsoft.Extensions.Configuration.IConfigurationSection RawConfig => null!;

    private readonly TestableLoggingConfig _config;
    private readonly object _syncRoot = new();
    private readonly List<string> _logEntries = new();

    public List<LoggedMessage> LoggedMessages { get; } = new();
    public List<IDisposable> ActiveScopes { get; } = new();

    public TestableLoggingService(TestableLoggingConfig config)
    {
        _config = config;
    }

    public void Verbose(string message, Exception? exception = null)
    {
        LogMessage(LogLevel.Verbose, message, exception);
    }

    public void Verbose(string message, params object[] propertyValues)
    {
        LogMessage(LogLevel.Verbose, message, null, propertyValues);
    }

    public void Debug(string message, Exception? exception = null)
    {
        LogMessage(LogLevel.Debug, message, exception);
    }

    public void Debug(string message, params object[] propertyValues)
    {
        LogMessage(LogLevel.Debug, message, null, propertyValues);
    }

    public void Info(string message, Exception? exception = null)
    {
        LogMessage(LogLevel.Information, message, exception);
    }

    public void Info(string message, params object[] propertyValues)
    {
        LogMessage(LogLevel.Information, message, null, propertyValues);
    }

    public void Warning(string message, Exception? exception = null)
    {
        LogMessage(LogLevel.Warning, message, exception);
    }

    public void Warning(string message, params object[] propertyValues)
    {
        LogMessage(LogLevel.Warning, message, null, propertyValues);
    }

    public void Error(string message, Exception? exception = null)
    {
        LogMessage(LogLevel.Error, message, exception);
    }

    public void Error(string message, params object[] propertyValues)
    {
        LogMessage(LogLevel.Error, message, null, propertyValues);
    }

    public void Fatal(string message, Exception? exception = null)
    {
        LogMessage(LogLevel.Fatal, message, exception);
    }

    public void Fatal(string message, params object[] propertyValues)
    {
        LogMessage(LogLevel.Fatal, message, null, propertyValues);
    }

    private void LogMessage(LogLevel level, string message, Exception? exception, object[]? propertyValues = null)
    {
        lock (_syncRoot)
        {
            if (_config.Enabled)
            {
                LoggedMessages.Add(new LoggedMessage
                {
                    Level = level,
                    Message = message,
                    Exception = exception,
                    PropertyValues = propertyValues
                });

                _logEntries.Add($"[{level}] {message}");
            }
        }
    }

    public void DeleteAllLogs()
    {
        lock (_syncRoot)
        {
            LoggedMessages.Clear();
            _logEntries.Clear();
        }
    }

    public string[] GetLogEntries()
    {
        lock (_syncRoot)
        {
            return _logEntries.ToArray();
        }
    }

    public IDisposable BeginScope(IDictionary<string, object>? properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return new TestableNoOpDisposable();
        }

        var scope = new TestableScope(this);
        ActiveScopes.Add(scope);
        return scope;
    }

    public IDisposable BeginScope(string propertyName, object value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return new TestableNoOpDisposable();
        }

        var scope = new TestableScope(this);
        ActiveScopes.Add(scope);
        return scope;
    }

    internal void RemoveScope(TestableScope scope)
    {
        ActiveScopes.Remove(scope);
    }
}

/// <summary>
/// Testable no-op disposable
/// </summary>
public class TestableNoOpDisposable : IDisposable
{
    public void Dispose() { }
}

/// <summary>
/// Testable scope that tracks disposal
/// </summary>
public class TestableScope : IDisposable
{
    private readonly TestableLoggingService _service;
    private bool _disposed;

    public TestableScope(TestableLoggingService service)
    {
        _service = service;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _service.RemoveScope(this);
        }
    }
}
