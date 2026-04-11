using Autofac;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Infrastructure.Adapters.Legacy;
using IntelliTrader.Infrastructure.Dispatching;
using IntelliTrader.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntelliTrader.Infrastructure;

/// <summary>
/// Autofac module for registering infrastructure services.
/// Includes Application layer dispatchers, handlers, and legacy service adapters.
/// </summary>
public class AppModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Register domain event dispatcher
        RegisterDomainEventDispatcher(builder);

        // Register CQRS dispatchers
        RegisterDispatchers(builder);

        // Register legacy service adapters
        RegisterLegacyAdapters(builder);

        // Register command handlers
        RegisterCommandHandlers(builder);
    }

    private static void RegisterDomainEventDispatcher(ContainerBuilder builder)
    {
        // InMemoryDomainEventDispatcher's constructor expects Microsoft DI
        // primitives (System.IServiceProvider, ILogger<T>) that are not
        // part of the Autofac root container. We adapt them manually here:
        //   * IServiceProvider is satisfied by a tiny adapter wrapping the
        //     active Autofac lifetime scope (no extra package required).
        //   * ILogger<InMemoryDomainEventDispatcher> falls back to
        //     NullLogger because IntelliTrader uses its own ILoggingService
        //     and does not configure Microsoft.Extensions.Logging.
        builder.Register(c =>
            {
                var scope = c.Resolve<ILifetimeScope>();
                return new InMemoryDomainEventDispatcher(
                    new AutofacServiceProviderAdapter(scope),
                    NullLogger<InMemoryDomainEventDispatcher>.Instance);
            })
            .As<IDomainEventDispatcher>()
            .SingleInstance();
    }

    /// <summary>
    /// Minimal IServiceProvider adapter over an Autofac lifetime scope.
    /// Used to bridge classes (like InMemoryDomainEventDispatcher) that
    /// take a Microsoft DI IServiceProvider into the Autofac container
    /// without pulling in the Autofac.Extensions.DependencyInjection
    /// package just for one type.
    /// </summary>
    private sealed class AutofacServiceProviderAdapter : IServiceProvider
    {
        private readonly ILifetimeScope _scope;

        public AutofacServiceProviderAdapter(ILifetimeScope scope)
        {
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public object? GetService(Type serviceType)
            => _scope.TryResolve(serviceType, out var instance) ? instance : null;
    }

    private static void RegisterDispatchers(ContainerBuilder builder)
    {
        // Command dispatcher - singleton, stateless
        builder.RegisterType<InMemoryCommandDispatcher>()
            .As<ICommandDispatcher>()
            .SingleInstance();

        // Query dispatcher - singleton, stateless
        builder.RegisterType<InMemoryQueryDispatcher>()
            .As<IQueryDispatcher>()
            .SingleInstance();
    }

    private static void RegisterLegacyAdapters(ContainerBuilder builder)
    {
        // Legacy trading service adapter - wraps the existing ITradingService
        builder.RegisterType<LegacyTradingServiceAdapter>()
            .As<ILegacyTradingServiceAdapter>()
            .SingleInstance();
    }

    private static void RegisterCommandHandlers(ContainerBuilder builder)
    {
        // PlaceBuyOrderHandler
        builder.RegisterType<PlaceBuyOrderHandler>()
            .As<ICommandHandler<PlaceBuyOrderCommand, PlaceBuyOrderResult>>()
            .InstancePerDependency();

        // PlaceSellOrderHandler
        builder.RegisterType<PlaceSellOrderHandler>()
            .As<ICommandHandler<PlaceSellOrderCommand, PlaceSellOrderResult>>()
            .InstancePerDependency();

        // PlaceSwapOrderHandler
        builder.RegisterType<PlaceSwapOrderHandler>()
            .As<ICommandHandler<PlaceSwapOrderCommand, PlaceSwapOrderResult>>()
            .InstancePerDependency();
    }
}
