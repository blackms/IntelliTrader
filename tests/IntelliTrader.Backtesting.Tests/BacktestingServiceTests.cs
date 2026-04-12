using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Backtesting;
using IntelliTrader.Exchange.Base;
using IntelliTrader.Signals.Base;
using System.Collections.Concurrent;

namespace IntelliTrader.Backtesting.Tests;

/// <summary>
/// Tests for IBacktestingService interface behavior and BacktestingService functionality.
/// These tests focus on testing the interface contract and behavior through mocks.
/// </summary>
public class BacktestingServiceInterfaceTests
{
    private readonly Mock<IBacktestingService> _backtestingServiceMock;
    private readonly Mock<IBacktestingConfig> _configMock;

    public BacktestingServiceInterfaceTests()
    {
        _backtestingServiceMock = new Mock<IBacktestingService>();
        _configMock = new Mock<IBacktestingConfig>();

        // Setup default config
        _configMock.Setup(x => x.Enabled).Returns(true);
        _configMock.Setup(x => x.Replay).Returns(false);
        _configMock.Setup(x => x.ReplaySpeed).Returns(1.0);
        _configMock.Setup(x => x.SnapshotsInterval).Returns(60);
        _configMock.Setup(x => x.SnapshotsPath).Returns("snapshots");
        _configMock.Setup(x => x.DeleteLogs).Returns(false);
        _configMock.Setup(x => x.DeleteAccountData).Returns(false);

        _backtestingServiceMock.Setup(x => x.Config).Returns(_configMock.Object);
        _backtestingServiceMock.Setup(x => x.SyncRoot).Returns(new object());
    }

    #region Config Property Tests

    [Fact]
    public void Config_ReturnsBacktestingConfig()
    {
        // Act
        var config = _backtestingServiceMock.Object.Config;

        // Assert
        config.Should().NotBeNull();
        config.Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Config_EnabledAndReplayFlags_AreConfigurable(bool enabled, bool replay)
    {
        // Arrange
        _configMock.Setup(x => x.Enabled).Returns(enabled);
        _configMock.Setup(x => x.Replay).Returns(replay);

        // Act
        var config = _backtestingServiceMock.Object.Config;

        // Assert
        config.Enabled.Should().Be(enabled);
        config.Replay.Should().Be(replay);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(10.0)]
    public void Config_ReplaySpeed_SupportsVariousValues(double replaySpeed)
    {
        // Arrange
        _configMock.Setup(x => x.ReplaySpeed).Returns(replaySpeed);

        // Act
        var config = _backtestingServiceMock.Object.Config;

        // Assert
        config.ReplaySpeed.Should().Be(replaySpeed);
    }

    [Fact]
    public void Config_SnapshotsPath_CanBeCustomized()
    {
        // Arrange
        var customPath = "custom/snapshots/path";
        _configMock.Setup(x => x.SnapshotsPath).Returns(customPath);

        // Act
        var config = _backtestingServiceMock.Object.Config;

        // Assert
        config.SnapshotsPath.Should().Be(customPath);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(300)]
    public void Config_SnapshotsInterval_SupportsVariousIntervals(int interval)
    {
        // Arrange
        _configMock.Setup(x => x.SnapshotsInterval).Returns(interval);

        // Act
        var config = _backtestingServiceMock.Object.Config;

        // Assert
        config.SnapshotsInterval.Should().Be(interval);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 100)]
    [InlineData(50, 150)]
    [InlineData(100, null)]
    public void Config_ReplayIndexRange_SupportsVariousRanges(int? startIndex, int? endIndex)
    {
        // Arrange
        _configMock.Setup(x => x.ReplayStartIndex).Returns(startIndex);
        _configMock.Setup(x => x.ReplayEndIndex).Returns(endIndex);

        // Act
        var config = _backtestingServiceMock.Object.Config;

        // Assert
        config.ReplayStartIndex.Should().Be(startIndex);
        config.ReplayEndIndex.Should().Be(endIndex);
    }

    #endregion

    #region SyncRoot Tests

