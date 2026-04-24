using Autofac;
using FluentAssertions;
using IntelliTrader.Core;
using IntelliTrader.Infrastructure.BackgroundServices;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class ActiveOrderRefreshServiceRegistrationTests
{
    [Fact]
    public void BuildContainer_ResolvesActiveAndLegacyRefreshContractsToSameSingleton()
    {
        // Arrange
        var loggingServiceMock = new Mock<ILoggingService>();
        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(loggingServiceMock.Object)
            .As<ILoggingService>()
            .SingleInstance();

        using var container = builder.Build();

        // Act
        var activeService = container.Resolve<IActiveOrderRefreshService>();
        var legacyService = container.Resolve<ISubmittedOrderRefreshService>();

        // Assert
        activeService.Should().BeOfType<ActiveOrderRefreshService>();
        legacyService.Should().BeSameAs(activeService);
    }
}
