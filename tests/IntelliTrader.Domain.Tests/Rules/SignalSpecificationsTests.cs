using IntelliTrader.Domain.Rules;
using IntelliTrader.Domain.Rules.Specifications;

namespace IntelliTrader.Domain.Tests.Rules;

public class SignalSpecificationsTests
{
    #region Price Specifications

    [Theory]
    [InlineData(100.0, 150.0, true)]
    [InlineData(100.0, 100.0, true)]
    [InlineData(100.0, 50.0, false)]
    public void MinPriceSpecification_EvaluatesCorrectly(decimal minPrice, decimal actualPrice, bool expected)
    {
        // Arrange
        var spec = new MinPriceSpecification("TV-15mins", minPrice);
        var context = CreateContextWithSignal("TV-15mins", price: actualPrice);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(200.0, 150.0, true)]
    [InlineData(200.0, 200.0, true)]
    [InlineData(200.0, 250.0, false)]
    public void MaxPriceSpecification_EvaluatesCorrectly(decimal maxPrice, decimal actualPrice, bool expected)
    {
        // Arrange
        var spec = new MaxPriceSpecification("TV-15mins", maxPrice);
        var context = CreateContextWithSignal("TV-15mins", price: actualPrice);

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(5.0, 10.0, true)]
    [InlineData(5.0, 5.0, true)]
    [InlineData(5.0, 2.0, false)]
    public void MinPriceChangeSpecification_EvaluatesCorrectly(decimal minPriceChange, decimal actualPriceChange, bool expected)
    {
        // Arrange
        var spec = new MinPriceChangeSpecification("TV-15mins", minPriceChange);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", PriceChange = actualPriceChange }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Volume Change Specifications

    [Theory]
    [InlineData(10.0, 15.0, true)]
    [InlineData(10.0, 10.0, true)]
    [InlineData(10.0, 5.0, false)]
    public void MinVolumeChangeSpecification_EvaluatesCorrectly(double minVolumeChange, double actualVolumeChange, bool expected)
    {
        // Arrange
        var spec = new MinVolumeChangeSpecification("TV-15mins", minVolumeChange);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", VolumeChange = actualVolumeChange }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(20.0, 15.0, true)]
    [InlineData(20.0, 20.0, true)]
    [InlineData(20.0, 25.0, false)]
    public void MaxVolumeChangeSpecification_EvaluatesCorrectly(double maxVolumeChange, double actualVolumeChange, bool expected)
    {
        // Arrange
        var spec = new MaxVolumeChangeSpecification("TV-15mins", maxVolumeChange);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", VolumeChange = actualVolumeChange }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Rating Change Specifications

    [Theory]
    [InlineData(0.1, 0.2, true)]
    [InlineData(0.1, 0.1, true)]
    [InlineData(0.1, 0.05, false)]
    public void MinRatingChangeSpecification_EvaluatesCorrectly(double minRatingChange, double actualRatingChange, bool expected)
    {
        // Arrange
        var spec = new MinRatingChangeSpecification("TV-15mins", minRatingChange);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", RatingChange = actualRatingChange }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.3, 0.2, true)]
    [InlineData(0.3, 0.3, true)]
    [InlineData(0.3, 0.4, false)]
    public void MaxRatingChangeSpecification_EvaluatesCorrectly(double maxRatingChange, double actualRatingChange, bool expected)
    {
        // Arrange
        var spec = new MaxRatingChangeSpecification("TV-15mins", maxRatingChange);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", RatingChange = actualRatingChange }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Volatility Specifications

    [Theory]
    [InlineData(0.5, 0.8, true)]
    [InlineData(0.5, 0.5, true)]
    [InlineData(0.5, 0.3, false)]
    public void MinVolatilitySpecification_EvaluatesCorrectly(double minVolatility, double actualVolatility, bool expected)
    {
        // Arrange
        var spec = new MinVolatilitySpecification("TV-15mins", minVolatility);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", Volatility = actualVolatility }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    [Theory]
    [InlineData(1.0, 0.8, true)]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.0, 1.2, false)]
    public void MaxVolatilitySpecification_EvaluatesCorrectly(double maxVolatility, double actualVolatility, bool expected)
    {
        // Arrange
        var spec = new MaxVolatilitySpecification("TV-15mins", maxVolatility);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", Volatility = actualVolatility }
            }
        };

        // Act & Assert
        spec.IsSatisfiedBy(context).Should().Be(expected);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SignalSpecification_WithNullSignalName_ReturnsFalse()
    {
        // Arrange
        var spec = new MinRatingSpecification(null, 0.3);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", Rating = 0.5 }
            }
        };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void SignalSpecification_WithWrongSignalName_ReturnsFalse()
    {
        // Arrange
        var spec = new MinRatingSpecification("TV-60mins", 0.3);
        var context = new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                ["TV-15mins"] = new SignalSnapshot { Name = "TV-15mins", Rating = 0.5 }
            }
        };

        // Act
        var result = spec.IsSatisfiedBy(context);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helpers

    private static RuleEvaluationContext CreateContextWithSignal(string signalName, decimal? price = null)
    {
        return new RuleEvaluationContext
        {
            Signals = new Dictionary<string, SignalSnapshot>
            {
                [signalName] = new SignalSnapshot { Name = signalName, Price = price }
            }
        };
    }

    #endregion
}
