using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using System.Collections.Concurrent;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for advanced LoggingService features: correlation scopes, operation timing, and sampled logging.
/// These test the testable wrapper methods that mirror the production LoggingService behavior.
/// </summary>
public class LoggingServiceAdvancedTests
{
    private readonly TestableAdvancedLoggingService _sut;

    public LoggingServiceAdvancedTests()
    {
        _sut = new TestableAdvancedLoggingService(enabled: true);
    }

    #region BeginCorrelationScope Tests

    [Fact]
    public void BeginCorrelationScope_WithoutId_GeneratesNewGuid()
    {
        // Act
        using var scope = _sut.BeginCorrelationScope();

        // Assert
        scope.Should().NotBeNull();
        _sut.LastCorrelationId.Should().NotBeNullOrEmpty();
        // Verify it's a valid GUID format (32 hex chars without dashes)
        _sut.LastCorrelationId.Should().HaveLength(32);
    }

    [Fact]
    public void BeginCorrelationScope_WithId_UsesProvidedId()
    {
        // Act
        using var scope = _sut.BeginCorrelationScope("my-correlation-id");

        // Assert
        _sut.LastCorrelationId.Should().Be("my-correlation-id");
    }

    [Fact]
    public void BeginCorrelationScope_ReturnsDisposable()
    {
        // Act
        var scope = _sut.BeginCorrelationScope();

        // Assert
        scope.Should().BeAssignableTo<IDisposable>();
        scope.Dispose(); // Should not throw
    }

    [Fact]
    public void BeginCorrelationScope_MultipleScopes_HaveUniqueIds()
    {
        // Act
        using var scope1 = _sut.BeginCorrelationScope();
        var id1 = _sut.LastCorrelationId;
        using var scope2 = _sut.BeginCorrelationScope();
        var id2 = _sut.LastCorrelationId;

        // Assert
        id1.Should().NotBe(id2);
    }

    #endregion

    #region TimeOperation Tests

    [Fact]
    public void TimeOperation_ReturnsDisposable()
    {
        // Act
        var timer = _sut.TimeOperation("TestOperation");

        // Assert
        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<IDisposable>();
        timer.Dispose();
    }

    [Fact]
    public void TimeOperation_LogsStartMessage()
    {
        // Act
        using var timer = _sut.TimeOperation("TestOperation");

        // Assert
        _sut.InfoMessages.Should().Contain(m => m.Contains("TestOperation") && m.Contains("started"));
    }

    [Fact]
    public void TimeOperation_OnDispose_LogsCompletionWithDuration()
    {
        // Arrange
        var timer = _sut.TimeOperation("TestOperation");

        // Act
        Thread.Sleep(10); // Ensure measurable duration
        timer.Dispose();

        // Assert
        _sut.InfoMessages.Should().Contain(m => m.Contains("TestOperation") && m.Contains("completed"));
    }

    [Fact]
    public void TimeOperation_DoubleDispose_DoesNotLogTwice()
    {
        // Arrange
        var timer = _sut.TimeOperation("TestOperation");

        // Act
        timer.Dispose();
        var messageCountAfterFirst = _sut.InfoMessages.Count;
        timer.Dispose();

        // Assert
        _sut.InfoMessages.Count.Should().Be(messageCountAfterFirst);
    }

    #endregion

    #region InfoSampled Tests

    [Fact]
    public void InfoSampled_FirstCall_DoesNotLog_BecauseCountIsOneNotMultipleOfSampleRate()
    {
        // Act - with sampleRate=10, first call sets counter to 1, 1 % 10 != 0
        _sut.InfoSampled("ticker-update", 10, "Ticker updated");

        // Assert
        _sut.InfoMessages.Should().BeEmpty();
    }

    [Fact]
    public void InfoSampled_AtExactSampleRate_LogsMessage()
    {
        // Act
        for (int i = 0; i < 10; i++)
        {
            _sut.InfoSampled("ticker-update", 10, "Ticker updated");
        }

        // Assert - should log at the 10th call (count=10, 10%10==0)
        _sut.InfoMessages.Should().ContainSingle();
    }

    [Fact]
    public void InfoSampled_WithSampleRateOf10_LogsEvery10thCall()
    {
        // Act
        for (int i = 0; i < 30; i++)
        {
            _sut.InfoSampled("event-key", 10, "Event");
        }

        // Assert - should log at calls 10, 20, 30
        _sut.InfoMessages.Should().HaveCount(3);
    }

