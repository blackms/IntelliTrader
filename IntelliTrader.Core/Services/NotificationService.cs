using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace IntelliTrader.Core
{
    internal class NotificationService(
        ILoggingService loggingService,
        ICoreService coreService) : ConfigrableServiceBase<NotificationConfig>, INotificationService
    {
        public override string ServiceName => Constants.ServiceNames.NotificationService;

        protected override ILoggingService LoggingService => loggingService;

        INotificationConfig INotificationService.Config => Config;

        // Telegram
        private TelegramBotClient telegramBotClient;
        private ChatId telegramChatId;

        public async Task StartAsync()
        {
            try
            {
                loggingService.Info("Start Notification service...");
                if (Config.TelegramEnabled)
                {
                    telegramBotClient = new TelegramBotClient(Config.TelegramBotToken);
                    // Validate bot token by calling GetMe
                    await telegramBotClient.GetMeAsync().ConfigureAwait(false);
                    telegramChatId = new ChatId(Config.TelegramChatId);
                }
                loggingService.Info("Notification service started");
            }
            catch (Exception ex)
            {
                loggingService.Error("Unable to start Notification service", ex);
                Config.Enabled = false;
            }
        }

        public void Stop()
        {
            loggingService.Info("Stop Notification service...");
            if (Config.TelegramEnabled)
            {
                telegramBotClient = null;
            }
            loggingService.Info("Notification service stopped");
        }

        public async Task NotifyAsync(string message)
        {
            if (Config.Enabled)
            {
                if (Config.TelegramEnabled)
                {
                    try
                    {
                        var instanceName = coreService.Config.InstanceName;
                        await telegramBotClient.SendTextMessageAsync(
                            chatId: telegramChatId,
                            text: $"({instanceName}) {message}",
                            disableNotification: !Config.TelegramAlertsEnabled).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        loggingService.Error("Unable to send Telegram message", ex);
                    }
                }
            }
        }

        protected override void OnConfigReloaded()
        {
            Stop();
            // Fire-and-forget for config reload since we're in a sync context
            _ = StartAsync();
        }
    }
}
