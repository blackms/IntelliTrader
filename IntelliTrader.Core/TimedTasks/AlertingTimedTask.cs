using System;

namespace IntelliTrader.Core
{
    internal class AlertingTimedTask : HighResolutionTimedTask
    {
        private readonly ILoggingService _loggingService;
        private readonly IAlertingService _alertingService;

        public AlertingTimedTask(ILoggingService loggingService, IAlertingService alertingService)
        {
            _loggingService = loggingService;
            _alertingService = alertingService;
        }

        public override void Run()
        {
            try
            {
                _alertingService.CheckAlerts();
            }
            catch (Exception ex)
            {
                _loggingService.Error("Error in alerting timed task", ex);
            }
        }
    }
}
