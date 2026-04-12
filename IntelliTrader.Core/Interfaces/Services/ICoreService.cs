using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS0612 // Type or member is obsolete

namespace IntelliTrader.Core
{
    public interface ICoreService : IConfigurableService
    {
        ICoreConfig Config { get; }
        string Version { get; }
        /// <summary>
        /// True once <see cref="Start"/> has completed and all timed
        /// tasks are active. Used by Kubernetes readiness probes to
        /// decide whether the bot is ready to serve traffic.
        /// </summary>
        bool Running { get; }
        void Start();
        void Stop();
        void Restart();
        Task RestartAsync(CancellationToken cancellationToken = default);
        void AddTask(string name, HighResolutionTimedTask task);
        void RemoveTask(string name);
        void RemoveAllTasks();
        void StartTask(string name);
        void StartAllTasks();
        void StopTask(string name);
        void StopAllTasks();
        HighResolutionTimedTask GetTask(string name);
        ConcurrentDictionary<string, HighResolutionTimedTask> GetAllTasks();
    }
}
