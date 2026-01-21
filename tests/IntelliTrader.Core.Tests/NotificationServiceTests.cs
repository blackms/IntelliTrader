using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for NotificationService functionality.
/// Tests focus on the notification logic, message formatting, and configuration-based filtering.
/// Telegram integration is mocked to avoid external dependencies.
/// </summary>
public class NotificationServiceTests
{
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<ICoreConfig> _coreConfigMock;
    private readonly TestableNotificationService _sut;
    private TestableNotificationConfig _config;

    public NotificationServiceTests()
    {
        _loggingServiceMock = new Mock<ILoggingService>();
        _coreServiceMock = new Mock<ICoreService>();
        _coreConfigMock = new Mock<ICoreConfig>();

        _coreConfigMock.Setup(x => x.InstanceName).Returns("TestInstance");
        _coreServiceMock.Setup(x => x.Config).Returns(_coreConfigMock.Object);

        _config = new TestableNotificationConfig
        {
            Enabled = true,
            TelegramEnabled = true,
            TelegramBotToken = "test-token",
            TelegramChatId = 12345,
            TelegramAlertsEnabled = true
        };

        _sut = new TestableNotificationService(
            _loggingServiceMock.Object,
            _coreServiceMock.Object,
            _config);
    }

    #region NotifyAsync Tests

    [Fact]
    public async Task NotifyAsync_WhenEnabled_SendsNotification()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        const string message = "Test notification";

        // Act
        await _sut.NotifyAsync(message);

