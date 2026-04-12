namespace IntelliTrader.Core
{
    /// <summary>
    /// Provides application-wide runtime context such as speed multiplier for backtesting.
    /// </summary>
    public interface IApplicationContext
    {
        /// <summary>
        /// Gets or sets the speed multiplier used for time-based calculations.
        /// Default is 1.0 (real-time). Values greater than 1 speed up the application (used in backtesting).
        /// </summary>
        double Speed { get; set; }
    }
}
