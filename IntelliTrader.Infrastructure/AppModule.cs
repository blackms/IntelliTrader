using Autofac;
using IntelliTrader.Application.Common;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading;
using IntelliTrader.Application.Trading.Handlers;
using IntelliTrader.Core;
using IntelliTrader.Domain.Trading.Services;
using IntelliTrader.Infrastructure.Adapters.Exchange;
using IntelliTrader.Infrastructure.Adapters.Legacy;
using IntelliTrader.Infrastructure.Adapters.Persistence.Json;
using IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;
using IntelliTrader.Infrastructure.BackgroundServices;
using IntelliTrader.Infrastructure.Dispatching;
using IntelliTrader.Infrastructure.Events;
using IntelliTrader.Infrastructure.Transactions;
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
        builder.RegisterType<JsonTransactionCoordinator>()
            .SingleInstance();

        RegisterServiceProviderAdapter(builder);
        // Register domain event dispatcher
        RegisterDomainEventDispatcher(builder);

        // Register CQRS dispatchers
        RegisterDispatchers(builder);

        // Register infrastructure adapters required by Application handlers
        RegisterPersistence(builder);
        RegisterPorts(builder);

        // Register legacy service adapters
        RegisterLegacyAdapters(builder);

        // Register application services and handlers
        RegisterApplicationServices(builder);
    }

    private static void RegisterServiceProviderAdapter(ContainerBuilder builder)
    {
        builder.Register(c =>
            {
                var scope = c.Resolve<ILifetimeScope>();
                return (IServiceProvider)new AutofacServiceProviderAdapter(scope);
            })
            .As<IServiceProvider>()
            .SingleInstance();
    }

    private static void RegisterDomainEventDispatcher(ContainerBuilder builder)
    {
        builder.Register(c =>
            {
                return new InMemoryDomainEventDispatcher(
                    c.Resolve<IServiceProvider>(),
                    NullLogger<InMemoryDomainEventDispatcher>.Instance,
                    c.Resolve<IDomainEventHandlerInbox>());
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
        builder.Register(c =>
            new InMemoryCommandDispatcher(
                c.Resolve<IServiceProvider>(),
                NullLogger<InMemoryCommandDispatcher>.Instance))
            .As<ICommandDispatcher>()
            .SingleInstance();

        builder.Register(c =>
            new InMemoryQueryDispatcher(
                c.Resolve<IServiceProvider>(),
                NullLogger<InMemoryQueryDispatcher>.Instance))
            .As<IQueryDispatcher>()
            .SingleInstance();
    }

    private static void RegisterPersistence(ContainerBuilder builder)
    {
        builder.Register(_ =>
            new JsonPositionRepository(CreateDataFilePath("positions.json"), _.Resolve<JsonTransactionCoordinator>()))
            .As<IPositionRepository>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ =>
            new JsonPortfolioRepository(CreateDataFilePath("portfolios.json"), _.Resolve<JsonTransactionCoordinator>()))
            .As<IPortfolioRepository>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ =>
            new JsonOrderRepository(CreateDataFilePath("orders.json"), _.Resolve<JsonTransactionCoordinator>()))
            .As<IOrderRepository>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RepositoryOrderReadModel>()
            .As<IOrderReadModel>()
            .SingleInstance();

        builder.Register(_ =>
            new JsonDomainEventOutbox(CreateDataFilePath("outbox.json"), _.Resolve<JsonTransactionCoordinator>()))
            .As<IDomainEventOutbox>()
            .AsSelf()
            .SingleInstance();

        builder.Register(_ =>
            new JsonDomainEventHandlerInbox(CreateDataFilePath("handler-inbox.json")))
            .As<IDomainEventHandlerInbox>()
            .AsSelf()
            .SingleInstance();

        builder.Register(c =>
            new DomainEventOutboxProcessor(
                c.Resolve<IDomainEventOutbox>(),
                c.Resolve<IDomainEventDispatcher>(),
                NullLogger<DomainEventOutboxProcessor>.Instance))
            .As<IDomainEventOutboxProcessor>()
            .AsSelf()
            .SingleInstance();

        builder.Register(c =>
            new DomainEventOutboxProcessorService(
                NullLogger<DomainEventOutboxProcessorService>.Instance,
                c.Resolve<IDomainEventOutboxProcessor>()))
            .AsSelf()
            .SingleInstance();

        builder.Register(c => new JsonTransactionalUnitOfWork(c.Resolve<JsonTransactionCoordinator>()))
            .As<IUnitOfWork>()
            .As<ITransactionalUnitOfWork>()
            .SingleInstance();
    }

    private static void RegisterPorts(ContainerBuilder builder)
    {
        builder.Register(c =>
            {
                var exchange =
                    c.ResolveOptionalNamed<IExchangeService>("Binance") ??
                    c.ResolveOptional<IExchangeService>() ??
                    throw new InvalidOperationException("No exchange service is registered for IExchangePort.");

                return new BinanceExchangeAdapter(exchange);
            })
            .As<IExchangePort>()
            .SingleInstance();

        builder.RegisterType<ActiveOrderRefreshService>()
            .As<IActiveOrderRefreshService>()
            .As<ISubmittedOrderRefreshService>()
            .SingleInstance();

        builder.RegisterType<DomainEventOutboxReplayService>()
            .As<IDomainEventOutboxReplayService>()
            .SingleInstance();
    }

    private static void RegisterLegacyAdapters(ContainerBuilder builder)
    {
        // Legacy trading service adapter - wraps the existing ITradingService
        builder.RegisterType<LegacyTradingServiceAdapter>()
            .As<ILegacyTradingServiceAdapter>()
            .SingleInstance();
    }

    private static void RegisterApplicationServices(ContainerBuilder builder)
    {
        var applicationAssembly = typeof(OpenPositionHandler).Assembly;

        builder.RegisterType<TradingConstraintValidator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TradingUseCase>()
            .As<ITradingUseCase>()
            .SingleInstance();

        builder.RegisterAssemblyTypes(applicationAssembly)
            .AsClosedTypesOf(typeof(ICommandHandler<,>))
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterAssemblyTypes(applicationAssembly)
            .AsClosedTypesOf(typeof(ICommandHandler<>))
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterAssemblyTypes(applicationAssembly)
            .AsClosedTypesOf(typeof(IQueryHandler<,>))
            .AsImplementedInterfaces()
            .InstancePerDependency();

        builder.RegisterAssemblyTypes(typeof(AppModule).Assembly)
            .AsClosedTypesOf(typeof(IDomainEventHandler<>))
            .AsImplementedInterfaces()
            .SingleInstance();
    }

    private static string CreateDataFilePath(string fileName)
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "data", fileName);
    }
}
