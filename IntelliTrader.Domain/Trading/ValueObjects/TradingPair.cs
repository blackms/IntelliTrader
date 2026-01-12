using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Trading.ValueObjects;

/// <summary>
/// Represents a trading pair (e.g., BTCUSDT) with base and quote currencies.
/// </summary>
public sealed class TradingPair : ValueObject
{
    /// <summary>
    /// The full symbol (e.g., "BTCUSDT")
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// The base currency (e.g., "BTC" in BTCUSDT)
    /// </summary>
    public string BaseCurrency { get; }

    /// <summary>
    /// The quote/market currency (e.g., "USDT" in BTCUSDT)
    /// </summary>
    public string QuoteCurrency { get; }

    private TradingPair(string symbol, string baseCurrency, string quoteCurrency)
    {
        Symbol = symbol;
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
    }

    /// <summary>
    /// Creates a TradingPair from a full symbol and market.
    /// </summary>
    /// <param name="symbol">The full trading pair symbol (e.g., "BTCUSDT")</param>
    /// <param name="market">The market/quote currency (e.g., "USDT")</param>
    public static TradingPair Create(string symbol, string market)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol cannot be null or empty", nameof(symbol));

        if (string.IsNullOrWhiteSpace(market))
            throw new ArgumentException("Market cannot be null or empty", nameof(market));

        symbol = symbol.ToUpperInvariant();
        market = market.ToUpperInvariant();

        if (!symbol.EndsWith(market))
            throw new ArgumentException($"Symbol '{symbol}' does not end with market '{market}'", nameof(symbol));

        var baseCurrency = symbol[..^market.Length];

        if (string.IsNullOrEmpty(baseCurrency))
            throw new ArgumentException("Base currency cannot be empty", nameof(symbol));

        return new TradingPair(symbol, baseCurrency, market);
    }

    /// <summary>
    /// Creates a TradingPair from base and quote currencies.
    /// </summary>
    public static TradingPair FromCurrencies(string baseCurrency, string quoteCurrency)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency))
            throw new ArgumentException("Base currency cannot be null or empty", nameof(baseCurrency));

        if (string.IsNullOrWhiteSpace(quoteCurrency))
            throw new ArgumentException("Quote currency cannot be null or empty", nameof(quoteCurrency));

        baseCurrency = baseCurrency.ToUpperInvariant();
        quoteCurrency = quoteCurrency.ToUpperInvariant();

        return new TradingPair($"{baseCurrency}{quoteCurrency}", baseCurrency, quoteCurrency);
    }

    /// <summary>
    /// Checks if this pair belongs to the specified market.
    /// </summary>
    public bool IsInMarket(string market)
    {
        if (string.IsNullOrWhiteSpace(market))
            return false;

        return QuoteCurrency.Equals(market.ToUpperInvariant(), StringComparison.Ordinal);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Symbol;
    }

    public override string ToString() => Symbol;

    public static implicit operator string(TradingPair pair) => pair.Symbol;
}
