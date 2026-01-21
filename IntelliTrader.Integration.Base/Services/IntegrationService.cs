using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Integration.Core
{
    internal class IntegrationService(ILoggingService loggingService) : ConfigrableServiceBase<IntegrationConfig>, IIntegrationService
    {
        public override string ServiceName => Constants.ServiceNames.IntegrationService;

        protected override ILoggingService LoggingService => loggingService;
    }
}
