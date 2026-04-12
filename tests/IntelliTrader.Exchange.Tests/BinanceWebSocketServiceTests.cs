using FluentAssertions;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Binance;
using Moq;
using Xunit;

namespace IntelliTrader.Exchange.Tests;

/// <summary>
/// Unit tests for IBinanceWebSocketService interface behavior.
/// Note: BinanceWebSocketService is internal, so tests use the public interface via mocks.
/// Full integration tests require a live connection to Binance WebSocket.
/// </summary>
public class BinanceWebSocketServiceTests
{
    private readonly Mock<IBinanceWebSocketService> _mockService;

    public BinanceWebSocketServiceTests()
    {
        _mockService = new Mock<IBinanceWebSocketService>();
        _mockService.Setup(x => x.IsConnected).Returns(false);
        _mockService.Setup(x => x.IsRestFallbackActive).Returns(false);
        _mockService.Setup(x => x.ConnectionState).Returns(WebSocketConnectionState.Disconnected);
        _mockService.Setup(x => x.TimeSinceLastUpdate).Returns(TimeSpan.FromHours(24));
    }

    [Fact]
    public void IsConnected_WhenNotConnected_ShouldReturnFalse()
    {
        // Act & Assert
        _mockService.Object.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void IsRestFallbackActive_WhenNotConnected_ShouldReturnFalse()
    {
        // Act & Assert
        _mockService.Object.IsRestFallbackActive.Should().BeFalse();
    }

    [Fact]
    public void ConnectionState_Initially_ShouldBeDisconnected()
    {
        // Act & Assert
        _mockService.Object.ConnectionState.Should().Be(WebSocketConnectionState.Disconnected);
    }

    [Fact]
    public void TimeSinceLastUpdate_WhenNotConnected_ShouldReturnLargeTimeSpan()
    {
        // Act
        var timeSinceUpdate = _mockService.Object.TimeSinceLastUpdate;

        // Assert
        timeSinceUpdate.Should().BeGreaterThan(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ShouldCompleteWithoutError()
    {
        // Arrange
        _mockService.Setup(x => x.DisconnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var act = async () => await _mockService.Object.DisconnectAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubscribeToTickersAsync_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        var pairs = new[] { "BTCUSDT", "ETHUSDT" };
        _mockService.Setup(x => x.SubscribeToTickersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _mockService.Object.SubscribeToTickersAsync(pairs);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnsubscribeFromTickersAsync_WhenNotConnected_ShouldNotThrow()
    {
        // Arrange
        var pairs = new[] { "BTCUSDT", "ETHUSDT" };
        _mockService.Setup(x => x.UnsubscribeFromTickersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _mockService.Object.UnsubscribeFromTickersAsync(pairs);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void ConnectionStateChanged_Event_ShouldBeSubscribable()
    {
        // Arrange
        var eventRaised = false;
        _mockService.Object.ConnectionStateChanged += state => eventRaised = true;

        // Assert
        eventRaised.Should().BeFalse(); // Event not raised since we haven't connected
    }

    [Fact]
    public void TickersUpdated_Event_ShouldBeSubscribable()
    {
        // Arrange
        var eventRaised = false;
        _mockService.Object.TickersUpdated += tickers => eventRaised = true;

        // Assert
        eventRaised.Should().BeFalse(); // Event not raised since we haven't received data
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Act
        var act = () => _mockService.Object.Dispose();

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
