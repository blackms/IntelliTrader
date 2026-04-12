using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Central orchestrator service that manages the application lifecycle and all timed tasks.
    /// </summary>
    public interface ICoreService : IConfigurableService
    {
        /// <summary>
        /// The core configuration.
        /// </summary>
        ICoreConfig Config { get; }

        /// <summary>
        /// The current application version string.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// True once <see cref="Start"/> has completed and all timed
        /// tasks are active. Used by Kubernetes readiness probes to
        /// decide whether the bot is ready to serve traffic.
        /// </summary>
        bool Running { get; }

        /// <summary>
        /// Starts all registered timed tasks and begins the trading loop.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops all timed tasks and shuts down gracefully.
        /// </summary>
        void Stop();

        /// <summary>
        /// Stops and then restarts all services and timed tasks.
        /// </summary>
        void Restart();

        /// <summary>
        /// Asynchronously stops and restarts all services and timed tasks.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        Task RestartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers a named timed task for periodic execution.
        /// </summary>
        /// <param name="name">The unique name for the task.</param>
        /// <param name="task">The timed task to register.</param>
        void AddTask(string name, HighResolutionTimedTask task);

        /// <summary>
        /// Removes a named timed task from the registry.
        /// </summary>
        /// <param name="name">The name of the task to remove.</param>
        void RemoveTask(string name);

        /// <summary>
        /// Removes all registered timed tasks.
        /// </summary>
        void RemoveAllTasks();

        /// <summary>
        /// Starts a specific named timed task.
        /// </summary>
        /// <param name="name">The name of the task to start.</param>
        void StartTask(string name);

        /// <summary>
        /// Starts all registered timed tasks.
        /// </summary>
        void StartAllTasks();

        /// <summary>
        /// Stops a specific named timed task.
        /// </summary>
        /// <param name="name">The name of the task to stop.</param>
        void StopTask(string name);

        /// <summary>
        /// Stops all registered timed tasks.
        /// </summary>
        void StopAllTasks();

        /// <summary>
        /// Gets a specific named timed task.
        /// </summary>
        /// <param name="name">The name of the task.</param>
        /// <returns>The timed task instance.</returns>
        HighResolutionTimedTask GetTask(string name);

        /// <summary>
        /// Gets all registered timed tasks.
        /// </summary>
        /// <returns>Dictionary of task name to task instance.</returns>
        ConcurrentDictionary<string, HighResolutionTimedTask> GetAllTasks();
    }
}
