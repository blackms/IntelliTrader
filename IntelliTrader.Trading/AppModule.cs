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

            // Register position sizer factory
            builder.RegisterType<PositionSizerFactory>()
                .As<IPositionSizerFactory>()
                .SingleInstance();

            // Register named position sizers
            builder.RegisterType<FixedPercentagePositionSizer>()
                .Named<IPositionSizer>("FixedPercent")
                .SingleInstance();

            builder.RegisterType<KellyCriterionPositionSizer>()
                .Named<IPositionSizer>("Kelly")
                .SingleInstance();

            // Register ATR calculator service for volatility-based calculations
            builder.RegisterType<ATRCalculator>()
                .As<IATRCalculator>()
                .SingleInstance();

            // Register dynamic stop-loss manager for ATR-based trailing stops
            builder.RegisterType<DynamicStopLossManager>()
                .As<IDynamicStopLossManager>()
                .SingleInstance();

            // Register portfolio risk manager for aggregate risk management
            builder.RegisterType<PortfolioRiskManager>()
                .As<IPortfolioRiskManager>()
                .SingleInstance();

            // Register trailing order manager for managing trailing buy/sell states
            builder.RegisterType<TrailingOrderManager>()
                .As<ITrailingOrderManager>()
                .SingleInstance();

            // Note: Buy, Sell, and Swap orchestrators are created internally by TradingService
            // using factory methods. They are not registered in DI because they require
            // TradingService-specific state (Account, OrderHistory, etc.) that is available
            // only after the service is initialized.

            builder.RegisterType<TradingService>()
                .As<ITradingService>()
                .As<IConfigurableService>()
                .Named<IConfigurableService>(Constants.ServiceNames.TradingService)
                .SingleInstance();
        }
    }
}
