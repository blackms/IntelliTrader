using System.Collections.Generic;
using IntelliTrader.Core;

namespace IntelliTrader.Rules.Specifications
{
    /// <summary>
    /// Contains all the context data needed to evaluate rule conditions.
    /// This is the candidate object passed to specifications for evaluation.
    /// </summary>
    public class ConditionContext
    {
        /// <summary>
        /// The signal associated with the condition (if any).
        /// May be null if no signal is specified or found.
        /// </summary>
        public ISignal? Signal { get; }

        /// <summary>
        /// The global rating across all signals.
        /// </summary>
        public double? GlobalRating { get; }

        /// <summary>
        /// The trading pair symbol (e.g., "BTCUSDT").
        /// </summary>
        public string? Pair { get; }

        /// <summary>
        /// The trading pair data containing position information.
        /// May be null for signal-only conditions.
        /// </summary>
        public ITradingPair? TradingPair { get; }

        /// <summary>
        /// The speed multiplier for age calculations.
        /// </summary>
        public double Speed { get; }

        public ConditionContext(
            ISignal? signal,
            double? globalRating,
            string? pair,
            ITradingPair? tradingPair,
            double speed)
        {
            Signal = signal;
            GlobalRating = globalRating;
            Pair = pair;
            TradingPair = tradingPair;
            Speed = speed;
        }

        /// <summary>
        /// Factory method to create a context from the CheckConditions parameters.
        /// </summary>
        public static ConditionContext Create(
            Dictionary<string, ISignal> signals,
            string? signalName,
            double? globalRating,
            string? pair,
            ITradingPair? tradingPair,
            double speed)
        {
            ISignal? signal = null;
            if (signalName != null && signals.TryGetValue(signalName, out var s))
            {
                signal = s;
            }

            return new ConditionContext(signal, globalRating, pair, tradingPair, speed);
        }
    }
}
