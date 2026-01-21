using System;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Represents a price candle (OHLC) for technical analysis calculations.
    /// </summary>
    public class Candle
    {
        /// <summary>
        /// The trading pair symbol (e.g., "BTCUSDT").
        /// </summary>
        public string Pair { get; set; } = string.Empty;

        /// <summary>
        /// The opening price of the candle.
        /// </summary>
        public decimal Open { get; set; }

        /// <summary>
        /// The highest price during the candle period.
        /// </summary>
        public decimal High { get; set; }

        /// <summary>
        /// The lowest price during the candle period.
        /// </summary>
        public decimal Low { get; set; }

        /// <summary>
        /// The closing price of the candle.
        /// </summary>
        public decimal Close { get; set; }

        /// <summary>
        /// The trading volume during the candle period.
        /// </summary>
        public decimal Volume { get; set; }

        /// <summary>
        /// The timestamp when the candle opened.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Calculates the True Range for this candle.
        /// True Range = max(High - Low, |High - PreviousClose|, |Low - PreviousClose|)
        /// </summary>
        /// <param name="previousClose">The closing price of the previous candle.</param>
        /// <returns>The True Range value.</returns>
        public decimal CalculateTrueRange(decimal previousClose)
        {
            var highLow = High - Low;
            var highPrevClose = Math.Abs(High - previousClose);
            var lowPrevClose = Math.Abs(Low - previousClose);

            return Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
        }

        /// <summary>
        /// Calculates the True Range using only this candle's data (for first candle).
        /// </summary>
        /// <returns>The True Range value (High - Low).</returns>
        public decimal CalculateTrueRange()
        {
            return High - Low;
        }
    }
}
