namespace IntelliTrader.Core
{
    /// <summary>
    /// Standardized property names for structured logging.
    /// Use these constants when adding contextual properties to log entries
    /// to ensure consistency across the codebase.
    /// </summary>
    public static class LogProperties
    {
        /// <summary>Unique identifier correlating all log entries within an operation</summary>
        public const string CorrelationId = "CorrelationId";

        /// <summary>Trading pair symbol (e.g., "BTCUSDT")</summary>
        public const string Pair = "Pair";

        /// <summary>Name of the operation being performed (e.g., "BuyOrder", "SignalEvaluation")</summary>
        public const string Operation = "Operation";

        /// <summary>Duration of an operation in milliseconds</summary>
        public const string Duration = "DurationMs";

        /// <summary>Name of the service emitting the log entry</summary>
        public const string ServiceName = "ServiceName";

        /// <summary>Signal name associated with the log entry</summary>
        public const string Signal = "Signal";

        /// <summary>Order side: Buy or Sell</summary>
        public const string OrderSide = "OrderSide";

        /// <summary>Order type: Market, Limit, etc.</summary>
        public const string OrderType = "OrderType";

        /// <summary>Trading amount or quantity</summary>
        public const string Amount = "Amount";

        /// <summary>Price at which an operation occurred</summary>
        public const string Price = "Price";

        /// <summary>Rule name that triggered an action</summary>
        public const string Rule = "Rule";

        /// <summary>Whether the operation succeeded</summary>
        public const string Success = "Success";

        /// <summary>Error code for failed operations</summary>
        public const string ErrorCode = "ErrorCode";
    }
}
