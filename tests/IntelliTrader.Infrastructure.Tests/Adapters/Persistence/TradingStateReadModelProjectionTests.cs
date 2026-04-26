using FluentAssertions;
using IntelliTrader.Domain.Events;
using IntelliTrader.Domain.SharedKernel;
using IntelliTrader.Infrastructure.Adapters.Persistence.ReadModels;
using IntelliTrader.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace IntelliTrader.Infrastructure.Tests.Adapters.Persistence;

public sealed class TradingStateReadModelProjectionTests
{
    [Fact]
    public async Task GetAsync_WhenProjectionIsEmpty_ReturnsRunningState()
    {
        var readModelPath = CreateReadModelPath();

        try
        {
            using var readModel = new JsonTradingStateReadModel(readModelPath);

            var state = await readModel.GetAsync();

            state.IsTradingSuspended.Should().BeFalse();
            state.SuspensionReason.Should().BeNull();
            state.SuspendedAt.Should().BeNull();
            state.ResumedAt.Should().BeNull();
        }
        finally
        {
            DeleteFileIfExists(readModelPath);
        }
    }

    [Fact]
    public async Task DispatchManyAsync_WhenSuspendedAndResumed_ProjectsAndPersistsTradingState()
    {
        var readModelPath = CreateReadModelPath();
        using var readModel = new JsonTradingStateReadModel(readModelPath);
        var handler = new TradingStateReadModelProjectionHandler(readModel);
        var dispatcher = CreateDispatcher(handler);
        var suspendedAt = DateTimeOffset.Parse("2026-04-26T10:00:00Z");
        var resumedAt = DateTimeOffset.Parse("2026-04-26T10:15:00Z");
        var suspended = new TradingSuspendedEvent(
            SuspensionReason.Manual,
            "Manual (Forced)",
            isForced: true,
            openPositions: 2,
            pendingOrders: 1,
            occurredAt: suspendedAt);
        var resumed = new TradingResumedEvent(
            "Manual (Forced)",
            wasForced: true,
            suspensionDuration: TimeSpan.FromMinutes(15),
            previousSuspensionReason: SuspensionReason.Manual,
            occurredAt: resumedAt);

        try
        {
            await dispatcher.DispatchManyAsync([suspended]);

            var suspendedState = await readModel.GetAsync();
            suspendedState.IsTradingSuspended.Should().BeTrue();
            suspendedState.SuspensionReason.Should().Be(SuspensionReason.Manual);
            suspendedState.IsForcedSuspension.Should().BeTrue();
            suspendedState.SuspendedAt.Should().Be(suspendedAt);
            suspendedState.OpenPositionsAtSuspension.Should().Be(2);
            suspendedState.PendingOrdersAtSuspension.Should().Be(1);

            await dispatcher.DispatchManyAsync([resumed]);

            using var reloaded = new JsonTradingStateReadModel(readModelPath);
            var resumedState = await reloaded.GetAsync();
            resumedState.IsTradingSuspended.Should().BeFalse();
            resumedState.SuspensionReason.Should().BeNull();
            resumedState.IsForcedSuspension.Should().BeFalse();
            resumedState.SuspendedAt.Should().BeNull();
            resumedState.ResumedAt.Should().Be(resumedAt);
            resumedState.LastChangedAt.Should().Be(resumedAt);
        }
        finally
        {
            DeleteFileIfExists(readModelPath);
        }
    }

    private static InMemoryDomainEventDispatcher CreateDispatcher(TradingStateReadModelProjectionHandler handler)
    {
        var dispatcher = new InMemoryDomainEventDispatcher(
            Mock.Of<IServiceProvider>(),
            NullLogger<InMemoryDomainEventDispatcher>.Instance);
        dispatcher.RegisterHandler<TradingSuspendedEvent>(handler);
        dispatcher.RegisterHandler<TradingResumedEvent>(handler);
        return dispatcher;
    }

    private static string CreateReadModelPath()
    {
        return Path.Combine(Path.GetTempPath(), $"trading_state_read_model_{Guid.NewGuid():N}.json");
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
