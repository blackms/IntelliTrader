using Autofac;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IntelliTrader.Core
{
    public class AppModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<HealthCheckService>().As<IHealthCheckService>().SingleInstance();
            builder.RegisterType<CoreService>().As<ICoreService>().As<IConfigurableService>().Named<IConfigurableService>(Constants.ServiceNames.CoreService).SingleInstance();
            builder.RegisterType<CachingService>().As<ICachingService>().As<IConfigurableService>().Named<IConfigurableService>(Constants.ServiceNames.CachingService).SingleInstance();
            builder.RegisterType<LoggingService>().As<ILoggingService>().As<IConfigurableService>().Named<IConfigurableService>(Constants.ServiceNames.LoggingService).SingleInstance();
            builder.RegisterType<NotificationService>().As<INotificationService>().As<IConfigurableService>().Named<IConfigurableService>(Constants.ServiceNames.NotificationService).SingleInstance();

            // Audit logging — reads config from config/audit.json (falls back to defaults if missing)
            builder.Register(_ => LoadAuditConfig()).SingleInstance();
            builder.RegisterType<AuditService>().As<IAuditService>().SingleInstance();
        }

        private static AuditConfig LoadAuditConfig()
        {
            try
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "config", "audit.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var wrapper = JsonSerializer.Deserialize<AuditConfigWrapper>(json);
                    return wrapper?.Audit ?? new AuditConfig();
                }
            }
            catch
            {
                // Fall back to defaults on any parse error.
            }
            return new AuditConfig();
        }

        /// <summary>
        /// Wrapper to match the JSON shape: { "Audit": { ... } }
        /// </summary>
        private sealed class AuditConfigWrapper
        {
            public AuditConfig Audit { get; set; }
        }
    }
}
