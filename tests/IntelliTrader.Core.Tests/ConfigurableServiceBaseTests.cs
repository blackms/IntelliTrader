using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Configuration;
using IntelliTrader.Core;
using System.Reflection;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for ConfigurableServiceBase - the common base class for all configurable services.
/// Uses a concrete test subclass to exercise the abstract base behavior.
/// </summary>
public class ConfigurableServiceBaseTests
{
    private readonly Mock<IConfigProvider> _configProviderMock;

    public ConfigurableServiceBaseTests()
    {
        _configProviderMock = new Mock<IConfigProvider>();
    }

    /// <summary>
    /// Creates a real IConfigurationSection backed by in-memory data,
    /// because IConfigurationSection.Get&lt;T&gt;() is an extension method and cannot be mocked.
    /// </summary>
    private static IConfigurationSection CreateRealConfigSection(string serviceName, string value)
    {
        var data = new Dictionary<string, string?>
        {
            { $"{serviceName}:Value", value }
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        return config.GetSection(serviceName);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullConfigProvider_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestConfigurableService(null!);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("configProvider");
    }

    [Fact]
    public void Constructor_WithValidConfigProvider_Succeeds()
    {
        // Act
        var sut = new TestConfigurableService(_configProviderMock.Object);

        // Assert
        sut.Should().NotBeNull();
    }

    #endregion

    #region ServiceName Tests

    [Fact]
    public void ServiceName_ReturnsImplementedValue()
    {
        // Arrange
        var sut = new TestConfigurableService(_configProviderMock.Object);

        // Act & Assert
        sut.ServiceName.Should().Be("TestService");
    }

    #endregion

    #region Config Property Tests

    [Fact]
    public void Config_FirstAccess_CallsGetSectionOnProvider()
    {
        // Arrange
        var configSection = CreateRealConfigSection("TestService", "test");
        _configProviderMock
            .Setup(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()))
            .Returns(configSection);

        var sut = new TestConfigurableService(_configProviderMock.Object);

        // Act
        var config = sut.Config;

        // Assert
        config.Should().NotBeNull();
        config.Value.Should().Be("test");
        _configProviderMock.Verify(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()), Times.Once);
    }

    [Fact]
    public void Config_SecondAccess_ReturnsCachedConfig()
    {
        // Arrange
        var configSection = CreateRealConfigSection("TestService", "cached");
        _configProviderMock
            .Setup(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()))
            .Returns(configSection);

        var sut = new TestConfigurableService(_configProviderMock.Object);

        // Act
        var config1 = sut.Config;
        var config2 = sut.Config;

        // Assert
        config1.Should().BeSameAs(config2);
        _configProviderMock.Verify(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()), Times.Once);
    }

    #endregion

    #region RawConfig Property Tests

    [Fact]
    public void RawConfig_FirstAccess_CallsGetSectionOnProvider()
    {
        // Arrange
        var configSection = CreateRealConfigSection("TestService", "raw");
        _configProviderMock
            .Setup(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()))
            .Returns(configSection);

        var sut = new TestConfigurableService(_configProviderMock.Object);

        // Act
        var rawConfig = sut.RawConfig;

        // Assert
        rawConfig.Should().NotBeNull();
    }

    [Fact]
    public void RawConfig_SecondAccess_ReturnsCachedSection()
    {
        // Arrange
        var configSection = CreateRealConfigSection("TestService", "raw");
        _configProviderMock
            .Setup(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()))
            .Returns(configSection);

        var sut = new TestConfigurableService(_configProviderMock.Object);

        // Act
        var raw1 = sut.RawConfig;
        var raw2 = sut.RawConfig;

        // Assert
        raw1.Should().BeSameAs(raw2);
        _configProviderMock.Verify(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()), Times.Once);
    }

    #endregion

    #region LoggingService Property Tests

    [Fact]
    public void LoggingService_DefaultImplementation_ReturnsNull()
    {
        // Arrange
        var sut = new TestConfigurableServiceWithoutLogging(_configProviderMock.Object);

        // Act - Access via reflection since it's protected
        var loggingProp = typeof(ConfigurableServiceBase<TestConfig>)
            .GetProperty("LoggingService", BindingFlags.Instance | BindingFlags.NonPublic);

        var result = loggingProp!.GetValue(sut);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void LoggingService_WhenOverridden_ReturnsInjectedService()
    {
        // Arrange
        var loggingMock = new Mock<ILoggingService>();
        var sut = new TestConfigurableServiceWithLogging(_configProviderMock.Object, loggingMock.Object);

        // Act - Access via reflection since it's protected
        var loggingProp = typeof(ConfigurableServiceBase<TestConfig>)
            .GetProperty("LoggingService", BindingFlags.Instance | BindingFlags.NonPublic);

        var result = loggingProp!.GetValue(sut);

        // Assert
        result.Should().BeSameAs(loggingMock.Object);
    }

    #endregion

    #region Config Reload Tests

    [Fact]
    public void OnRawConfigChanged_InvalidatesConfigCache()
    {
        // Arrange
        var originalSection = CreateRealConfigSection("TestService", "original");

        Action<IConfigurationSection> capturedOnChange = null!;
        _configProviderMock
            .Setup(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()))
            .Callback<string, Action<IConfigurationSection>>((_, onChange) => capturedOnChange = onChange)
            .Returns(originalSection);

        var sut = new TestConfigurableService(_configProviderMock.Object);

        // First access to populate cache
        var firstConfig = sut.Config;
        firstConfig.Value.Should().Be("original");

        // Simulate config change with a new section
        var updatedSection = CreateRealConfigSection("TestService", "updated");

        // Act - trigger the onChange callback
        capturedOnChange?.Invoke(updatedSection);

        // Assert - config should be re-read from the new section
        var secondConfig = sut.Config;
        secondConfig.Value.Should().Be("updated");
    }

    [Fact]
    public void OnConfigReloaded_IsCalledOnChange()
    {
        // Arrange
        var configSection = CreateRealConfigSection("TestService", "test");

        Action<IConfigurationSection> capturedOnChange = null!;
        _configProviderMock
            .Setup(x => x.GetSection("TestService", It.IsAny<Action<IConfigurationSection>>()))
            .Callback<string, Action<IConfigurationSection>>((_, onChange) => capturedOnChange = onChange)
            .Returns(configSection);

        var sut = new TestConfigurableService(_configProviderMock.Object);
        _ = sut.Config; // trigger initial load

        // Act
        capturedOnChange?.Invoke(configSection);

        // Assert
        sut.OnConfigReloadedCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion
}

