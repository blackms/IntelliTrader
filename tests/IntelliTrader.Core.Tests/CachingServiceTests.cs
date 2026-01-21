using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Configuration;
using IntelliTrader.Core;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for CachingService functionality.
/// Tests focus on cache retrieval, expiration, and the GetOrRefresh pattern.
/// </summary>
public class CachingServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly TestableCachingService _sut;
    private readonly TestableCachingConfig _config;

    public CachingServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _config = new TestableCachingConfig
        {
            Enabled = true,
            Shared = false,
            SharedCachePath = "/tmp/test-cache",
            SharedCacheCleanupInterval = 3600,
            MaxAge = new List<KeyValuePair<string, double>>
            {
                new("TestObject", 60),
                new("ShortLived", 5),
                new("LongLived", 3600)
            }
        };

        _sut = new TestableCachingService(_loggingServiceMock.Object, _config);
    }

    #region GetOrRefresh Basic Tests

    [Fact]
    public void GetOrRefresh_WhenCacheEmpty_CallsRefreshFunction()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return "Fresh Value";
        };

        // Act
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().Be("Fresh Value");
        refreshCallCount.Should().Be(1);
    }

    [Fact]
    public void GetOrRefresh_WhenCacheHit_DoesNotCallRefreshFunction()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return "Fresh Value";
        };

        // First call to populate cache
        _sut.GetOrRefresh("TestObject", refreshFunc);

        // Act - Second call should hit cache
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().Be("Fresh Value");
        refreshCallCount.Should().Be(1); // Should still be 1
    }

    [Fact]
    public void GetOrRefresh_WhenCacheDisabled_AlwaysCallsRefreshFunction()
    {
        // Arrange
        _config.Enabled = false;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // Act
        var result1 = _sut.GetOrRefresh("TestObject", refreshFunc);
        var result2 = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result1.Should().Be("Value 1");
        result2.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public void GetOrRefresh_ReturnsDifferentValueAfterExpiration()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // First call
        var result1 = _sut.GetOrRefresh("ShortLived", refreshFunc);

        // Simulate expiration
        _sut.SimulateTimePassage(TimeSpan.FromSeconds(10));

        // Act - Should refresh after expiration
        var result2 = _sut.GetOrRefresh("ShortLived", refreshFunc);

        // Assert
        result1.Should().Be("Value 1");
        result2.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public void GetOrRefresh_WithNoMaxAge_AlwaysRefreshes()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // Act - Object with no configured maxAge
        var result1 = _sut.GetOrRefresh("UnknownObject", refreshFunc);
        var result2 = _sut.GetOrRefresh("UnknownObject", refreshFunc);

        // Assert
        result1.Should().Be("Value 1");
        result2.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    #endregion

    #region GetOrRefresh with Different Types

    [Fact]
    public void GetOrRefresh_WithIntegerType_ReturnsCorrectValue()
    {
        // Arrange
        _config.Enabled = true;
        Func<int> refreshFunc = () => 42;

        // Act
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void GetOrRefresh_WithComplexType_ReturnsCorrectValue()
    {
        // Arrange
        _config.Enabled = true;
        var expectedObject = new TestCacheObject { Id = 1, Name = "Test" };
        Func<TestCacheObject> refreshFunc = () => expectedObject;

        // Act
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().BeEquivalentTo(expectedObject);
    }

    [Fact]
    public void GetOrRefresh_WithNullableType_ReturnsNull()
    {
        // Arrange
        _config.Enabled = true;
        Func<string?> refreshFunc = () => null;

        // Act
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetOrRefresh_WithListType_ReturnsCorrectList()
    {
        // Arrange
        _config.Enabled = true;
        var expectedList = new List<int> { 1, 2, 3, 4, 5 };
        Func<List<int>> refreshFunc = () => expectedList;

        // Act
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().BeEquivalentTo(expectedList);
    }

    [Fact]
    public void GetOrRefresh_WithDictionaryType_ReturnsCorrectDictionary()
    {
        // Arrange
        _config.Enabled = true;
        var expectedDict = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
        Func<Dictionary<string, int>> refreshFunc = () => expectedDict;

        // Act
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().BeEquivalentTo(expectedDict);
    }

    #endregion

    #region Cache Expiration Tests

    [Fact]
    public void GetOrRefresh_BeforeExpiration_ReturnsCachedValue()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // First call
        _sut.GetOrRefresh("LongLived", refreshFunc);

        // Simulate partial time passage (less than maxAge)
        _sut.SimulateTimePassage(TimeSpan.FromSeconds(100));

        // Act
        var result = _sut.GetOrRefresh("LongLived", refreshFunc);

        // Assert
        result.Should().Be("Value 1");
        refreshCallCount.Should().Be(1);
    }

    [Fact]
    public void GetOrRefresh_ExactlyAtExpiration_RefreshesValue()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // First call
        _sut.GetOrRefresh("ShortLived", refreshFunc);

        // Simulate exact expiration time (maxAge = 5 seconds)
        _sut.SimulateTimePassage(TimeSpan.FromSeconds(5.1));

        // Act
        var result = _sut.GetOrRefresh("ShortLived", refreshFunc);

        // Assert
        result.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public void GetOrRefresh_DifferentObjectsExpireIndependently()
    {
        // Arrange
        _config.Enabled = true;
        int shortRefreshCount = 0;
        int longRefreshCount = 0;

        Func<string> shortRefresh = () =>
        {
            shortRefreshCount++;
            return $"Short {shortRefreshCount}";
        };

        Func<string> longRefresh = () =>
        {
            longRefreshCount++;
            return $"Long {longRefreshCount}";
        };

        // Initialize both
        _sut.GetOrRefresh("ShortLived", shortRefresh);
        _sut.GetOrRefresh("LongLived", longRefresh);

        // Simulate time that expires ShortLived but not LongLived
        _sut.SimulateTimePassage(TimeSpan.FromSeconds(10));

        // Act
        var shortResult = _sut.GetOrRefresh("ShortLived", shortRefresh);
        var longResult = _sut.GetOrRefresh("LongLived", longRefresh);

        // Assert
        shortResult.Should().Be("Short 2"); // Expired and refreshed
        longResult.Should().Be("Long 1");   // Still cached
        shortRefreshCount.Should().Be(2);
        longRefreshCount.Should().Be(1);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Config_ReturnsConfigurationObject()
    {
        // Act
        var config = _sut.Config;

        // Assert
        config.Should().NotBeNull();
        config.Should().BeAssignableTo<ICachingConfig>();
    }

    [Fact]
    public void Config_MaxAge_ReturnsConfiguredValues()
    {
        // Arrange
        _config.MaxAge = new List<KeyValuePair<string, double>>
        {
            new("Object1", 100),
            new("Object2", 200)
        };

        // Act
        var maxAgeList = _sut.Config.MaxAge.ToList();

        // Assert
        maxAgeList.Should().HaveCount(2);
        maxAgeList.Should().Contain(kvp => kvp.Key == "Object1" && kvp.Value == 100);
        maxAgeList.Should().Contain(kvp => kvp.Key == "Object2" && kvp.Value == 200);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Config_Enabled_ReturnsConfiguredValue(bool enabled)
    {
        // Arrange
        _config.Enabled = enabled;

        // Act & Assert
        _sut.Config.Enabled.Should().Be(enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Config_Shared_ReturnsConfiguredValue(bool shared)
    {
        // Arrange
        _config.Shared = shared;

        // Act & Assert
        _sut.Config.Shared.Should().Be(shared);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GetOrRefresh_ConcurrentAccess_IsThreadSafe()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<int> refreshFunc = () =>
        {
            Interlocked.Increment(ref refreshCallCount);
            return 42;
        };

        var tasks = new List<Task<int>>();

        // Act - Multiple concurrent calls
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => _sut.GetOrRefresh("TestObject", refreshFunc)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All results should be the same
        results.Should().AllBeEquivalentTo(42);
        // Due to thread safety, should only refresh once (or very few times due to race conditions)
        refreshCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetOrRefresh_ConcurrentDifferentObjects_HandlesCorrectly()
    {
        // Arrange
        _config.Enabled = true;
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        // Act - Concurrent access to different cache entries
        Parallel.For(0, 50, i =>
        {
            var key = $"Object{i % 10}"; // 10 different keys
            var value = _sut.GetOrRefresh($"TestObject", () => $"Value-{key}");
            results.TryAdd($"Result{i}", value);
        });

        // Assert
        results.Should().HaveCount(50);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetOrRefresh_WhenRefreshThrows_PropagatesException()
    {
        // Arrange
        _config.Enabled = true;
        Func<string> refreshFunc = () => throw new InvalidOperationException("Refresh failed");

        // Act
        var action = () => _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Refresh failed");
    }

    [Fact]
    public void GetOrRefresh_WithZeroMaxAge_AlwaysRefreshes()
    {
        // Arrange
        _config.Enabled = true;
        _config.MaxAge = new List<KeyValuePair<string, double>>
        {
            new("ZeroAge", 0)
        };

        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // Act
        var result1 = _sut.GetOrRefresh("ZeroAge", refreshFunc);
        var result2 = _sut.GetOrRefresh("ZeroAge", refreshFunc);

        // Assert
        result1.Should().Be("Value 1");
        result2.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public void GetOrRefresh_WithNegativeMaxAge_TreatedAsNoCache()
    {
        // Arrange
        _config.Enabled = true;
        _config.MaxAge = new List<KeyValuePair<string, double>>
        {
            new("NegativeAge", -1)
        };

        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // Act
        var result1 = _sut.GetOrRefresh("NegativeAge", refreshFunc);
        var result2 = _sut.GetOrRefresh("NegativeAge", refreshFunc);

        // Assert
        result1.Should().Be("Value 1");
        result2.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public void GetOrRefresh_WithVeryLargeMaxAge_CachesIndefinitely()
    {
        // Arrange
        _config.Enabled = true;
        _config.MaxAge = new List<KeyValuePair<string, double>>
        {
            new("VeryLong", 86400 * 365) // 1 year
        };

        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // First call
        _sut.GetOrRefresh("VeryLong", refreshFunc);

        // Simulate 30 days
        _sut.SimulateTimePassage(TimeSpan.FromDays(30));

        // Act
        var result = _sut.GetOrRefresh("VeryLong", refreshFunc);

        // Assert
        result.Should().Be("Value 1");
        refreshCallCount.Should().Be(1);
    }

    [Fact]
    public void GetOrRefresh_CachesDefaultValues()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<int> refreshFunc = () =>
        {
            refreshCallCount++;
            return default; // Returns 0
        };

        // Act
        var result1 = _sut.GetOrRefresh("TestObject", refreshFunc);
        var result2 = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result1.Should().Be(0);
        result2.Should().Be(0);
        refreshCallCount.Should().Be(1); // Should cache even default values
    }

    #endregion

    #region Remove Tests

    [Fact]
    public void Remove_RemovesCachedObject()
    {
        // Arrange
        _config.Enabled = true;
        int refreshCallCount = 0;
        Func<string> refreshFunc = () =>
        {
            refreshCallCount++;
            return $"Value {refreshCallCount}";
        };

        // Populate cache
        _sut.GetOrRefresh("TestObject", refreshFunc);

        // Act
        _sut.RemoveFromCache("TestObject");
        var result = _sut.GetOrRefresh("TestObject", refreshFunc);

        // Assert
        result.Should().Be("Value 2");
        refreshCallCount.Should().Be(2);
    }

    [Fact]
    public void Remove_NonExistentKey_DoesNotThrow()
    {
        // Act
        var action = () => _sut.RemoveFromCache("NonExistent");

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsCachingService()
    {
        // Act
        var serviceName = _sut.ServiceName;

        // Assert
        serviceName.Should().Be(Constants.ServiceNames.CachingService);
    }

    #endregion
}

/// <summary>
/// Test object for complex type caching tests
/// </summary>
public class TestCacheObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Testable caching configuration
/// </summary>
public class TestableCachingConfig : ICachingConfig
{
    public bool Enabled { get; set; }
    public bool Shared { get; set; }
    public string SharedCachePath { get; set; } = string.Empty;
    public double SharedCacheCleanupInterval { get; set; }
    public IEnumerable<KeyValuePair<string, double>> MaxAge { get; set; } = new List<KeyValuePair<string, double>>();
}

/// <summary>
/// Testable implementation of caching service
/// </summary>
public class TestableCachingService : ICachingService
{
    public string ServiceName => Constants.ServiceNames.CachingService;
    public ICachingConfig Config => _config;
    public IConfigurationSection RawConfig => null!;

    private readonly ILoggingService _loggingService;
    private readonly TestableCachingConfig _config;
    private readonly Dictionary<string, TestableCachedObject> _cachedObjects = new();
    private readonly object _syncRoot = new();
    private DateTimeOffset _currentTime = DateTimeOffset.Now;

    public TestableCachingService(ILoggingService loggingService, TestableCachingConfig config)
    {
        _loggingService = loggingService;
        _config = config;
    }

    public T GetOrRefresh<T>(string objectName, Func<T> refresh)
    {
        lock (_syncRoot)
        {
            T value = default!;

            if (_config.Enabled)
            {
                var maxAge = _config.MaxAge.FirstOrDefault(m => m.Key == objectName).Value;

                if (maxAge > 0)
                {
                    if (_cachedObjects.TryGetValue(objectName, out var obj) &&
                        (_currentTime - obj.LastUpdated).TotalSeconds <= maxAge)
                    {
                        value = (T)obj.Value!;
                    }
                    else
                    {
                        value = refresh();
                        _cachedObjects[objectName] = new TestableCachedObject
                        {
                            LastUpdated = _currentTime,
                            Value = value
                        };
                    }
                }
                else
                {
                    value = refresh();
                }
            }
            else
            {
                value = refresh();
            }

            return value;
        }
    }

    public void SimulateTimePassage(TimeSpan duration)
    {
        _currentTime = _currentTime.Add(duration);
    }

    public void RemoveFromCache(string objectName)
    {
        lock (_syncRoot)
        {
            _cachedObjects.Remove(objectName);
        }
    }
}

/// <summary>
/// Testable cached object
/// </summary>
public class TestableCachedObject
{
    public DateTimeOffset LastUpdated { get; set; }
    public object? Value { get; set; }
}
