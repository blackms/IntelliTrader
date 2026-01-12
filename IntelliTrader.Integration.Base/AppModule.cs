using Autofac;
using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Integration.Core
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<IntegrationService>().As<IIntegrationService>().As<IConfigurableService>().Named<IConfigurableService>(Constants.ServiceNames.IntegrationService).SingleInstance();
        }
    }
}
