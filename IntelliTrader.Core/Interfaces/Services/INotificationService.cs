using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    public interface INotificationService
    {
        INotificationConfig Config { get; }
        Task StartAsync();
        void Stop();
        Task NotifyAsync(string message);
    }
}
