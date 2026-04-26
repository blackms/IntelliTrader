namespace IntelliTrader.Core
{
    /// <summary>
    /// Manages the lifecycle of durable domain event outbox replay.
    /// </summary>
    public interface IDomainEventOutboxReplayService
    {
        /// <summary>
        /// Starts background replay of pending domain events.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops background replay of pending domain events.
        /// </summary>
        void Stop();
    }
}
