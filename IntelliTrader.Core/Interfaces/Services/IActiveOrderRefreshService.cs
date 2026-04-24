namespace IntelliTrader.Core
{
    /// <summary>
    /// Manages the lifecycle of background active-order reconciliation.
    /// </summary>
    public interface IActiveOrderRefreshService
    {
        /// <summary>
        /// Starts background refresh of active, non-terminal orders.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops background refresh of active, non-terminal orders.
        /// </summary>
        void Stop();
    }
}