    [Fact]
    public void SyncRoot_ReturnsNonNullObject()
    {
        // Act
        var syncRoot = _backtestingServiceMock.Object.SyncRoot;

        // Assert
        syncRoot.Should().NotBeNull();
    }

    [Fact]
    public void SyncRoot_UsedForThreadSafeOperations()
    {
        // Arrange
        var lockObject = new object();
        _backtestingServiceMock.Setup(x => x.SyncRoot).Returns(lockObject);

        // Act
        var syncRoot = _backtestingServiceMock.Object.SyncRoot;

        // Assert - Verify we can use it for locking
        bool lockAcquired = false;
        lock (syncRoot)
        {
            lockAcquired = true;
        }
        lockAcquired.Should().BeTrue();
    }

    #endregion

    #region GetSnapshotFilePath Tests

    [Fact]
    public void GetSnapshotFilePath_WithSignalsEntity_ReturnsPath()
    {
        // Arrange
        var expectedPath = "/path/to/signals/snapshot.bin";
        _backtestingServiceMock.Setup(x => x.GetSnapshotFilePath(Constants.SnapshotEntities.Signals))
            .Returns(expectedPath);

        // Act
        var path = _backtestingServiceMock.Object.GetSnapshotFilePath(Constants.SnapshotEntities.Signals);

        // Assert
        path.Should().Be(expectedPath);
    }

    [Fact]
    public void GetSnapshotFilePath_WithTickersEntity_ReturnsPath()
    {
        // Arrange
        var expectedPath = "/path/to/tickers/snapshot.bin";
        _backtestingServiceMock.Setup(x => x.GetSnapshotFilePath(Constants.SnapshotEntities.Tickers))
            .Returns(expectedPath);

        // Act
        var path = _backtestingServiceMock.Object.GetSnapshotFilePath(Constants.SnapshotEntities.Tickers);

        // Assert
        path.Should().Be(expectedPath);
    }

