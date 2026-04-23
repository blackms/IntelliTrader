namespace IntelliTrader.Core
{
    /// <summary>
    /// Manages the lifecycle of background submitted-order reconciliation.
    /// </summary>
    public interface ISubmittedOrderRefreshService
    {
        /// <summary>
        /// Starts background refresh of submitted orders.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops background refresh of submitted orders.
        /// </summary>
        void Stop();
    }
}
