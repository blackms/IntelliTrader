using System;
using System.Collections.Generic;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Manager responsible for tracking and managing trailing buy/sell orders.
    /// Provides UI state tracking for active trailing operations.
    /// </summary>
    public interface ITrailingOrderManager
    {
        /// <summary>
        /// Initiates a trailing buy for a pair if trailing is enabled and not already active.
        /// </summary>
        /// <param name="pair">The trading pair to trail.</param>
        /// <returns>True if trailing was initiated; false if already active or not applicable.</returns>
        bool InitiateTrailingBuy(string pair);

        /// <summary>
        /// Initiates a trailing sell for a pair if trailing is enabled and not already active.
        /// </summary>
        /// <param name="pair">The trading pair to trail.</param>
        /// <returns>True if trailing was initiated; false if already active or not applicable.</returns>
        bool InitiateTrailingSell(string pair);

        /// <summary>
        /// Cancels trailing buy for a pair.
        /// </summary>
        /// <param name="pair">The trading pair.</param>
        void CancelTrailingBuy(string pair);

        /// <summary>
        /// Cancels trailing sell for a pair.
        /// </summary>
        /// <param name="pair">The trading pair.</param>
        void CancelTrailingSell(string pair);

        /// <summary>
        /// Clears all trailing operations (both buys and sells).
        /// </summary>
        void ClearAll();

        /// <summary>
        /// Gets list of pairs with active trailing buys.
        /// </summary>
        /// <returns>List of pair names.</returns>
        List<string> GetTrailingBuys();

        /// <summary>
        /// Gets list of pairs with active trailing sells.
        /// </summary>
        /// <returns>List of pair names.</returns>
        List<string> GetTrailingSells();

        /// <summary>
        /// Checks if a trailing buy is active for the specified pair.
        /// </summary>
        /// <param name="pair">The trading pair.</param>
        /// <returns>True if trailing buy is active.</returns>
        bool HasTrailingBuy(string pair);

        /// <summary>
        /// Checks if a trailing sell is active for the specified pair.
        /// </summary>
        /// <param name="pair">The trading pair.</param>
        /// <returns>True if trailing sell is active.</returns>
        bool HasTrailingSell(string pair);
    }
}
