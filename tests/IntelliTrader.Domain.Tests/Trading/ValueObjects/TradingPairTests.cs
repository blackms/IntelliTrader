using IntelliTrader.Domain.Trading.ValueObjects;

namespace IntelliTrader.Domain.Tests.Trading.ValueObjects;

public class TradingPairTests
{
    #region Create from Symbol and Market

    [Theory]
    [InlineData("BTCUSDT", "USDT", "BTC", "USDT")]
    [InlineData("ETHBTC", "BTC", "ETH", "BTC")]
    [InlineData("btcusdt", "usdt", "BTC", "USDT")]
    [InlineData("SOLUSDT", "USDT", "SOL", "USDT")]
    public void Create_WithValidSymbolAndMarket_CreatesPair(string symbol, string market, string expectedBase, string expectedQuote)
    {
        // Act
        var pair = TradingPair.Create(symbol, market);

        // Assert
        pair.Symbol.Should().Be(symbol.ToUpperInvariant());
        pair.BaseCurrency.Should().Be(expectedBase);
        pair.QuoteCurrency.Should().Be(expectedQuote);
    }

    [Theory]
    [InlineData("", "USDT")]
    [InlineData(null, "USDT")]
    [InlineData("  ", "USDT")]
    public void Create_WithInvalidSymbol_ThrowsArgumentException(string? symbol, string market)
    {
        // Act
        var act = () => TradingPair.Create(symbol!, market);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("symbol");
    }

    [Theory]
    [InlineData("BTCUSDT", "")]
    [InlineData("BTCUSDT", null)]
    [InlineData("BTCUSDT", "  ")]
    public void Create_WithInvalidMarket_ThrowsArgumentException(string symbol, string? market)
    {
        // Act
        var act = () => TradingPair.Create(symbol, market!);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("market");
    }

    [Fact]
    public void Create_WhenSymbolDoesNotEndWithMarket_ThrowsArgumentException()
    {
        // Act
        var act = () => TradingPair.Create("BTCUSDT", "BTC");

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("symbol");
    }

    [Fact]
    public void Create_WhenBaseCurrencyWouldBeEmpty_ThrowsArgumentException()
    {
        // Act
        var act = () => TradingPair.Create("USDT", "USDT");

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("symbol");
    }

    #endregion

    #region Create from Currencies

    [Theory]
    [InlineData("BTC", "USDT", "BTCUSDT")]
    [InlineData("eth", "btc", "ETHBTC")]
    public void FromCurrencies_WithValidCurrencies_CreatesPair(string baseCurrency, string quoteCurrency, string expectedSymbol)
    {
        // Act
        var pair = TradingPair.FromCurrencies(baseCurrency, quoteCurrency);

        // Assert
        pair.Symbol.Should().Be(expectedSymbol);
        pair.BaseCurrency.Should().Be(baseCurrency.ToUpperInvariant());
        pair.QuoteCurrency.Should().Be(quoteCurrency.ToUpperInvariant());
    }

    [Theory]
    [InlineData("", "USDT")]
    [InlineData(null, "USDT")]
    public void FromCurrencies_WithInvalidBaseCurrency_ThrowsArgumentException(string? baseCurrency, string quoteCurrency)
    {
        // Act
        var act = () => TradingPair.FromCurrencies(baseCurrency!, quoteCurrency);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("baseCurrency");
    }

    [Theory]
    [InlineData("BTC", "")]
    [InlineData("BTC", null)]
    public void FromCurrencies_WithInvalidQuoteCurrency_ThrowsArgumentException(string baseCurrency, string? quoteCurrency)
    {
        // Act
        var act = () => TradingPair.FromCurrencies(baseCurrency, quoteCurrency!);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("quoteCurrency");
    }

    #endregion

    #region IsInMarket

    [Theory]
    [InlineData("BTCUSDT", "USDT", true)]
    [InlineData("BTCUSDT", "usdt", true)]
    [InlineData("BTCUSDT", "BTC", false)]
    [InlineData("ETHBTC", "BTC", true)]
    [InlineData("ETHBTC", "USDT", false)]
    public void IsInMarket_ReturnsCorrectResult(string symbol, string market, bool expected)
    {
        // Arrange
        var pair = TradingPair.Create(symbol, symbol.EndsWith("USDT") ? "USDT" : "BTC");

        // Act
        var result = pair.IsInMarket(market);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    public void IsInMarket_WithInvalidMarket_ReturnsFalse(string? market)
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act
        var result = pair.IsInMarket(market!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_WithSameSymbol_ReturnsTrue()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("BTCUSDT", "USDT");

        // Assert
        pair1.Should().Be(pair2);
        (pair1 == pair2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentSymbol_ReturnsFalse()
    {
        // Arrange
        var pair1 = TradingPair.Create("BTCUSDT", "USDT");
        var pair2 = TradingPair.Create("ETHUSDT", "USDT");

        // Assert
        pair1.Should().NotBe(pair2);
        (pair1 != pair2).Should().BeTrue();
    }

    #endregion

    #region ToString and Implicit Conversion

    [Fact]
    public void ToString_ReturnsSymbol()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act & Assert
        pair.ToString().Should().Be("BTCUSDT");
    }

    [Fact]
    public void ImplicitConversion_ToString_ReturnsSymbol()
    {
        // Arrange
        var pair = TradingPair.Create("BTCUSDT", "USDT");

        // Act
        string symbol = pair;

        // Assert
        symbol.Should().Be("BTCUSDT");
    }

    #endregion
}
