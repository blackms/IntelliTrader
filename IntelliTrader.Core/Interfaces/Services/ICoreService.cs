using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    public interface ICoreService : IConfigurableService
    {
        ICoreConfig Config { get; }
        string Version { get; }
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
