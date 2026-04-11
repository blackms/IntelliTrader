using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace IntelliTrader.Core
{
    // ICoreService is injected as Lazy<T> on purpose: CoreService depends
    // on INotificationService, so a direct injection would create a cycle
    // at container build time. The lazy wrapper defers resolution until
    // NotifyAsync runs, by which point both services are constructed.
    // Autofac supports Lazy<T> natively (no extra registration needed).
    internal class NotificationService(
        ILoggingService loggingService,
        Lazy<ICoreService> coreService,
        IConfigProvider configProvider) : ConfigrableServiceBase<NotificationConfig>(configProvider), INotificationService
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
                        var instanceName = coreService.Value.Config.InstanceName;
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