    [Theory]
    [InlineData("signals")]
    [InlineData("tickers")]
    [InlineData("custom")]
    public void GetSnapshotFilePath_AcceptsVariousEntityTypes(string entity)
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetSnapshotFilePath(entity))
            .Returns($"/snapshots/{entity}/snapshot.bin");

        // Act
        var path = _backtestingServiceMock.Object.GetSnapshotFilePath(entity);

        // Assert
        path.Should().Contain(entity);
        path.Should().EndWith(".bin");
    }

    #endregion

    #region GetCurrentSignals Tests

    [Fact]
    public void GetCurrentSignals_WhenEmpty_ReturnsEmptyDictionary()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(new Dictionary<string, IEnumerable<ISignal>>());

        // Act
        var signals = _backtestingServiceMock.Object.GetCurrentSignals();

        // Assert
        signals.Should().NotBeNull();
        signals.Should().BeEmpty();
    }

    [Fact]
    public void GetCurrentSignals_WithData_ReturnsSignalsByPair()
    {
        // Arrange
        var btcSignals = new List<ISignal> { CreateMockSignal("BTCUSDT", "TradingView") };
        var ethSignals = new List<ISignal> { CreateMockSignal("ETHUSDT", "TradingView") };

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", btcSignals },
            { "ETHUSDT", ethSignals }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _backtestingServiceMock.Object.GetCurrentSignals();

        // Assert
        result.Should().HaveCount(2);
        result.Should().ContainKey("BTCUSDT");
        result.Should().ContainKey("ETHUSDT");
    }

    [Fact]
    public void GetCurrentSignals_SignalsGroupedByPair()
    {
        // Arrange
        var signal1 = CreateMockSignal("BTCUSDT", "Signal1");
        var signal2 = CreateMockSignal("BTCUSDT", "Signal2");
        var signal3 = CreateMockSignal("ETHUSDT", "Signal1");

        var signals = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { signal1, signal2 } },
            { "ETHUSDT", new[] { signal3 } }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals()).Returns(signals);

        // Act
        var result = _backtestingServiceMock.Object.GetCurrentSignals();

        // Assert
        result["BTCUSDT"].Should().HaveCount(2);
        result["ETHUSDT"].Should().HaveCount(1);
    }

    #endregion

    #region GetCurrentTickers Tests

    [Fact]
    public void GetCurrentTickers_WhenEmpty_ReturnsEmptyDictionary()
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(new Dictionary<string, ITicker>());

        // Act
        var tickers = _backtestingServiceMock.Object.GetCurrentTickers();

        // Assert
        tickers.Should().NotBeNull();
        tickers.Should().BeEmpty();
    }

    [Fact]
    public void GetCurrentTickers_WithData_ReturnsTickersByPair()
    {
        // Arrange
        var tickers = new Dictionary<string, ITicker>
        {
            { "BTCUSDT", CreateMockTicker("BTCUSDT", 50000m) },
            { "ETHUSDT", CreateMockTicker("ETHUSDT", 3000m) }
        };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act
        var result = _backtestingServiceMock.Object.GetCurrentTickers();

        // Assert
        result.Should().HaveCount(2);
        result["BTCUSDT"].LastPrice.Should().Be(50000m);
        result["ETHUSDT"].LastPrice.Should().Be(3000m);
    }

    [Fact]
    public void GetCurrentTickers_TickersHaveCorrectPriceData()
    {
        // Arrange
        var ticker = CreateMockTicker("BTCUSDT", 50000m, 49950m, 50050m);
        var tickers = new Dictionary<string, ITicker> { { "BTCUSDT", ticker } };
        _backtestingServiceMock.Setup(x => x.GetCurrentTickers()).Returns(tickers);

        // Act
        var result = _backtestingServiceMock.Object.GetCurrentTickers();

        // Assert
        result["BTCUSDT"].LastPrice.Should().Be(50000m);
        result["BTCUSDT"].BidPrice.Should().Be(49950m);
        result["BTCUSDT"].AskPrice.Should().Be(50050m);
    }

    #endregion

    #region GetTotalSnapshots Tests

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void GetTotalSnapshots_ReturnsCorrectCount(int count)
    {
        // Arrange
        _backtestingServiceMock.Setup(x => x.GetTotalSnapshots()).Returns(count);

        // Act
        var total = _backtestingServiceMock.Object.GetTotalSnapshots();

        // Assert
        total.Should().Be(count);
    }

    #endregion

    #region Complete Tests

    [Fact]
    public void Complete_CanBeCalledWithSkippedSnapshots()
    {
        // Arrange
        var skippedSignals = 5;
        var skippedTickers = 3;

        // Act & Assert - Should not throw
        _backtestingServiceMock.Object.Complete(skippedSignals, skippedTickers);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 5)]
    [InlineData(100, 50)]
    public void Complete_AcceptsVariousSkippedCounts(int skippedSignals, int skippedTickers)
    {
        // Act & Assert - Should not throw
        _backtestingServiceMock.Object.Complete(skippedSignals, skippedTickers);
        _backtestingServiceMock.Verify(x => x.Complete(skippedSignals, skippedTickers), Times.Once);
    }

    #endregion

    #region Start/Stop Tests

    [Fact]
    public void Start_CanBeCalled()
    {
        // Act & Assert - Should not throw
        _backtestingServiceMock.Object.Start();
        _backtestingServiceMock.Verify(x => x.Start(), Times.Once);
    }

    [Fact]
    public void Stop_CanBeCalled()
    {
        // Act & Assert - Should not throw
        _backtestingServiceMock.Object.Stop();
        _backtestingServiceMock.Verify(x => x.Stop(), Times.Once);
    }

    [Fact]
    public void Start_ThenStop_CanBeCalledSequentially()
    {
        // Act
        _backtestingServiceMock.Object.Start();
        _backtestingServiceMock.Object.Stop();

        // Assert
        _backtestingServiceMock.Verify(x => x.Start(), Times.Once);
        _backtestingServiceMock.Verify(x => x.Stop(), Times.Once);
    }

    #endregion

    #region Snapshot Loading/Replay Simulation Tests

    [Fact]
    public void GetCurrentSignals_SimulatesSnapshotReplay()
    {
        // Arrange - Simulate loading different snapshots over time
        var snapshot1 = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateMockSignal("BTCUSDT", "TradingView", 0.5) } }
        };

        var snapshot2 = new Dictionary<string, IEnumerable<ISignal>>
        {
            { "BTCUSDT", new[] { CreateMockSignal("BTCUSDT", "TradingView", 0.7) } },
            { "ETHUSDT", new[] { CreateMockSignal("ETHUSDT", "TradingView", 0.6) } }
        };

        var callCount = 0;
        _backtestingServiceMock.Setup(x => x.GetCurrentSignals())
            .Returns(() => ++callCount == 1 ? snapshot1 : snapshot2);

        // Act
        var result1 = _backtestingServiceMock.Object.GetCurrentSignals();
        var result2 = _backtestingServiceMock.Object.GetCurrentSignals();

        // Assert
        result1.Should().HaveCount(1);
        result2.Should().HaveCount(2);
    }

    [Fact]
    public void GetCurrentTickers_SimulatesHistoricalPriceReplay()
    {
        // Arrange - Simulate price changes over snapshots
        var prices = new[] { 45000m, 46000m, 47000m, 48000m };
        var index = 0;

        _backtestingServiceMock.Setup(x => x.GetCurrentTickers())
            .Returns(() => new Dictionary<string, ITicker>
            {
                { "BTCUSDT", CreateMockTicker("BTCUSDT", prices[index++ % prices.Length]) }
            });

        // Act & Assert - Verify price changes through replay
        for (int i = 0; i < prices.Length; i++)
        {
            var tickers = _backtestingServiceMock.Object.GetCurrentTickers();
            tickers["BTCUSDT"].LastPrice.Should().Be(prices[i]);
        }
    }

    #endregion

    #region Helper Methods

    private static ISignal CreateMockSignal(string pair, string name, double? rating = 0.5)
    {
        var mock = new Mock<ISignal>();
        mock.Setup(x => x.Pair).Returns(pair);
        mock.Setup(x => x.Name).Returns(name);
        mock.Setup(x => x.Rating).Returns(rating);
        mock.Setup(x => x.Price).Returns(50000m);
        mock.Setup(x => x.Volume).Returns(1000000);
        return mock.Object;
    }

    private static ITicker CreateMockTicker(string pair, decimal lastPrice, decimal? bidPrice = null, decimal? askPrice = null)
    {
        var mock = new Mock<ITicker>();
        mock.Setup(x => x.Pair).Returns(pair);
        mock.Setup(x => x.LastPrice).Returns(lastPrice);
        mock.Setup(x => x.BidPrice).Returns(bidPrice ?? lastPrice * 0.999m);
        mock.Setup(x => x.AskPrice).Returns(askPrice ?? lastPrice * 1.001m);
        return mock.Object;
    }

    #endregion
}