/// <summary>
/// Test configuration model
/// </summary>
public class TestConfig
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Concrete test subclass of ConfigurableServiceBase for testing
/// </summary>
public class TestConfigurableService : ConfigurableServiceBase<TestConfig>
{
    public override string ServiceName => "TestService";
    protected override ILoggingService LoggingService => null!;

    public int OnConfigReloadedCallCount { get; private set; }

    public TestConfigurableService(IConfigProvider configProvider) : base(configProvider) { }

    protected override void OnConfigReloaded()
    {
        OnConfigReloadedCallCount++;
    }
}

/// <summary>
/// Test subclass that doesn't override LoggingService
/// </summary>
public class TestConfigurableServiceWithoutLogging : ConfigurableServiceBase<TestConfig>
{
    public override string ServiceName => "TestService";

    public TestConfigurableServiceWithoutLogging(IConfigProvider configProvider) : base(configProvider) { }
}

/// <summary>
/// Test subclass that provides a logging service
/// </summary>
public class TestConfigurableServiceWithLogging : ConfigurableServiceBase<TestConfig>
{
    private readonly ILoggingService _loggingService;

    public override string ServiceName => "TestService";
    protected override ILoggingService LoggingService => _loggingService;

    public TestConfigurableServiceWithLogging(IConfigProvider configProvider, ILoggingService loggingService)
        : base(configProvider)
    {
        _loggingService = loggingService;
    }
}
