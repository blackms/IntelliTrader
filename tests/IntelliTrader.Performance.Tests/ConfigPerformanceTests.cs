using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;

namespace IntelliTrader.Performance.Tests;

/// <summary>
/// Performance tests for configuration and health check operations.
/// Thresholds are generous (10x expected) for CI stability.
/// </summary>
public class ConfigPerformanceTests
{
    #region Health Check Performance Tests

    [Fact]
    public void HealthCheck_UpdateAndRetrieve50Checks_CompletesWithin50ms()
    {
        // Arrange
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var coreServiceMock = new Mock<ICoreService>();
        var tradingServiceMock = new Mock<ITradingService>();

        var sut = new TestableHealthCheckService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            coreServiceMock.Object,
            tradingServiceMock.Object);

        // Warmup
        sut.UpdateHealthCheck("warmup", "warmup message");
        sut.GetHealthChecks().ToList();

        // Act - add 50 health checks
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            sut.UpdateHealthCheck($"Check_{i}", $"Status message for check {i}", failed: i % 10 == 0);
        }

        // Retrieve all checks
        for (int iter = 0; iter < 100; iter++)
        {
            var checks = sut.GetHealthChecks().ToList();
        }
        sw.Stop();

        // Assert - 50ms for 50 updates + 100 retrievals
        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            "50 health check updates and 100 retrievals should complete within 50ms");
    }

    [Fact]
    public void HealthCheck_ConcurrentUpdates_CompletesWithin200ms()
    {
        // Arrange
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var coreServiceMock = new Mock<ICoreService>();
        var tradingServiceMock = new Mock<ITradingService>();

        var sut = new TestableHealthCheckService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            coreServiceMock.Object,
            tradingServiceMock.Object);

        // Act - concurrent updates from multiple threads
        var sw = Stopwatch.StartNew();
        Parallel.For(0, 200, i =>
        {
            sut.UpdateHealthCheck($"Check_{i % 50}", $"Update {i}", failed: i % 7 == 0);
            sut.GetHealthChecks().ToList();
        });
        sw.Stop();

        // Assert - 200ms for 200 concurrent update+read cycles
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "200 concurrent health check update+read cycles should complete within 200ms");
    }

    [Fact]
    public void HealthCheck_RapidUpdateRemoveCycle_CompletesWithin100ms()
    {
        // Arrange
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var coreServiceMock = new Mock<ICoreService>();
        var tradingServiceMock = new Mock<ITradingService>();

        var sut = new TestableHealthCheckService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            coreServiceMock.Object,
            tradingServiceMock.Object);

        // Warmup
        sut.UpdateHealthCheck("warmup");
        sut.RemoveHealthCheck("warmup");

        // Act - rapid add/remove cycles simulating timed task health checks
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            string name = $"Task_{i % 20}";
            sut.UpdateHealthCheck(name, $"Running iteration {i}");
            if (i % 5 == 0)
            {
                sut.RemoveHealthCheck(name);
            }
        }
        sw.Stop();

        // Assert - 100ms for 1000 update/remove cycles
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "1000 health check update/remove cycles should complete within 100ms");
    }

    [Fact]
    public void HealthCheck_AggregateFailedChecks_CompletesWithin50ms()
    {
        // Arrange
        var loggingServiceMock = new Mock<ILoggingService>();
        var notificationServiceMock = new Mock<INotificationService>();
        var coreServiceMock = new Mock<ICoreService>();
        var tradingServiceMock = new Mock<ITradingService>();

        var sut = new TestableHealthCheckService(
            loggingServiceMock.Object,
            notificationServiceMock.Object,
            coreServiceMock.Object,
            tradingServiceMock.Object);

        // Setup 50 health checks, some failed
        for (int i = 0; i < 50; i++)
        {
            sut.UpdateHealthCheck($"Check_{i}", $"Message {i}", failed: i % 3 == 0);
        }

        // Warmup
        sut.GetHealthChecks().Count(c => c.Failed);

        // Act - simulate frequent health check aggregation (as done by HealthCheckTimedTask)
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            var checks = sut.GetHealthChecks().ToList();
            var failedCount = checks.Count(c => c.Failed);
            var healthyCount = checks.Count(c => !c.Failed);
        }
        sw.Stop();

        // Assert - 50ms for 1000 aggregations of 50 checks
        sw.ElapsedMilliseconds.Should().BeLessThan(50,
            "1000 health check aggregations (50 checks each) should complete within 50ms");
    }

    #endregion

    #region Configuration Serialization Performance Tests

    [Fact]
    public void JsonSerialization_TradingConfig_CompletesWithin100ms()
    {
        // Arrange - simulate config serialization/deserialization like hot-reload
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Enabled = true,
            Market = "USDT",
            Exchange = "Binance",
            MaxPairs = 10,
            MinCost = 10m,
            ExcludedPairs = Enumerable.Range(0, 50).Select(i => $"EXCLUDED{i}USDT").ToList(),
            VirtualTrading = true,
            VirtualAccountInitialBalance = 10000m,
            TradingCheckInterval = 3.0,
            AccountRefreshInterval = 5.0,
            BuyEnabled = true,
            BuyMaxCost = 100m,
            BuyMultiplier = 1.0,
            SellEnabled = true,
            SellMargin = 1.5,
            DCALevels = Enumerable.Range(0, 5).Select(i => new { Margin = -(i + 1) * 2.0, BuyMultiplier = 1.0 + (i * 0.5) }).ToList()
        });

        // Warmup
        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Act - simulate 100 config reloads
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            // Simulate accessing properties as config reload would
            _ = result.GetProperty("Market").GetString();
            _ = result.GetProperty("MaxPairs").GetInt32();
            _ = result.GetProperty("ExcludedPairs").EnumerateArray().Count();
        }
        sw.Stop();

        // Assert - 100ms for 100 config reload cycles
        sw.ElapsedMilliseconds.Should().BeLessThan(100,
            "100 config deserialization cycles should complete within 100ms");
    }

    [Fact]
    public void JsonSerialization_LargeSignalConfig_CompletesWithin200ms()
    {
        // Arrange - large signal config with many definitions
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            Enabled = true,
            GlobalRatingSignals = Enumerable.Range(0, 20).Select(i => $"Signal_{i}").ToList(),
            Definitions = Enumerable.Range(0, 20).Select(i => new
            {
                Name = $"Signal_{i}",
                Receiver = "TradingViewCryptoSignalReceiver",
                Configuration = new { Period = 60 + i, Exchange = "Binance" }
            }).ToList()
        });

        // Warmup
        System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Act
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
            _ = result.GetProperty("Definitions").EnumerateArray().Count();
            _ = result.GetProperty("GlobalRatingSignals").EnumerateArray().Count();
        }
        sw.Stop();

        // Assert - 200ms for 200 signal config reload cycles
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "200 signal config deserialization cycles should complete within 200ms");
    }

    #endregion
}

/// <summary>
/// Testable implementation of IHealthCheckService that mirrors the real HealthCheckService
/// using ConcurrentDictionary, without requiring the full DI graph.
/// </summary>
internal class TestableHealthCheckService : IHealthCheckService
{
    private readonly ConcurrentDictionary<string, TestableHealthCheck> _healthChecks = new();

    public TestableHealthCheckService(
        ILoggingService loggingService,
        INotificationService notificationService,
        ICoreService coreService,
        ITradingService tradingService)
    {
    }

    public void Start() { }
    public void Stop() { }

    public void UpdateHealthCheck(string name, string? message = null, bool failed = false)
    {
        if (!_healthChecks.TryGetValue(name, out _))
        {
            _healthChecks.TryAdd(name, new TestableHealthCheck
            {
                Name = name,
                Message = message,
                LastUpdated = DateTimeOffset.Now,
                Failed = failed
            });
        }
        else
        {
            _healthChecks[name].Message = message;
            _healthChecks[name].LastUpdated = DateTimeOffset.Now;
            _healthChecks[name].Failed = failed;
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

internal class TestableHealthCheck : IHealthCheck
{
    public string Name { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public bool Failed { get; set; }
}
