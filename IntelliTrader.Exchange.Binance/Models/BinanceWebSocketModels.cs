using System.Text.Json.Serialization;

namespace IntelliTrader.Exchange.Binance.Models
{
    /// <summary>
    /// Represents a ticker update from Binance WebSocket stream.
    /// Maps to the mini ticker stream (stream: !miniTicker@arr).
    /// </summary>
    public class BinanceMiniTickerMessage
    {
        /// <summary>Event type (e.g., "24hrMiniTicker").</summary>
        [JsonPropertyName("e")]
        public string EventType { get; set; } = string.Empty;

        /// <summary>Event time (Unix timestamp ms).</summary>
        [JsonPropertyName("E")]
        public long EventTime { get; set; }

        /// <summary>Symbol (e.g., "BTCUSDT").</summary>
        [JsonPropertyName("s")]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Close price.</summary>
        [JsonPropertyName("c")]
        public string ClosePrice { get; set; } = string.Empty;

        /// <summary>Open price.</summary>
        [JsonPropertyName("o")]
        public string OpenPrice { get; set; } = string.Empty;

        /// <summary>High price.</summary>
        [JsonPropertyName("h")]
        public string HighPrice { get; set; } = string.Empty;

        /// <summary>Low price.</summary>
        [JsonPropertyName("l")]
        public string LowPrice { get; set; } = string.Empty;

        /// <summary>Total traded base asset volume.</summary>
        [JsonPropertyName("v")]
        public string TotalVolume { get; set; } = string.Empty;

        /// <summary>Total traded quote asset volume.</summary>
        [JsonPropertyName("q")]
        public string QuoteVolume { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a full ticker update from Binance WebSocket stream.
    /// Maps to the ticker stream (stream: !ticker@arr).
    /// </summary>
    public class BinanceTickerMessage
    {
        /// <summary>Event type (e.g., "24hrTicker").</summary>
        [JsonPropertyName("e")]
        public string EventType { get; set; } = string.Empty;

        /// <summary>Event time (Unix timestamp ms).</summary>
        [JsonPropertyName("E")]
        public long EventTime { get; set; }

        /// <summary>Symbol (e.g., "BTCUSDT").</summary>
        [JsonPropertyName("s")]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Price change.</summary>
        [JsonPropertyName("p")]
        public string PriceChange { get; set; } = string.Empty;

        /// <summary>Price change percent.</summary>
        [JsonPropertyName("P")]
        public string PriceChangePercent { get; set; } = string.Empty;

        /// <summary>Weighted average price.</summary>
        [JsonPropertyName("w")]
        public string WeightedAveragePrice { get; set; } = string.Empty;

        /// <summary>First trade (F) price.</summary>
        [JsonPropertyName("x")]
        public string PrevClosePrice { get; set; } = string.Empty;

        /// <summary>Last price.</summary>
        [JsonPropertyName("c")]
        public string LastPrice { get; set; } = string.Empty;

        /// <summary>Last quantity.</summary>
        [JsonPropertyName("Q")]
        public string LastQuantity { get; set; } = string.Empty;

        /// <summary>Best bid price.</summary>
        [JsonPropertyName("b")]
        public string BidPrice { get; set; } = string.Empty;

        /// <summary>Best bid quantity.</summary>
        [JsonPropertyName("B")]
        public string BidQuantity { get; set; } = string.Empty;

        /// <summary>Best ask price.</summary>
        [JsonPropertyName("a")]
        public string AskPrice { get; set; } = string.Empty;

        /// <summary>Best ask quantity.</summary>
        [JsonPropertyName("A")]
        public string AskQuantity { get; set; } = string.Empty;

        /// <summary>Open price.</summary>
        [JsonPropertyName("o")]
        public string OpenPrice { get; set; } = string.Empty;

        /// <summary>High price.</summary>
        [JsonPropertyName("h")]
        public string HighPrice { get; set; } = string.Empty;

        /// <summary>Low price.</summary>
        [JsonPropertyName("l")]
        public string LowPrice { get; set; } = string.Empty;

        /// <summary>Total traded base asset volume.</summary>
        [JsonPropertyName("v")]
        public string Volume { get; set; } = string.Empty;

        /// <summary>Total traded quote asset volume.</summary>
        [JsonPropertyName("q")]
        public string QuoteVolume { get; set; } = string.Empty;

        /// <summary>Statistics open time.</summary>
        [JsonPropertyName("O")]
        public long OpenTime { get; set; }

        /// <summary>Statistics close time.</summary>
        [JsonPropertyName("C")]
        public long CloseTime { get; set; }

        /// <summary>First trade ID.</summary>
        [JsonPropertyName("F")]
        public long FirstTradeId { get; set; }

        /// <summary>Last trade ID.</summary>
        [JsonPropertyName("L")]
        public long LastTradeId { get; set; }

        /// <summary>Total number of trades.</summary>
        [JsonPropertyName("n")]
        public long TradeCount { get; set; }
    }

    /// <summary>
    /// Represents a book ticker update from Binance WebSocket stream.
    /// Maps to the book ticker stream (stream: !bookTicker).
    /// </summary>
    public class BinanceBookTickerMessage
    {
        /// <summary>Order book update ID.</summary>
        [JsonPropertyName("u")]
        public long UpdateId { get; set; }

        /// <summary>Symbol.</summary>
        [JsonPropertyName("s")]
        public string Symbol { get; set; } = string.Empty;

        /// <summary>Best bid price.</summary>
        [JsonPropertyName("b")]
        public string BidPrice { get; set; } = string.Empty;

        /// <summary>Best bid quantity.</summary>
        [JsonPropertyName("B")]
        public string BidQuantity { get; set; } = string.Empty;

        /// <summary>Best ask price.</summary>
        [JsonPropertyName("a")]
        public string AskPrice { get; set; } = string.Empty;

        /// <summary>Best ask quantity.</summary>
        [JsonPropertyName("A")]
        public string AskQuantity { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a WebSocket subscription request message.
    /// </summary>
    public class BinanceSubscriptionRequest
    {
        /// <summary>Request method (SUBSCRIBE or UNSUBSCRIBE).</summary>
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        /// <summary>List of streams to subscribe to.</summary>
        [JsonPropertyName("params")]
        public string[] Params { get; set; } = [];

        /// <summary>Unique request ID.</summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    /// <summary>
    /// Represents a WebSocket subscription response message.
    /// </summary>
    public class BinanceSubscriptionResponse
    {
        /// <summary>Result (null if successful).</summary>
        [JsonPropertyName("result")]
        public object? Result { get; set; }

        /// <summary>Request ID that this response corresponds to.</summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    /// <summary>
    /// Represents an error response from Binance WebSocket.
    /// </summary>
    public class BinanceErrorResponse
    {
        /// <summary>Error code.</summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>Error message.</summary>
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Wrapper for combined stream data.
    /// </summary>
    public class BinanceCombinedStreamMessage<T>
    {
        /// <summary>Stream name.</summary>
        [JsonPropertyName("stream")]
        public string Stream { get; set; } = string.Empty;

        /// <summary>Data payload.</summary>
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }
}