    [Fact]
    public void InfoSampled_DifferentEventKeys_TrackSeparately()
    {
        // Act
        for (int i = 0; i < 5; i++)
        {
            _sut.InfoSampled("key-a", 5, "Event A");
            _sut.InfoSampled("key-b", 5, "Event B");
        }

        // Assert - each key should have logged once (at the 5th call)
        _sut.InfoMessages.Should().HaveCount(2);
    }

    [Fact]
    public void InfoSampled_WithZeroSampleRate_TreatsAsEveryCall()
    {
        // Act
        for (int i = 0; i < 3; i++)
        {
            _sut.InfoSampled("event", 0, "Message");
        }

        // Assert - sampleRate <= 0 is treated as 1, so every call logs
        _sut.InfoMessages.Should().HaveCount(3);
    }

    [Fact]
    public void InfoSampled_WithNegativeSampleRate_TreatsAsEveryCall()
    {
        // Act
        for (int i = 0; i < 3; i++)
        {
            _sut.InfoSampled("event", -5, "Message");
        }

        // Assert
        _sut.InfoMessages.Should().HaveCount(3);
    }

    #endregion

    #region DebugSampled Tests

    [Fact]
    public void DebugSampled_AtSampleRate_LogsMessage()
    {
        // Act - 10 calls with sample rate 10
        for (int i = 0; i < 10; i++)
        {
            _sut.DebugSampled("debug-event", 10, "Debug message");
        }

        // Assert
        _sut.DebugMessages.Should().ContainSingle();
    }

    [Fact]
    public void DebugSampled_WithSampleRate_OnlyLogsAtSampleInterval()
    {
        // Act
        for (int i = 0; i < 20; i++)
        {
            _sut.DebugSampled("debug-key", 5, "Debug event");
        }

        // Assert - should log at 5, 10, 15, 20 = 4 times
        _sut.DebugMessages.Should().HaveCount(4);
    }

    #endregion
}

/// <summary>
/// Testable implementation that mimics advanced logging features
/// </summary>
public class TestableAdvancedLoggingService
{
    private readonly bool _enabled;
    private readonly ConcurrentDictionary<string, long> _samplingCounters = new();

    public string? LastCorrelationId { get; private set; }
    public List<string> InfoMessages { get; } = new();
    public List<string> DebugMessages { get; } = new();

    public TestableAdvancedLoggingService(bool enabled)
    {
        _enabled = enabled;
    }

    public IDisposable BeginCorrelationScope(string? correlationId = null)
    {
        var id = correlationId ?? Guid.NewGuid().ToString("N");
        LastCorrelationId = id;
        return new TestableNoOpDisposable();
    }

    public IDisposable TimeOperation(string operationName)
    {
        return new TestableAdvancedOperationTimer(this, operationName);
    }

    public void InfoSampled(string eventKey, int sampleRate, string message, params object[] propertyValues)
    {
        if (sampleRate <= 0) sampleRate = 1;
        var count = _samplingCounters.AddOrUpdate(eventKey, 1, (_, prev) => prev + 1);
        if (count % sampleRate == 0)
        {
            InfoMessages.Add(message);
        }
    }

    public void DebugSampled(string eventKey, int sampleRate, string message, params object[] propertyValues)
    {
        if (sampleRate <= 0) sampleRate = 1;
        var count = _samplingCounters.AddOrUpdate(eventKey, 1, (_, prev) => prev + 1);
        if (count % sampleRate == 0)
        {
            DebugMessages.Add(message);
        }
    }

    internal void LogInfo(string message)
    {
        InfoMessages.Add(message);
    }
}

/// <summary>
/// Operation timer for advanced logging tests
/// </summary>
public class TestableAdvancedOperationTimer : IDisposable
{
    private readonly TestableAdvancedLoggingService _service;
    private readonly string _operationName;
    private readonly System.Diagnostics.Stopwatch _stopwatch;
    private bool _disposed;

    public TestableAdvancedOperationTimer(TestableAdvancedLoggingService service, string operationName)
    {
        _service = service;
        _operationName = operationName;
        _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _service.LogInfo($"Operation {operationName} started");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        _service.LogInfo($"Operation {_operationName} completed in {_stopwatch.ElapsedMilliseconds}ms");
    }
}
