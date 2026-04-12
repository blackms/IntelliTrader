using Autofac;
using IntelliTrader.Core;
using IntelliTrader.Exchange.Binance.Config;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Exchange.Binance
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.Register(c =>
            {
                var configProvider = c.Resolve<IConfigProvider>();
                return configProvider.GetSection<ResilienceConfig>("Resilience") ?? new ResilienceConfig();
            }).As<IResilienceConfig>().SingleInstance();

            builder.RegisterType<BinanceExchangeService>().Named<IExchangeService>("Binance").As<IConfigurableService>().Named<IConfigurableService>("ExchangeBinance").SingleInstance().PreserveExistingDefaults();
        }
    }
}
