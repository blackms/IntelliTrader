using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace IntelliTrader.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when there is insufficient balance to execute a trading operation.
    /// </summary>
    [Serializable]
    public class InsufficientBalanceException : IntelliTraderException
    {
        private const string ErrorCodeValue = "INSUFFICIENT_BALANCE";

        /// <summary>
        /// The required amount for the operation.
        /// </summary>
        public decimal RequiredAmount { get; }

        /// <summary>
        /// The available balance.
        /// </summary>
        public decimal AvailableBalance { get; }

        /// <summary>
        /// The currency of the balance.
        /// </summary>
        public string Currency { get; }

        /// <summary>
        /// The trading pair involved in the operation (if applicable).
        /// </summary>
        public string? Pair { get; }

        public InsufficientBalanceException(decimal requiredAmount, decimal availableBalance, string currency)
            : this(requiredAmount, availableBalance, currency, null)
        {
        }

        public InsufficientBalanceException(decimal requiredAmount, decimal availableBalance, string currency, string? pair)
            : base(
                CreateMessage(requiredAmount, availableBalance, currency, pair),
                ErrorCodeValue,
                CreateContext(requiredAmount, availableBalance, currency, pair))
        {
            RequiredAmount = requiredAmount;
            AvailableBalance = availableBalance;
            Currency = currency;
            Pair = pair;
        }

        public InsufficientBalanceException(decimal requiredAmount, decimal availableBalance, string currency, string? pair, Exception innerException)
            : base(
                CreateMessage(requiredAmount, availableBalance, currency, pair),
                ErrorCodeValue,
                CreateContext(requiredAmount, availableBalance, currency, pair),
                innerException)
        {
            RequiredAmount = requiredAmount;
            AvailableBalance = availableBalance;
            Currency = currency;
            Pair = pair;
        }

        protected InsufficientBalanceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            RequiredAmount = info.GetDecimal(nameof(RequiredAmount));
            AvailableBalance = info.GetDecimal(nameof(AvailableBalance));
            Currency = info.GetString(nameof(Currency)) ?? "UNKNOWN";
            Pair = info.GetString(nameof(Pair));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(RequiredAmount), RequiredAmount);
            info.AddValue(nameof(AvailableBalance), AvailableBalance);
            info.AddValue(nameof(Currency), Currency);
            info.AddValue(nameof(Pair), Pair);
        }

        /// <summary>
        /// Gets the deficit amount (how much is missing).
        /// </summary>
        public decimal Deficit => RequiredAmount - AvailableBalance;

        private static string CreateMessage(decimal required, decimal available, string currency, string? pair)
        {
            var message = $"Insufficient balance: required {required:F8} {currency}, available {available:F8} {currency}";
            if (!string.IsNullOrEmpty(pair))
            {
                message += $" for pair {pair}";
            }
            return message;
        }

        private static Dictionary<string, object> CreateContext(decimal required, decimal available, string currency, string? pair)
        {
            var context = new Dictionary<string, object>
            {
                ["RequiredAmount"] = required,
                ["AvailableBalance"] = available,
                ["Currency"] = currency,
                ["Deficit"] = required - available
            };

            if (!string.IsNullOrEmpty(pair))
            {
                context["Pair"] = pair;
            }

            return context;
        }
    }
}
