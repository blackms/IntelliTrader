using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IntelliTrader.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when there is a connection issue with the exchange.
    /// </summary>
    [Serializable]
    public class ExchangeConnectionException : IntelliTraderException
    {
        private const string ErrorCodeValue = "EXCHANGE_CONNECTION_ERROR";

        /// <summary>
        /// The name of the exchange.
        /// </summary>
        public string ExchangeName { get; }

        /// <summary>
        /// The type of connection operation that failed.
        /// </summary>
        public ExchangeConnectionErrorType ConnectionErrorType { get; }

        /// <summary>
        /// Whether this is a recoverable error that may succeed on retry.
        /// </summary>
        public bool IsRecoverable { get; }

        public ExchangeConnectionException(string exchangeName, string message)
            : this(exchangeName, ExchangeConnectionErrorType.Unknown, true, message, null)
        {
        }

        public ExchangeConnectionException(string exchangeName, string message, Exception innerException)
            : this(exchangeName, ExchangeConnectionErrorType.Unknown, true, message, innerException)
        {
        }

        public ExchangeConnectionException(
            string exchangeName,
            ExchangeConnectionErrorType errorType,
            bool isRecoverable,
            string message,
            Exception? innerException = null)
            : base(
                CreateMessage(exchangeName, errorType, message),
                ErrorCodeValue,
                CreateContext(exchangeName, errorType, isRecoverable),
                innerException)
        {
            ExchangeName = exchangeName;
            ConnectionErrorType = errorType;
            IsRecoverable = isRecoverable;
        }

        protected ExchangeConnectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ExchangeName = info.GetString(nameof(ExchangeName)) ?? "UNKNOWN";
            ConnectionErrorType = (ExchangeConnectionErrorType)info.GetInt32(nameof(ConnectionErrorType));
            IsRecoverable = info.GetBoolean(nameof(IsRecoverable));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ExchangeName), ExchangeName);
            info.AddValue(nameof(ConnectionErrorType), (int)ConnectionErrorType);
            info.AddValue(nameof(IsRecoverable), IsRecoverable);
        }

        private static string CreateMessage(string exchangeName, ExchangeConnectionErrorType errorType, string details)
        {
            return errorType switch
            {
                ExchangeConnectionErrorType.WebSocketDisconnected => $"WebSocket disconnected from {exchangeName}: {details}",
                ExchangeConnectionErrorType.ApiTimeout => $"API timeout when connecting to {exchangeName}: {details}",
                ExchangeConnectionErrorType.RateLimited => $"Rate limited by {exchangeName}: {details}",
                ExchangeConnectionErrorType.AuthenticationFailed => $"Authentication failed for {exchangeName}: {details}",
                ExchangeConnectionErrorType.ServiceUnavailable => $"{exchangeName} service unavailable: {details}",
                _ => $"Connection error with {exchangeName}: {details}"
            };
        }

        private static Dictionary<string, object> CreateContext(
            string exchangeName,
            ExchangeConnectionErrorType errorType,
            bool isRecoverable)
        {
            return new Dictionary<string, object>
            {
                ["ExchangeName"] = exchangeName,
                ["ConnectionErrorType"] = errorType.ToString(),
                ["IsRecoverable"] = isRecoverable
            };
        }
    }

    /// <summary>
    /// Types of exchange connection errors.
    /// </summary>
    public enum ExchangeConnectionErrorType
    {
        Unknown = 0,
        WebSocketDisconnected = 1,
        ApiTimeout = 2,
        RateLimited = 3,
        AuthenticationFailed = 4,
        ServiceUnavailable = 5
    }
}
