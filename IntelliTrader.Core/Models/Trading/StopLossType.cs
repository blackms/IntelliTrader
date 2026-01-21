namespace IntelliTrader.Core
{
    /// <summary>
    /// Defines the type of stop-loss calculation to use.
    /// </summary>
    public enum StopLossType
    {
        /// <summary>
        /// Fixed percentage-based stop-loss (legacy behavior).
        /// </summary>
        Fixed,

        /// <summary>
        /// Dynamic ATR-based stop-loss that adapts to market volatility.
        /// </summary>
        ATR
    }
}
