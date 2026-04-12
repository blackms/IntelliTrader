using FluentAssertions;
using Xunit;
using IntelliTrader.Core;

namespace IntelliTrader.Core.Tests;

/// <summary>
/// Tests for LogProperties constants to ensure consistent structured logging property names.
/// </summary>
public class LogPropertiesTests
{
    [Fact]
    public void CorrelationId_HasExpectedValue()
    {
        LogProperties.CorrelationId.Should().Be("CorrelationId");
    }

    [Fact]
    public void Operation_HasExpectedValue()
    {
        LogProperties.Operation.Should().Be("Operation");
    }

    [Fact]
    public void Duration_HasExpectedValue()
    {
        LogProperties.Duration.Should().Be("DurationMs");
    }

    [Fact]
    public void AllProperties_AreNonEmpty()
    {
        LogProperties.CorrelationId.Should().NotBeNullOrEmpty();
        LogProperties.Pair.Should().NotBeNullOrEmpty();
        LogProperties.Operation.Should().NotBeNullOrEmpty();
        LogProperties.Duration.Should().NotBeNullOrEmpty();
        LogProperties.ServiceName.Should().NotBeNullOrEmpty();
        LogProperties.Signal.Should().NotBeNullOrEmpty();
        LogProperties.OrderSide.Should().NotBeNullOrEmpty();
        LogProperties.OrderType.Should().NotBeNullOrEmpty();
        LogProperties.Amount.Should().NotBeNullOrEmpty();
        LogProperties.Price.Should().NotBeNullOrEmpty();
        LogProperties.Rule.Should().NotBeNullOrEmpty();
        LogProperties.Success.Should().NotBeNullOrEmpty();
        LogProperties.ErrorCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AllProperties_AreUnique()
    {
        var values = new[]
        {
            LogProperties.CorrelationId,
            LogProperties.Pair,
            LogProperties.Operation,
            LogProperties.Duration,
            LogProperties.ServiceName,
            LogProperties.Signal,
            LogProperties.OrderSide,
            LogProperties.OrderType,
            LogProperties.Amount,
            LogProperties.Price,
            LogProperties.Rule,
            LogProperties.Success,
            LogProperties.ErrorCode
        };

        values.Should().OnlyHaveUniqueItems();
    }
}
