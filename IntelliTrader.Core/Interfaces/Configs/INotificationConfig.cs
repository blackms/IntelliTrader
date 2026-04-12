using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Core
{
    /// <summary>
    /// Configuration for the notification subsystem that sends alerts via Telegram.
    /// </summary>
    public interface INotificationConfig
    {
        /// <summary>
        /// Whether the notification subsystem is enabled.
        /// </summary>
        bool Enabled { get; }

        /// <summary>
        /// Whether Telegram notifications are enabled.
        /// </summary>
        bool TelegramEnabled { get; }

        /// <summary>
        /// The Telegram bot API token used for sending messages.
        /// </summary>
        string TelegramBotToken { get; }

        /// <summary>
        /// The Telegram chat ID to send notifications to.
        /// </summary>
        long TelegramChatId { get; }

        /// <summary>
        /// Whether alert-type Telegram notifications are enabled (e.g., health check failures).
        /// </summary>
        bool TelegramAlertsEnabled { get; }
    }
}
