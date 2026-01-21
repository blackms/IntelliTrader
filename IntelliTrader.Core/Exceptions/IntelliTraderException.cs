using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace IntelliTrader.Core.Exceptions
{
    /// <summary>
    /// Base exception for all IntelliTrader domain exceptions.
    /// Provides structured error information for logging and diagnostics.
    /// </summary>
    [Serializable]
    public class IntelliTraderException : Exception
    {
        /// <summary>
        /// A unique error code for categorizing the exception.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Additional context data for logging and diagnostics.
        /// </summary>
        public IReadOnlyDictionary<string, object> Context { get; }

        public IntelliTraderException()
            : this("An error occurred in IntelliTrader")
        {
        }

        public IntelliTraderException(string message)
            : this(message, "INTELLITRADER_ERROR")
        {
        }

        public IntelliTraderException(string message, string errorCode)
            : this(message, errorCode, null, null)
        {
        }

        public IntelliTraderException(string message, Exception innerException)
            : this(message, "INTELLITRADER_ERROR", null, innerException)
        {
        }

        public IntelliTraderException(string message, string errorCode, IDictionary<string, object>? context)
            : this(message, errorCode, context, null)
        {
        }

        public IntelliTraderException(string message, string errorCode, IDictionary<string, object>? context, Exception? innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode ?? "INTELLITRADER_ERROR";
            Context = context != null
                ? new Dictionary<string, object>(context)
                : new Dictionary<string, object>();
        }

        protected IntelliTraderException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ErrorCode = info.GetString(nameof(ErrorCode)) ?? "INTELLITRADER_ERROR";
            Context = new Dictionary<string, object>();
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ErrorCode), ErrorCode);
        }

        /// <summary>
        /// Creates a formatted message including error code and context.
        /// </summary>
        public string GetDetailedMessage()
        {
            var details = $"[{ErrorCode}] {Message}";
            if (Context.Count > 0)
            {
                var contextPairs = string.Join(", ", Context.Select(kv => $"{kv.Key}={kv.Value}"));
                details += $" | Context: {contextPairs}";
            }
            return details;
        }
    }
}
