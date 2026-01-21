using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IntelliTrader.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when an order execution fails.
    /// </summary>
    [Serializable]
    public class OrderExecutionException : IntelliTraderException
    {
        private const string ErrorCodeValue = "ORDER_EXECUTION_FAILED";

        /// <summary>
        /// The trading pair for the failed order.
        /// </summary>
        public string Pair { get; }

        /// <summary>
        /// The type of order that failed (Buy/Sell).
        /// </summary>
        public string OrderSide { get; }

        /// <summary>
        /// The order type (Market/Limit).
        /// </summary>
        public string? OrderType { get; }

        /// <summary>
        /// The requested amount.
        /// </summary>
        public decimal? Amount { get; }

        /// <summary>
        /// The requested price (for limit orders).
        /// </summary>
        public decimal? Price { get; }

        /// <summary>
        /// The order ID if available.
        /// </summary>
        public string? OrderId { get; }

        /// <summary>
        /// The exchange error code if available.
        /// </summary>
        public string? ExchangeErrorCode { get; }

        public OrderExecutionException(string pair, string orderSide, string message)
            : this(pair, orderSide, null, null, null, null, null, message, null)
        {
        }

        public OrderExecutionException(string pair, string orderSide, string message, Exception innerException)
            : this(pair, orderSide, null, null, null, null, null, message, innerException)
        {
        }

        public OrderExecutionException(
            string pair,
            string orderSide,
            string? orderType,
            decimal? amount,
            decimal? price,
            string? orderId,
            string? exchangeErrorCode,
            string message,
            Exception? innerException = null)
            : base(
                CreateMessage(pair, orderSide, orderType, message),
                ErrorCodeValue,
                CreateContext(pair, orderSide, orderType, amount, price, orderId, exchangeErrorCode),
                innerException)
        {
            Pair = pair;
            OrderSide = orderSide;
            OrderType = orderType;
            Amount = amount;
            Price = price;
            OrderId = orderId;
            ExchangeErrorCode = exchangeErrorCode;
        }

        protected OrderExecutionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Pair = info.GetString(nameof(Pair)) ?? "UNKNOWN";
            OrderSide = info.GetString(nameof(OrderSide)) ?? "UNKNOWN";
            OrderType = info.GetString(nameof(OrderType));
            Amount = (decimal?)info.GetValue(nameof(Amount), typeof(decimal?));
            Price = (decimal?)info.GetValue(nameof(Price), typeof(decimal?));
            OrderId = info.GetString(nameof(OrderId));
            ExchangeErrorCode = info.GetString(nameof(ExchangeErrorCode));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Pair), Pair);
            info.AddValue(nameof(OrderSide), OrderSide);
            info.AddValue(nameof(OrderType), OrderType);
            info.AddValue(nameof(Amount), Amount);
            info.AddValue(nameof(Price), Price);
            info.AddValue(nameof(OrderId), OrderId);
            info.AddValue(nameof(ExchangeErrorCode), ExchangeErrorCode);
        }

        private static string CreateMessage(string pair, string orderSide, string? orderType, string details)
        {
            var message = $"Failed to execute {orderSide} order for {pair}";
            if (!string.IsNullOrEmpty(orderType))
            {
                message = $"Failed to execute {orderType} {orderSide} order for {pair}";
            }
            return $"{message}: {details}";
        }

        private static Dictionary<string, object> CreateContext(
            string pair,
            string orderSide,
            string? orderType,
            decimal? amount,
            decimal? price,
            string? orderId,
            string? exchangeErrorCode)
        {
            var context = new Dictionary<string, object>
            {
                ["Pair"] = pair,
                ["OrderSide"] = orderSide
            };

            if (!string.IsNullOrEmpty(orderType))
                context["OrderType"] = orderType;

            if (amount.HasValue)
                context["Amount"] = amount.Value;

            if (price.HasValue)
                context["Price"] = price.Value;

            if (!string.IsNullOrEmpty(orderId))
                context["OrderId"] = orderId;

            if (!string.IsNullOrEmpty(exchangeErrorCode))
                context["ExchangeErrorCode"] = exchangeErrorCode;

            return context;
        }
    }
}
