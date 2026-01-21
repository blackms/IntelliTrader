using Autofac;
using IntelliTrader.Core;
using System;

namespace IntelliTrader.Backtesting
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<BacktestingService>()
                .As<IBacktestingService>()
                .As<IConfigurableService>()
                .Named<IConfigurableService>(Constants.ServiceNames.BacktestingService)
                .SingleInstance();

            // Access config through the static facade during module registration.
            // The facade is initialized by ApplicationBootstrapper before module loading,
            // ensuring the same IConfigProvider instance is used throughout the application.
#pragma warning disable CS0618 // Suppress obsolete warning - this is a known transitional usage
            var backtestingConfig = Application.ConfigProvider.GetSection<BacktestingConfig>(Constants.ServiceNames.BacktestingService);
#pragma warning restore CS0618

            if (backtestingConfig.Enabled && backtestingConfig.Replay)
            {
                builder.RegisterType<BacktestingExchangeService>()
                    .Named<IExchangeService>(Constants.ServiceNames.BacktestingExchangeService)
                    .As<IConfigurableService>()
                    .Named<IConfigurableService>(Constants.ServiceNames.BacktestingExchangeService)
                    .SingleInstance();

                builder.RegisterType<BacktestingSignalsService>()
                    .As<ISignalsService>()
                    .As<IConfigurableService>()
                    .Named<IConfigurableService>(Constants.ServiceNames.BacktestingSignalsService)
                    .SingleInstance();
            }
        }
    }
}
