using Autofac;
using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Trading
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Register exchange service factory that resolves named exchange services
            builder.Register<Func<string, IExchangeService>>(ctx =>
            {
                var context = ctx.Resolve<IComponentContext>();
                return name => context.ResolveOptionalNamed<IExchangeService>(name);
            });

            builder.RegisterType<TradingService>()
                .As<ITradingService>()
                .As<IConfigurableService>()
                .Named<IConfigurableService>(Constants.ServiceNames.TradingService)
                .SingleInstance();
        }
    }
}
