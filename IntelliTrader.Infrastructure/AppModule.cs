using Autofac;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading.Commands;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Infrastructure.Adapters.Legacy;
using IntelliTrader.Infrastructure.Dispatching;
using IntelliTrader.Infrastructure.Events;

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
        // Register the domain event dispatcher as a singleton
        // The dispatcher resolves handlers from the container at dispatch time
        builder.RegisterType<InMemoryDomainEventDispatcher>()
            .As<IDomainEventDispatcher>()
            .SingleInstance();
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
