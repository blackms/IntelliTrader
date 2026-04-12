using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for sending notifications (e.g., Telegram messages) about trading events.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// The notification configuration.
        /// </summary>
        INotificationConfig Config { get; }

        /// <summary>
        /// Starts the notification service and connects to notification channels.
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Stops the notification service.
        /// </summary>
        void Stop();

        /// <summary>
        /// Sends a notification message asynchronously.
        /// </summary>
        /// <param name="message">The message text to send.</param>
        Task NotifyAsync(string message);
    }
}
