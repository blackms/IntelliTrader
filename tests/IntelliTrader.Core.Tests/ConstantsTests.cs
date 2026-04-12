using FluentAssertions;
using Xunit;
using IntelliTrader.Core;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for Constants class values to ensure they are defined correctly
/// and guard against accidental changes to critical configuration constants.
/// </summary>
public class ConstantsTests
{
    #region ServiceNames Tests

    [Fact]
    public void ServiceNames_CoreService_HasExpectedValue()
    {
        Constants.ServiceNames.CoreService.Should().Be("Core");
    }

    [Fact]
    public void ServiceNames_LoggingService_HasExpectedValue()
    {
        Constants.ServiceNames.LoggingService.Should().Be("Logging");
    }

    [Fact]
    public void ServiceNames_TradingService_HasExpectedValue()
    {
        Constants.ServiceNames.TradingService.Should().Be("Trading");
    }

    [Fact]
    public void ServiceNames_NotificationService_HasExpectedValue()
    {
        Constants.ServiceNames.NotificationService.Should().Be("Notification");
    }

    [Fact]
    public void ServiceNames_CachingService_HasExpectedValue()
    {
        Constants.ServiceNames.CachingService.Should().Be("Caching");
    }

    [Fact]
    public void ServiceNames_AlertingService_HasExpectedValue()
    {
        Constants.ServiceNames.AlertingService.Should().Be("Alerting");
    }

    #endregion

    #region HealthChecks Tests

    [Fact]
    public void HealthChecks_AllConstantsAreNonEmpty()
    {
        Constants.HealthChecks.AccountRefreshed.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.TickersUpdated.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.TradingPairsProcessed.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.TradingViewCryptoSignalsReceived.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.SignalRulesProcessed.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.TradingRulesProcessed.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.BacktestingSignalsSnapshotTaken.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.BacktestingTickersSnapshotTaken.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.BacktestingSignalsSnapshotLoaded.Should().NotBeNullOrEmpty();
        Constants.HealthChecks.BacktestingTickersSnapshotLoaded.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void HealthChecks_AllConstantsAreUnique()
    {
        var values = new[]
        {
            Constants.HealthChecks.AccountRefreshed,
            Constants.HealthChecks.TickersUpdated,
            Constants.HealthChecks.TradingPairsProcessed,
            Constants.HealthChecks.TradingViewCryptoSignalsReceived,
            Constants.HealthChecks.SignalRulesProcessed,
            Constants.HealthChecks.TradingRulesProcessed,
            Constants.HealthChecks.BacktestingSignalsSnapshotTaken,
            Constants.HealthChecks.BacktestingTickersSnapshotTaken,
            Constants.HealthChecks.BacktestingSignalsSnapshotLoaded,
            Constants.HealthChecks.BacktestingTickersSnapshotLoaded
        };

        values.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Timeouts Tests

    [Fact]
    public void Timeouts_StartupDelayMs_IsPositive()
    {
        Constants.Timeouts.StartupDelayMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Timeouts_RestartTimeoutSeconds_IsPositive()
    {
        Constants.Timeouts.RestartTimeoutSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Timeouts_HttpRequestTimeoutSeconds_IsPositive()
    {
        Constants.Timeouts.HttpRequestTimeoutSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Timeouts_SocketDisconnectTimeoutSeconds_IsPositive()
    {
        Constants.Timeouts.SocketDisconnectTimeoutSeconds.Should().BeGreaterThan(0);
    }

    #endregion

    #region TimedTasks Tests

    [Fact]
    public void TimedTasks_StandardDelay_IsPositive()
    {
        Constants.TimedTasks.StandardDelay.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TimedTasks_DefaultIntervalMs_IsPositive()
    {
        Constants.TimedTasks.DefaultIntervalMs.Should().BeGreaterThan(0);
    }

    #endregion

    #region RetryPolicy Tests

    [Fact]
    public void RetryPolicy_MaxRetryAttempts_IsPositive()
    {
        Constants.RetryPolicy.MaxRetryAttempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RetryPolicy_InitialRetryDelayMs_IsPositive()
    {
        Constants.RetryPolicy.InitialRetryDelayMs.Should().BeGreaterThan(0);
    }

    #endregion

    #region Trading Tests

    [Fact]
    public void Trading_DefaultMaxOrderHistorySize_IsWithinBounds()
    {
        Constants.Trading.DefaultMaxOrderHistorySize
            .Should().BeGreaterThanOrEqualTo(Constants.Trading.MinOrderHistorySize)
            .And.BeLessThanOrEqualTo(Constants.Trading.MaxOrderHistorySize);
    }

    [Fact]
    public void Trading_MinOrderHistorySize_IsLessThanMax()
    {
        Constants.Trading.MinOrderHistorySize.Should().BeLessThan(Constants.Trading.MaxOrderHistorySize);
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public void Resilience_OrderMaxRetryAttempts_IsMinimal()
    {
        // Orders should have minimal retries to prevent duplicate orders
        Constants.Resilience.DefaultOrderMaxRetryAttempts.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Resilience_CircuitBreakerFailureRatio_IsWithinValidRange()
    {
        Constants.Resilience.DefaultCircuitBreakerFailureRatio.Should().BeGreaterThan(0.0).And.BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void Resilience_OrderCircuitBreakerFailureRatio_IsMoreSensitiveThanRead()
    {
        Constants.Resilience.DefaultOrderCircuitBreakerFailureRatio
            .Should().BeLessThanOrEqualTo(Constants.Resilience.DefaultCircuitBreakerFailureRatio);
    }

    [Fact]
    public void Resilience_RateLimitPermitsPerMinute_IsBelowBinanceLimit()
    {
        // Binance limit is 1200/min, we should be below that
        Constants.Resilience.DefaultRateLimitPermitsPerMinute.Should().BeLessThan(1200);
    }

    #endregion

    #region WebSocket Tests

    [Fact]
    public void WebSocket_BinanceStreamUrl_IsValidWssUrl()
    {
        Constants.WebSocket.BinanceStreamUrl.Should().StartWith("wss://");
    }

    [Fact]
    public void WebSocket_ReceiveBufferSize_IsPositive()
    {
        Constants.WebSocket.ReceiveBufferSize.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WebSocket_MaxMessageSize_IsGreaterThanReceiveBuffer()
    {
        Constants.WebSocket.MaxMessageSize.Should().BeGreaterThanOrEqualTo(Constants.WebSocket.ReceiveBufferSize);
    }

    #endregion
}