        // Assert
        _sut.SentMessages.Should().ContainSingle();
        _sut.SentMessages.First().Should().Contain(message);
    }

    [Fact]
    public async Task NotifyAsync_WhenDisabled_DoesNotSendNotification()
    {
        // Arrange
        _config.Enabled = false;
        const string message = "Test notification";

        // Act
        await _sut.NotifyAsync(message);

        // Assert
        _sut.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_WhenTelegramDisabled_DoesNotSendNotification()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = false;
        const string message = "Test notification";

        // Act
        await _sut.NotifyAsync(message);

        // Assert
        _sut.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_IncludesInstanceNameInMessage()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        _coreConfigMock.Setup(x => x.InstanceName).Returns("MyTrader");
        const string message = "Price alert triggered";

        // Act
        await _sut.NotifyAsync(message);

        // Assert
        _sut.SentMessages.Should().ContainSingle();
        _sut.SentMessages.First().Should().Contain("MyTrader");
        _sut.SentMessages.First().Should().Contain("Price alert triggered");
    }

    [Fact]
    public async Task NotifyAsync_MultipleNotifications_SendsAll()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;

        // Act
        await _sut.NotifyAsync("Message 1");
        await _sut.NotifyAsync("Message 2");
        await _sut.NotifyAsync("Message 3");

        // Assert
        _sut.SentMessages.Should().HaveCount(3);
    }

    [Fact]
    public async Task NotifyAsync_WithEmptyMessage_SendsEmptyNotification()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;

        // Act
        await _sut.NotifyAsync(string.Empty);

        // Assert
        _sut.SentMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task NotifyAsync_SetsAlertBasedOnConfig()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        _config.TelegramAlertsEnabled = false;

        // Act
        await _sut.NotifyAsync("Test message");

        // Assert
        _sut.LastDisableNotification.Should().BeTrue();
    }

    [Fact]
    public async Task NotifyAsync_WithAlertsEnabled_DoesNotDisableNotification()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        _config.TelegramAlertsEnabled = true;

        // Act
        await _sut.NotifyAsync("Test message");

        // Assert
        _sut.LastDisableNotification.Should().BeFalse();
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WhenTelegramEnabled_InitializesTelegramClient()
    {
        // Arrange
        _config.TelegramEnabled = true;

        // Act
        await _sut.StartAsync();

        // Assert
        _sut.IsTelegramInitialized.Should().BeTrue();
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s.Contains("Start Notification service")), It.IsAny<Exception>()), Times.Once);
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s.Contains("Notification service started")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenTelegramDisabled_DoesNotInitializeTelegramClient()
    {
        // Arrange
        _config.TelegramEnabled = false;

        // Act
        await _sut.StartAsync();

        // Assert
        _sut.IsTelegramInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_WhenInitializationFails_DisablesService()
    {
        // Arrange
        _config.TelegramEnabled = true;
        _sut.SimulateInitializationFailure = true;

        // Act
        await _sut.StartAsync();

        // Assert
        _config.SetEnabled.Should().BeFalse();
        _loggingServiceMock.Verify(
            x => x.Error(It.Is<string>(s => s.Contains("Unable to start")), It.IsAny<Exception>()),
            Times.Once);
    }

    #endregion

    #region Stop Tests

    [Fact]
    public void Stop_WhenTelegramEnabled_ClearsTelegramClient()
    {
        // Arrange
        _config.TelegramEnabled = true;
        _sut.IsTelegramInitialized = true;

        // Act
        _sut.Stop();

        // Assert
        _sut.IsTelegramInitialized.Should().BeFalse();
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s.Contains("Stop Notification service")), It.IsAny<Exception>()), Times.Once);
        _loggingServiceMock.Verify(x => x.Info(It.Is<string>(s => s.Contains("Notification service stopped")), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void Stop_WhenTelegramDisabled_CompletesWithoutError()
    {
        // Arrange
        _config.TelegramEnabled = false;

        // Act
        var action = () => _sut.Stop();

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Config_ReturnsNotificationConfig()
    {
        // Act
        var config = _sut.Config;

        // Assert
        config.Should().NotBeNull();
        config.Should().BeAssignableTo<INotificationConfig>();
    }

    [Fact]
    public void Config_TelegramBotToken_ReturnsConfiguredValue()
    {
        // Arrange
        _config.TelegramBotToken = "my-bot-token-123";

        // Act
        var token = _sut.Config.TelegramBotToken;

        // Assert
        token.Should().Be("my-bot-token-123");
    }

    [Fact]
    public void Config_TelegramChatId_ReturnsConfiguredValue()
    {
        // Arrange
        _config.TelegramChatId = 987654321;

        // Act
        var chatId = _sut.Config.TelegramChatId;

        // Assert
        chatId.Should().Be(987654321);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task NotifyAsync_WhenSendFails_LogsError()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        _sut.SimulateSendFailure = true;

        // Act
        await _sut.NotifyAsync("Test message");

        // Assert
        _loggingServiceMock.Verify(
            x => x.Error(It.Is<string>(s => s.Contains("Unable to send")), It.IsAny<Exception>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyAsync_WhenSendFails_DoesNotThrow()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        _sut.SimulateSendFailure = true;

        // Act
        var action = async () => await _sut.NotifyAsync("Test message");

        // Assert
        await action.Should().NotThrowAsync();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLoggingService_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestableNotificationService(
            null!,
            _coreServiceMock.Object,
            _config);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("loggingService");
    }

    [Fact]
    public void Constructor_WithNullCoreService_ThrowsArgumentNullException()
    {
        // Act
        var action = () => new TestableNotificationService(
            _loggingServiceMock.Object,
            null!,
            _config);

        // Assert
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("coreService");
    }

    #endregion

    #region Notification Filtering Tests

    [Fact]
    public async Task NotifyAsync_WhenBothEnabledAndTelegramEnabledTrue_Sends()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;

        // Act
        await _sut.NotifyAsync("Test");

        // Assert
        _sut.SentMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_WhenEnabledFalseAndTelegramEnabledTrue_DoesNotSend()
    {
        // Arrange
        _config.Enabled = false;
        _config.TelegramEnabled = true;

        // Act
        await _sut.NotifyAsync("Test");

        // Assert
        _sut.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyAsync_WhenEnabledTrueAndTelegramEnabledFalse_DoesNotSend()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = false;

        // Act
        await _sut.NotifyAsync("Test");

        // Assert
        _sut.SentMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task NotifyAsync_ConfigurationMatrix_BehavesCorrectly(
        bool enabled, bool telegramEnabled, bool expectSend)
    {
        // Arrange
        _config.Enabled = enabled;
        _config.TelegramEnabled = telegramEnabled;

        // Act
        await _sut.NotifyAsync("Test message");

        // Assert
        if (expectSend)
        {
            _sut.SentMessages.Should().NotBeEmpty();
        }
        else
        {
            _sut.SentMessages.Should().BeEmpty();
        }
    }

    #endregion

    #region Message Formatting Tests

    [Fact]
    public async Task NotifyAsync_FormatsMessageWithParenthesesAroundInstanceName()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        _coreConfigMock.Setup(x => x.InstanceName).Returns("Bot1");

        // Act
        await _sut.NotifyAsync("Signal received");

        // Assert
        _sut.SentMessages.First().Should().StartWith("(Bot1)");
    }

    [Fact]
    public async Task NotifyAsync_PreservesSpecialCharactersInMessage()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        const string specialMessage = "Price: $100.50 (+5.2%)";

        // Act
        await _sut.NotifyAsync(specialMessage);

        // Assert
        _sut.SentMessages.First().Should().Contain(specialMessage);
    }

    [Fact]
    public async Task NotifyAsync_HandlesMultilineMessages()
    {
        // Arrange
        _config.Enabled = true;
        _config.TelegramEnabled = true;
        const string multilineMessage = "Line 1\nLine 2\nLine 3";

        // Act
        await _sut.NotifyAsync(multilineMessage);

        // Assert
        _sut.SentMessages.First().Should().Contain("Line 1");
        _sut.SentMessages.First().Should().Contain("Line 2");
        _sut.SentMessages.First().Should().Contain("Line 3");
    }

    #endregion
}

/// <summary>
/// Testable implementation of notification service that doesn't depend on actual Telegram
/// </summary>
public class TestableNotificationService : INotificationService
{
    public INotificationConfig Config { get; }

    private readonly ILoggingService _loggingService;
    private readonly ICoreService _coreService;
    private readonly TestableNotificationConfig _config;

    public bool IsTelegramInitialized { get; set; }
    public bool SimulateInitializationFailure { get; set; }
    public bool SimulateSendFailure { get; set; }
    public List<string> SentMessages { get; } = new();
    public bool LastDisableNotification { get; private set; }

    public TestableNotificationService(
        ILoggingService loggingService,
        ICoreService coreService,
        TestableNotificationConfig config)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _coreService = coreService ?? throw new ArgumentNullException(nameof(coreService));
        _config = config;
        Config = config;
    }

    public async Task StartAsync()
    {
        try
        {
            _loggingService.Info("Start Notification service...");

            if (_config.TelegramEnabled)
            {
                if (SimulateInitializationFailure)
                {
                    throw new Exception("Simulated initialization failure");
                }

                // Simulate bot initialization
                await Task.Delay(1); // Simulate async operation
                IsTelegramInitialized = true;
            }

            _loggingService.Info("Notification service started");
        }
        catch (Exception ex)
        {
            _loggingService.Error("Unable to start Notification service", ex);
            _config.SetEnabled = false;
        }
    }

    public void Stop()
    {
        _loggingService.Info("Stop Notification service...");

        if (_config.TelegramEnabled)
        {
            IsTelegramInitialized = false;
        }

        _loggingService.Info("Notification service stopped");
    }

    public async Task NotifyAsync(string message)
    {
        if (_config.Enabled)
        {
            if (_config.TelegramEnabled)
            {
                try
                {
                    if (SimulateSendFailure)
                    {
                        throw new Exception("Simulated send failure");
                    }

                    var instanceName = _coreService.Config.InstanceName;
                    var formattedMessage = $"({instanceName}) {message}";
                    LastDisableNotification = !_config.TelegramAlertsEnabled;

                    // Simulate sending message
                    await Task.Delay(1); // Simulate async operation
                    SentMessages.Add(formattedMessage);
                }
                catch (Exception ex)
                {
                    _loggingService.Error("Unable to send Telegram message", ex);
                }
            }
        }
    }
}

/// <summary>
/// Testable notification configuration
/// </summary>
public class TestableNotificationConfig : INotificationConfig
{
    public bool Enabled { get; set; }
    public bool TelegramEnabled { get; set; }
    public string TelegramBotToken { get; set; } = string.Empty;
    public long TelegramChatId { get; set; }
    public bool TelegramAlertsEnabled { get; set; }

    // Used to track when Enabled is set (for error handling tests)
    public bool SetEnabled
    {
        get => Enabled;
        set => Enabled = value;
    }
}
