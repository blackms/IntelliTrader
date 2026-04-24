using FluentAssertions;
using Moq;
using IntelliTrader.Application.Common;
using IntelliTrader.Infrastructure.Dispatching;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Dispatching;

public sealed class InMemoryCommandDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WhenResultHandlerIsMissing_RollsBackActiveTransaction()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var serviceProviderMock = CreateServiceProvider(transactionMock.Object);
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync<ResultCommand, string>(new ResultCommand("missing"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenResultHandlerThrows_RollsBackActiveTransactionAndReturnsDispatchError()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var handler = new ThrowingResultHandler();
        var serviceProviderMock = CreateServiceProvider(
            transactionMock.Object,
            (typeof(ICommandHandler<ResultCommand, string>), handler));
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync<ResultCommand, string>(new ResultCommand("boom"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DispatchError");
        result.Error.Message.Should().Contain("handler exploded");

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenVoidHandlerIsMissing_RollsBackActiveTransaction()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var serviceProviderMock = CreateServiceProvider(transactionMock.Object);
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync(new VoidCommand("missing"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenVoidHandlerReturnsNullTask_RollsBackActiveTransaction()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var handler = new NullVoidHandler();
        var serviceProviderMock = CreateServiceProvider(
            transactionMock.Object,
            (typeof(ICommandHandler<VoidCommand>), handler));
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync(new VoidCommand("null"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("InvocationFailed");

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenResultHandlerReturnsNullTask_RollsBackActiveTransaction()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var handler = new NullResultHandler();
        var serviceProviderMock = CreateServiceProvider(
            transactionMock.Object,
            (typeof(ICommandHandler<ResultCommand, string>), handler));
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync<ResultCommand, string>(new ResultCommand("null"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("InvocationFailed");

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenVoidHandlerSucceeds_RollsBackActiveTransactionAndReturnsSuccess()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var handler = new SuccessfulVoidHandler();
        var serviceProviderMock = CreateServiceProvider(
            transactionMock.Object,
            (typeof(ICommandHandler<VoidCommand>), handler));
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync(new VoidCommand("ok"));

        // Assert
        result.IsSuccess.Should().BeTrue();

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WhenVoidHandlerThrows_RollsBackActiveTransactionAndReturnsDispatchError()
    {
        // Arrange
        var transactionMock = CreateTransactionMock(activeTransaction: true);
        var handler = new ThrowingVoidHandler();
        var serviceProviderMock = CreateServiceProvider(
            transactionMock.Object,
            (typeof(ICommandHandler<VoidCommand>), handler));
        var dispatcher = CreateDispatcher(serviceProviderMock.Object);

        // Act
        var result = await dispatcher.DispatchAsync(new VoidCommand("boom"));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DispatchError");
        result.Error.Message.Should().Contain("void handler exploded");

        transactionMock.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        transactionMock.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static InMemoryCommandDispatcher CreateDispatcher(IServiceProvider serviceProvider)
    {
        return new InMemoryCommandDispatcher(
            serviceProvider,
            Mock.Of<ILogger<InMemoryCommandDispatcher>>());
    }

    private static Mock<ITransactionalUnitOfWork> CreateTransactionMock(bool activeTransaction)
    {
        var transactionMock = new Mock<ITransactionalUnitOfWork>();
        transactionMock
            .SetupGet(x => x.HasActiveTransaction)
            .Returns(activeTransaction);
        transactionMock
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        transactionMock
            .Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        transactionMock
            .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        return transactionMock;
    }

    private static Mock<IServiceProvider> CreateServiceProvider(
        ITransactionalUnitOfWork transaction,
        params (Type serviceType, object implementation)[] registrations)
    {
        var registry = registrations.ToDictionary(x => x.serviceType, x => x.implementation);
        registry[typeof(ITransactionalUnitOfWork)] = transaction;

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(x => x.GetService(It.IsAny<Type>()))
            .Returns<Type>(serviceType =>
                registry.TryGetValue(serviceType, out var implementation)
                    ? implementation
                    : null);

        return serviceProviderMock;
    }

    private sealed record ResultCommand(string Id);

    private sealed record VoidCommand(string Id);

    private sealed class ThrowingResultHandler : ICommandHandler<ResultCommand, string>
    {
        public Task<Result<string>> HandleAsync(ResultCommand command, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException($"result handler exploded for {command.Id}");
        }
    }

    private sealed class NullResultHandler : ICommandHandler<ResultCommand, string>
    {
        public Task<Result<string>> HandleAsync(ResultCommand command, CancellationToken cancellationToken = default)
        {
            return null!;
        }
    }

    private sealed class NullVoidHandler : ICommandHandler<VoidCommand>
    {
        public Task<Result> HandleAsync(VoidCommand command, CancellationToken cancellationToken = default)
        {
            return null!;
        }
    }

    private sealed class SuccessfulVoidHandler : ICommandHandler<VoidCommand>
    {
        public Task<Result> HandleAsync(VoidCommand command, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class ThrowingVoidHandler : ICommandHandler<VoidCommand>
    {
        public Task<Result> HandleAsync(VoidCommand command, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException($"void handler exploded for {command.Id}");
        }
    }
}
