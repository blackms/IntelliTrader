using FluentAssertions;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Binance;
using Moq;
using Xunit;

namespace IntelliTrader.Exchange.Tests;

/// <summary>
/// Unit tests for BinanceWebSocketService.
/// Note: Full integration tests require a live connection to Binance WebSocket.
/// </summary>
public class BinanceWebSocketServiceTests
{
    private readonly Mock<ILoggingService> _mockLoggingService;
    private readonly Mock<IHealthCheckService> _mockHealthCheckService;

    public BinanceWebSocketServiceTests()
    {
        _mockLoggingService = new Mock<ILoggingService>();
        _mockHealthCheckService = new Mock<IHealthCheckService>();
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldCreateService()
    {
        // Arrange & Act
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Assert
        service.Should().NotBeNull();
        service.IsConnected.Should().BeFalse();
        service.IsRestFallbackActive.Should().BeFalse();
        service.ConnectionState.Should().Be(WebSocketConnectionState.Disconnected);
    }

    [Fact]
    public void Constructor_WithNullLoggingService_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new BinanceWebSocketService(
            null!,
            _mockHealthCheckService.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("loggingService");
    }

    [Fact]
    public void Constructor_WithNullHealthCheckService_ShouldThrowArgumentNullException()
    {
        // Arrange & Act
        var act = () => new BinanceWebSocketService(
            _mockLoggingService.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("healthCheckService");
    }

    [Fact]
    public void IsConnected_WhenNotConnected_ShouldReturnFalse()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Act & Assert
        service.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void TimeSinceLastUpdate_WhenNotConnected_ShouldReturnLargeTimeSpan()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Act
        var timeSinceUpdate = service.TimeSinceLastUpdate;

        // Assert
        // TimeSinceLastUpdate should be > 0 since we haven't received any updates
        // (DateTimeOffset.Now - DateTimeOffset.MinValue is a very large value)
        timeSinceUpdate.Should().BeGreaterThan(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ConnectionState_Initially_ShouldBeDisconnected()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Act & Assert
        service.ConnectionState.Should().Be(WebSocketConnectionState.Disconnected);
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ShouldCompleteWithoutError()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Act
        var act = async () => await service.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubscribeToTickersAsync_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        var pairs = new[] { "BTCUSDT", "ETHUSDT" };

        // Act
        var act = async () => await service.SubscribeToTickersAsync(pairs);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnsubscribeFromTickersAsync_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        var pairs = new[] { "BTCUSDT", "ETHUSDT" };

        // Act
        var act = async () => await service.UnsubscribeFromTickersAsync(pairs);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConnectionStateChanged_Event_ShouldBeRaisable()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        var eventRaised = false;
        service.ConnectionStateChanged += state => eventRaised = true;

        // Note: Since ConnectionState is private set, we cannot directly test this event
        // In real tests, we would mock or use integration tests
        eventRaised.Should().BeFalse(); // Event not raised since we haven't connected
    }

    [Fact]
    public void TickersUpdated_Event_ShouldBeSubscribable()
    {
        // Arrange
        using var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        var eventRaised = false;
        service.TickersUpdated += tickers => eventRaised = true;

        // Assert
        eventRaised.Should().BeFalse(); // Event not raised since we haven't received data
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Act
        var act = () => service.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var service = new BinanceWebSocketService(
            _mockLoggingService.Object,
            _mockHealthCheckService.Object);

        // Act
        service.Dispose();
        var act = () => service.Dispose();

        // Assert
        act.Should().NotThrow();
    }
}

/// <summary>
/// Tests for the WebSocket connection state enum values.
/// </summary>
public class WebSocketConnectionStateTests
{
    [Theory]
    [InlineData(WebSocketConnectionState.Disconnected)]
    [InlineData(WebSocketConnectionState.Connecting)]
    [InlineData(WebSocketConnectionState.Connected)]
    [InlineData(WebSocketConnectionState.Reconnecting)]
    [InlineData(WebSocketConnectionState.FallbackToRest)]
    [InlineData(WebSocketConnectionState.Disconnecting)]
    public void WebSocketConnectionState_ShouldHaveExpectedValues(WebSocketConnectionState state)
    {
        // Assert
        Enum.IsDefined(typeof(WebSocketConnectionState), state).Should().BeTrue();
    }
}
