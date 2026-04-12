using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Defines a set of min/max conditions evaluated against signal data and trading pair state.
    /// All specified conditions must be satisfied simultaneously for the rule to match.
    /// </summary>
    public interface IRuleCondition
    {
        /// <summary>
        /// The signal name this condition evaluates against.
        /// </summary>
        string Signal { get; }

        /// <summary>
        /// Minimum 24h volume required.
        /// </summary>
        long? MinVolume { get; }

        /// <summary>
        /// Maximum 24h volume allowed.
        /// </summary>
        long? MaxVolume { get; }

        /// <summary>
        /// Minimum volume change percentage required.
        /// </summary>
        double? MinVolumeChange { get; }

        /// <summary>
        /// Maximum volume change percentage allowed.
        /// </summary>
        double? MaxVolumeChange { get; }

        /// <summary>
        /// Minimum price required.
        /// </summary>
        decimal? MinPrice { get; }

        /// <summary>
        /// Maximum price allowed.
        /// </summary>
        decimal? MaxPrice { get; }

        /// <summary>
        /// Minimum price change percentage required.
        /// </summary>
        decimal? MinPriceChange { get; }

        /// <summary>
        /// Maximum price change percentage allowed.
        /// </summary>
        decimal? MaxPriceChange { get; }

        /// <summary>
        /// Minimum signal rating required.
        /// </summary>
        double? MinRating { get; }

        /// <summary>
        /// Maximum signal rating allowed.
        /// </summary>
        double? MaxRating { get; }

        /// <summary>
        /// Minimum rating change required.
        /// </summary>
        double? MinRatingChange { get; }

        /// <summary>
        /// Maximum rating change allowed.
        /// </summary>
        double? MaxRatingChange { get; }

        /// <summary>
        /// Minimum volatility required.
        /// </summary>
        double? MinVolatility { get; }

        /// <summary>
        /// Maximum volatility allowed.
        /// </summary>
        double? MaxVolatility { get; }

        /// <summary>
        /// Minimum global market rating required.
        /// </summary>
        double? MinGlobalRating { get; }

        /// <summary>
        /// Maximum global market rating allowed.
        /// </summary>
        double? MaxGlobalRating { get; }

        /// <summary>
        /// Specific trading pairs this condition applies to. Empty means all pairs.
        /// </summary>
        List<string> Pairs { get; }

        /// <summary>
        /// Minimum position age in minutes.
        /// </summary>
        double? MinAge { get; }

        /// <summary>
        /// Maximum position age in minutes.
        /// </summary>
        double? MaxAge { get; }

        /// <summary>
        /// Minimum time in minutes since last buy for this pair.
        /// </summary>
        double? MinLastBuyAge { get; }

        /// <summary>
        /// Maximum time in minutes since last buy for this pair.
        /// </summary>
        double? MaxLastBuyAge { get; }

        /// <summary>
        /// Minimum profit/loss margin percentage required.
        /// </summary>
        decimal? MinMargin { get; }

        /// <summary>
        /// Maximum profit/loss margin percentage allowed.
        /// </summary>
        decimal? MaxMargin { get; }

        /// <summary>
        /// Minimum margin change percentage required.
        /// </summary>
        decimal? MinMarginChange { get; }

        /// <summary>
        /// Maximum margin change percentage allowed.
        /// </summary>
        decimal? MaxMarginChange { get; }

        /// <summary>
        /// Minimum position amount required.
        /// </summary>
        decimal? MinAmount { get; set; }

        /// <summary>
        /// Maximum position amount allowed.
        /// </summary>
        decimal? MaxAmount { get; set; }

        /// <summary>
        /// Minimum position cost required.
        /// </summary>
        decimal? MinCost { get; }

        /// <summary>
        /// Maximum position cost allowed.
        /// </summary>
        decimal? MaxCost { get; }

        /// <summary>
        /// Minimum DCA level required.
        /// </summary>
        int? MinDCALevel { get; }

        /// <summary>
        /// Maximum DCA level allowed.
        /// </summary>
        int? MaxDCALevel { get; }

        /// <summary>
        /// Signal rule names that must have been triggered for this pair.
        /// </summary>
        List<string> SignalRules { get; }
    }
}
