using Autofac;
using FluentAssertions;
using IntelliTrader.Application.Ports.Driven;
using IntelliTrader.Application.Ports.Driving;
using IntelliTrader.Application.Trading;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Integration;

public sealed class TradingUseCaseRegistrationTests
{
    [Fact]
    public void BuildContainer_ResolvesTradingUseCasePrimaryPort()
    {
        var exchangePortMock = new Mock<IExchangePort>();
        var builder = new ContainerBuilder();
        builder.RegisterModule(new IntelliTrader.Infrastructure.AppModule());
        builder.RegisterInstance(exchangePortMock.Object)
            .As<IExchangePort>()
            .SingleInstance();

        using var container = builder.Build();

        var useCase = container.Resolve<ITradingUseCase>();

        useCase.Should().BeOfType<TradingUseCase>();
    }
}
