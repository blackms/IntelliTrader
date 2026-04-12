using Autofac;
using IntelliTrader.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace IntelliTrader.Exchange.Base
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.Register(ctx =>
            {
                var configProvider = ctx.Resolve<IConfigProvider>();
                return configProvider.GetSection<SecretRotationConfig>("SecretRotation");
            }).AsSelf().SingleInstance();

            builder.Register(ctx =>
            {
                var loggingService = ctx.Resolve<ILoggingService>();
                var notificationService = new Lazy<INotificationService>(() =>
                    ctx.Resolve<IComponentContext>().Resolve<INotificationService>());

                // Resolve the named exchange service via factory
                // The exchange name comes from trading config; default to "Binance"
                var exchangeService = new Lazy<IExchangeService>(() =>
                {
                    var context = ctx.Resolve<IComponentContext>();
                    return context.ResolveOptionalNamed<IExchangeService>("Binance")
                        ?? context.Resolve<IExchangeService>();
                });

                var config = ctx.Resolve<SecretRotationConfig>();

                return (ISecretRotationService)new SecretRotationService(
                    loggingService, exchangeService, notificationService, config);
            }).As<ISecretRotationService>().SingleInstance();
        }
    }
}
