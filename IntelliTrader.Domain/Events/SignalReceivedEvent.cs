using IntelliTrader.Domain.SharedKernel;

namespace IntelliTrader.Domain.Events;

/// <summary>
/// Raised when a new trading signal is received.
/// </summary>
public sealed record SignalReceivedEvent : IDomainEvent
{
    /// <inheritdoc />
    public Guid EventId { get; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; }

    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>
    /// The name of the signal (e.g., "TradingView-BTC-15m").
    /// </summary>
    public string SignalName { get; }

    /// <summary>
    /// The trading pair this signal is for.
    /// </summary>
    public string Pair { get; }

    /// <summary>
    /// The signal rating (-1 to 1, where positive = buy, negative = sell).
    /// </summary>
    public double Rating { get; }

    /// <summary>
    /// The source of the signal (e.g., "TradingView", "Custom").
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The previous rating for this signal, if any.
    /// </summary>
    public double? PreviousRating { get; }

    /// <summary>
    /// The current price at the time of the signal.
    /// </summary>
    public decimal? Price { get; }

    /// <summary>
    /// The price change percentage at the time of the signal.
    /// </summary>
    public decimal? PriceChange { get; }

    /// <summary>
    /// The volume at the time of the signal.
    /// </summary>
    public long? Volume { get; }

    /// <summary>
    /// The volatility metric at the time of the signal.
    /// </summary>
    public double? Volatility { get; }

    public SignalReceivedEvent(
        string signalName,
        string pair,
        double rating,
        string source,
        double? previousRating = null,
        decimal? price = null,
        decimal? priceChange = null,
        long? volume = null,
        double? volatility = null,
        string? correlationId = null)
    {
        EventId = Guid.NewGuid();
        OccurredAt = DateTimeOffset.UtcNow;
        CorrelationId = correlationId;
        SignalName = signalName;
        Pair = pair;
        Rating = rating;
        Source = source;
        PreviousRating = previousRating;
        Price = price;
        PriceChange = priceChange;
        Volume = volume;
        Volatility = volatility;
    }
}
