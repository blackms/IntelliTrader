namespace IntelliTrader.Core
{
    /// <summary>
    /// Compatibility alias for the legacy submitted-order refresh contract.
    /// Prefer <see cref="IActiveOrderRefreshService"/>.
    /// </summary>
    public interface ISubmittedOrderRefreshService : IActiveOrderRefreshService
    {
    }
}