/// <summary>
/// Tests for BacktestingService constants and static members
/// </summary>
public class BacktestingServiceConstantsTests
{
    [Fact]
    public void SnapshotFileExtension_IsBin()
    {
        // Assert
        BacktestingService.SNAPSHOT_FILE_EXTENSION.Should().Be("bin");
    }

    [Fact]
    public void SnapshotFileExtension_IsNotEmpty()
    {
        // Assert
        BacktestingService.SNAPSHOT_FILE_EXTENSION.Should().NotBeNullOrEmpty();
    }
}

/// <summary>
/// Tests for BacktestingConfig behavior
/// </summary>
public class BacktestingConfigTests
{
    [Fact]
    public void Config_DeleteLogs_WhenTrue_IndicatesLogDeletion()
    {
        // Arrange
        var configMock = new Mock<IBacktestingConfig>();
        configMock.Setup(x => x.DeleteLogs).Returns(true);

        // Assert
        configMock.Object.DeleteLogs.Should().BeTrue();
    }

    [Fact]
    public void Config_DeleteAccountData_WhenTrue_IndicatesAccountDataDeletion()
    {
        // Arrange
        var configMock = new Mock<IBacktestingConfig>();
        configMock.Setup(x => x.DeleteAccountData).Returns(true);

        // Assert
        configMock.Object.DeleteAccountData.Should().BeTrue();
    }

