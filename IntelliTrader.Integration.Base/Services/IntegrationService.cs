using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Integration.Core
{
    internal class IntegrationService : ConfigrableServiceBase<IntegrationConfig>, IIntegrationService
    {
        public override string ServiceName => Constants.ServiceNames.IntegrationService;

        private readonly ILoggingService loggingService;

        public IntegrationService(ILoggingService loggingService)
        {
            this.loggingService = loggingService;
        }
    }
}
