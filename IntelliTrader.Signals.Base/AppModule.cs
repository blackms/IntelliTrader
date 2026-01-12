using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using IntelliTrader.Core;
using Microsoft.Extensions.Configuration;

namespace IntelliTrader.Signals.Base
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Register signal receiver factory that resolves named signal receivers
            builder.Register<Func<string, string, IConfigurationSection, ISignalReceiver>>(ctx =>
            {
                var context = ctx.Resolve<IComponentContext>();
                return (receiverType, name, configuration) => context.ResolveOptionalNamed<ISignalReceiver>(receiverType,
                    new TypedParameter(typeof(string), name),
                    new TypedParameter(typeof(IConfigurationSection), configuration));
            });

            builder.RegisterType<SignalsService>().As<ISignalsService>().As<IConfigurableService>().Named<IConfigurableService>(Constants.ServiceNames.SignalsService).SingleInstance().PreserveExistingDefaults();
        }
    }
}