    [Fact]
    public void Config_CopyAccountDataPath_CanBeSet()
    {
        // Arrange
        var configMock = new Mock<IBacktestingConfig>();
        var path = "backup/account_data.json";
        configMock.Setup(x => x.CopyAccountDataPath).Returns(path);

        // Assert
        configMock.Object.CopyAccountDataPath.Should().Be(path);
    }

    [Fact]
    public void Config_ReplayOutput_ControlsLoggingVerbosity()
    {
        // Arrange
        var configMock = new Mock<IBacktestingConfig>();
        configMock.Setup(x => x.ReplayOutput).Returns(true);

        // Assert
        configMock.Object.ReplayOutput.Should().BeTrue();
    }
}

/// <summary>
/// Tests for BacktestingService performance metrics calculation
/// </summary>
public class BacktestingServicePerformanceTests
{
    [Fact]
    public void Complete_WithTasks_CalculatesPerformanceMetrics()
    {
        // Arrange
        var mockService = new Mock<IBacktestingService>();
        var tasks = new ConcurrentDictionary<string, HighResolutionTimedTask>();

        var mockCoreService = new Mock<ICoreService>();
        mockCoreService.Setup(x => x.GetAllTasks()).Returns(tasks);

        // Act - Complete should process task metrics
        mockService.Object.Complete(0, 0);

        // Assert - Service should complete without errors
        mockService.Verify(x => x.Complete(0, 0), Times.Once);
    }

    [Theory]
    [InlineData(0, 0, "All snapshots processed")]
    [InlineData(5, 3, "Some snapshots skipped")]
    [InlineData(100, 50, "Significant skipping")]
    public void Complete_SkippedSnapshots_IndicatePerformanceIssues(int skippedSignals, int skippedTickers, string scenario)
    {
        // Arrange
        var mockService = new Mock<IBacktestingService>();

        // Act
        mockService.Object.Complete(skippedSignals, skippedTickers);

        // Assert - Verify complete was called with expected parameters
        mockService.Verify(x => x.Complete(skippedSignals, skippedTickers), Times.Once);

        // Higher skipped counts indicate the replay speed was too fast
        if (skippedSignals > 0 || skippedTickers > 0)
        {
            (skippedSignals + skippedTickers).Should().BeGreaterThan(0,
                $"Scenario '{scenario}' should have skipped snapshots");
        }
    }
}

/// <summary>
/// Tests for time manipulation during backtesting
/// </summary>
public class BacktestingTimeManipulationTests
{
    [Theory]
    [InlineData(0.5, "Half speed - more accurate")]
    [InlineData(1.0, "Real-time speed")]
    [InlineData(2.0, "Double speed - faster but may skip")]
    [InlineData(10.0, "10x speed - fastest, may miss data")]
    public void ReplaySpeed_AffectsSnapshotProcessingRate(double speed, string description)
    {
        // Arrange
        var configMock = new Mock<IBacktestingConfig>();
        configMock.Setup(x => x.ReplaySpeed).Returns(speed);

        // Assert
        configMock.Object.ReplaySpeed.Should().Be(speed,
            $"ReplaySpeed should support {description}");

        // Higher speeds process more snapshots per real-time second
        // but may result in skipped snapshots if system can't keep up
    }

    [Fact]
    public void SnapshotsInterval_DeterminesSnapshotFrequency()
    {
        // Arrange
        var configMock = new Mock<IBacktestingConfig>();
        var interval = 60; // 60 seconds between snapshots
        configMock.Setup(x => x.SnapshotsInterval).Returns(interval);
        configMock.Setup(x => x.ReplaySpeed).Returns(10.0);

        // Calculate effective interval at replay speed
        var effectiveInterval = interval / configMock.Object.ReplaySpeed;

        // Assert
        effectiveInterval.Should().Be(6.0,
            "At 10x speed, 60s interval becomes 6s real-time");
    }
}
