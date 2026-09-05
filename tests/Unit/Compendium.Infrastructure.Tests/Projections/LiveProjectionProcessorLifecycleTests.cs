// -----------------------------------------------------------------------
// <copyright file="LiveProjectionProcessorLifecycleTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Compendium.Infrastructure.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Compendium.Infrastructure.Tests.Projections;

/// <summary>
/// Covers what the live processor owes beyond its cursor: saying that a projection has
/// failed instead of only logging it, honouring a pause and a resume for real, starting a
/// newly-registered projection from the beginning whatever its siblings' checkpoints say,
/// and applying events only while this process holds the consumer lease.
/// </summary>
public sealed class LiveProjectionProcessorLifecycleTests
{
    [Fact]
    public async Task ProcessBatch_Failure_PersistsFailedStateWithMessage_AndHoldsTheCursor()
    {
        var (store, sp) = Setup(new CountingProjection("Healthy"), new ThrowingProjection("Failing", 2));

        using var sut = Sut(store, sp, o => o.MaxProjectionApplyFailures = 5);
        sut.RegisterProjection<CountingProjection>();
        sut.RegisterProjection<ThrowingProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        await sut.ProcessEventBatchAsync([Evt(1), Evt(2), Evt(3)], CancellationToken.None);

        // The failure is legible outside the logs: status Failed, message not empty,
        // and the position it stalled on.
        await store.Received().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state =>
                state.ProjectionName == "Failing"
                && state.Status == ProjectionStatus.Failed
                && !string.IsNullOrWhiteSpace(state.ErrorMessage)
                && state.ErrorMessage!.Contains("boom at 2")
                && state.LastProcessedPosition == 1),
            Arg.Any<CancellationToken>());

        // A healthy projection says nothing: no state row is written for it.
        await store.DidNotReceive().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state => state.ProjectionName == "Healthy"),
            Arg.Any<CancellationToken>());

        // Next pass: the cursor of the failing projection has not moved.
        store.ClearReceivedCalls();
        await sut.ProcessEventBatchAsync([Evt(2), Evt(3)], CancellationToken.None);
        await store.Received().SaveCheckpointAsync("Failing", 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveCheckpointAsync("Failing", 2, Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveCheckpointAsync("Failing", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_RecoveryAfterFailure_PublishesBuilding()
    {
        var recovering = new FlakyProjection("Recovering", failuresBeforeSuccess: 1);
        var (store, sp) = Setup(recovering);

        // RetryCount = 0: the retry happens on the next pass, so the transition is visible.
        using var sut = Sut(store, sp, o => o.RetryCount = 0);
        sut.RegisterProjection<FlakyProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        await sut.ProcessEventBatchAsync([Evt(1)], CancellationToken.None);
        await sut.ProcessEventBatchAsync([Evt(1)], CancellationToken.None);

        await store.Received().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state =>
                state.ProjectionName == "Recovering" && state.Status == ProjectionStatus.Failed),
            Arg.Any<CancellationToken>());
        await store.Received().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state =>
                state.ProjectionName == "Recovering"
                && state.Status == ProjectionStatus.Building
                && state.ErrorMessage == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_RetryCount_ReAppliesTheSameEventWithinOnePass()
    {
        var flaky = new FlakyProjection("Flaky", failuresBeforeSuccess: 2);
        var (store, sp) = Setup(flaky);

        using var sut = Sut(store, sp, o =>
        {
            o.RetryCount = 3;
            o.RetryDelay = TimeSpan.Zero;
        });
        sut.RegisterProjection<FlakyProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        await sut.ProcessEventBatchAsync([Evt(1)], CancellationToken.None);

        // Two failures then a success, all inside the single pass: the cursor advances
        // and nothing was ever reported as failed.
        flaky.Attempts.Should().Be(3);
        flaky.Applied.Should().Equal(1);
        await store.Received().SaveCheckpointAsync("Flaky", 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state => state.Status == ProjectionStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initialize_NewProjectionAmongCheckpointedSiblings_StartsFromZero()
    {
        // The sibling sits at the head of the stream. A projection registered today has
        // no checkpoint: it must replay the whole history, not inherit the head.
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync("Established", Arg.Any<CancellationToken>()).Returns((long?)900);
        store.GetCheckpointAsync("BrandNew", Arg.Any<CancellationToken>()).Returns((long?)null);
        store.GetProjectionStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProjectionState?)null);

        var brandNew = new CountingProjection("BrandNew");
        var services = new ServiceCollection();
        services.AddSingleton(new EstablishedProjection("Established"));
        services.AddSingleton(brandNew);
        var sp = services.BuildServiceProvider();

        using var sut = Sut(store, sp);
        sut.RegisterProjection<EstablishedProjection>();
        sut.RegisterProjection<CountingProjection>();

        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // The read cursor drops to 0 for the newcomer's sake.
        sut.GetStatus().LastProcessedPosition.Should().Be(0);

        // And it applies every event of the stream, while the sibling skips them all.
        await sut.ProcessEventBatchAsync([Evt(1), Evt(2), Evt(3)], CancellationToken.None);
        brandNew.Applied.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Suspend_FreezesTheCursor_AndResumeRestartsFromThePersistedCheckpoint()
    {
        var paused = new CountingProjection("Paused");
        var (store, sp) = Setup(paused);
        store.GetCheckpointAsync("Paused", Arg.Any<CancellationToken>()).Returns((long?)1);

        var eventStore = FakeStreamingEventStore(Evt(1), Evt(2), Evt(3));
        using var sut = Sut(store, sp, eventStore: eventStore);
        sut.RegisterProjection<CountingProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Act — pause, then let the loop run a pass over events 2 and 3.
        sut.SuspendProjection("Paused");
        sut.IsProjectionSuspended("Paused").Should().BeTrue();
        await sut.ProcessNewEventsAsync(CancellationToken.None);

        // Assert — nothing applied, and no checkpoint written: the cursor is frozen at
        // the persisted 1 rather than merely reported as paused.
        paused.Applied.Should().BeEmpty();
        await store.DidNotReceive().SaveCheckpointAsync("Paused", Arg.Any<long>(), Arg.Any<CancellationToken>());

        // Act — resume. The position is re-read from the store first.
        sut.ResumeProjection("Paused");
        await sut.ProcessNewEventsAsync(CancellationToken.None);

        // Assert — it restarts exactly at position 1: no jump over 2, no replay of 1.
        paused.Applied.Should().Equal(2, 3);
    }

    [Fact]
    public async Task Initialize_PersistedPausedState_StartsSuspended()
    {
        // The pause was requested on another pod. A restart must not silently resume it.
        var (store, sp) = Setup(new CountingProjection("Paused"));
        store.GetProjectionStateAsync("Paused", Arg.Any<CancellationToken>())
            .Returns(new ProjectionState { ProjectionName = "Paused", Status = ProjectionStatus.Paused });

        using var sut = Sut(store, sp);
        sut.RegisterProjection<CountingProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        sut.IsProjectionSuspended("Paused").Should().BeTrue();
        sut.GetStatus().SuspendedProjections.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_NoCheckpointEverGoesBackwards_EvenWhenALaggardIsReDelivered()
    {
        // The MIN cursor re-delivers events a projection has already applied. The guard
        // must make that a no-op rather than an occasion to write a lower position.
        var ahead = new CountingProjection("Ahead");
        var laggard = new EstablishedProjection("Laggard");
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync("Ahead", Arg.Any<CancellationToken>()).Returns((long?)3);
        store.GetCheckpointAsync("Laggard", Arg.Any<CancellationToken>()).Returns((long?)1);
        store.GetProjectionStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProjectionState?)null);

        var saved = new ConcurrentBag<(string Name, long Position)>();
        store.SaveCheckpointAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                saved.Add((callInfo.ArgAt<string>(0), callInfo.ArgAt<long>(1)));
                return Task.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddSingleton(ahead);
        services.AddSingleton(laggard);
        var sp = services.BuildServiceProvider();

        using var sut = Sut(store, sp);
        sut.RegisterProjection<CountingProjection>();
        sut.RegisterProjection<EstablishedProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        await sut.ProcessEventBatchAsync([Evt(2), Evt(3), Evt(4)], CancellationToken.None);

        var highWater = new Dictionary<string, long> { ["Ahead"] = 3, ["Laggard"] = 1 };
        foreach (var (name, position) in saved.OrderBy(_ => 0))
        {
            position.Should().BeGreaterOrEqualTo(highWater[name],
                $"the engine must never move the cursor of {name} backwards");
            highWater[name] = position;
        }

        // The re-delivered 2 and 3 were skipped by the projection already past them.
        ahead.Applied.Should().Equal(4);
    }

    [Fact]
    public async Task Execute_WithoutTheConsumerLease_AppliesNothing_ThenAppliesOnceItIsGranted()
    {
        var projection = new CountingProjection("Leased");
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)0);
        store.GetProjectionStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProjectionState?)null);

        var lease = new SwitchableLease { Granted = false };
        var services = new ServiceCollection();
        services.AddSingleton(projection);
        services.AddSingleton<IProjectionConsumerLease>(lease);
        var sp = services.BuildServiceProvider();

        using var sut = Sut(store, sp, o => o.ConsumerLeaseRetryInterval = TimeSpan.FromMilliseconds(20),
            eventStore: FakeStreamingEventStore(Evt(1), Evt(2)));
        sut.RegisterProjection<CountingProjection>();

        await sut.StartAsync(CancellationToken.None);
        try
        {
            // While the lease is refused, this replica is inert.
            await Task.Delay(200);
            projection.Applied.Should().BeEmpty();
            sut.GetStatus().IsConsumerLeaseHolder.Should().BeFalse();

            // Granted: it takes over and consumes the stream.
            lease.Granted = true;
            await WaitUntil(() => projection.Applied.Count == 2);
            sut.GetStatus().IsConsumerLeaseHolder.Should().BeTrue();
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }

        lease.Released.Should().BeTrue("the lease must be handed back when the processor stops");
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        condition().Should().BeTrue("the condition should have been met before the timeout");
    }

    private static (IProjectionStore Store, ServiceProvider Provider) Setup(params Compendium.Infrastructure.Projections.IProjection[] projections)
    {
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        store.GetProjectionStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ProjectionState?)null);

        var services = new ServiceCollection();
        foreach (var projection in projections)
        {
            services.AddSingleton(projection.GetType(), projection);
        }

        return (store, services.BuildServiceProvider());
    }

    private static LiveProjectionProcessor Sut(
        IProjectionStore store,
        IServiceProvider sp,
        Action<ProjectionOptions>? configure = null,
        IStreamingEventStore? eventStore = null)
    {
        var options = new ProjectionOptions
        {
            EnableSnapshots = false,
            BackfillFromBeginningOnEmptyCheckpoint = true,
            RetryCount = 0,
            RetryDelay = TimeSpan.Zero,
        };

        configure?.Invoke(options);

        return new LiveProjectionProcessor(
            eventStore ?? Substitute.For<IStreamingEventStore>(),
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(options));
    }

    private static EventData Evt(long position) => new()
    {
        EventId = Guid.NewGuid(),
        StreamId = "s",
        StreamPosition = position,
        GlobalPosition = position,
        Timestamp = DateTime.UtcNow,
        EventType = "TestEvent",
        Event = new TestEvent(),
    };

    private sealed class TestEvent : IDomainEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();

        public string AggregateId { get; init; } = "agg";

        public string AggregateType { get; init; } = "Test";

        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;

        public long AggregateVersion { get; init; } = 1;

        public int EventVersion { get; init; } = 1;
    }

    /// <summary>
    /// An event store that replays a fixed stream, honouring the strict
    /// <c>global_position &gt; fromPosition</c> semantics of the real one.
    /// </summary>
    private static IStreamingEventStore FakeStreamingEventStore(params EventData[] events)
    {
        var store = Substitute.For<IStreamingEventStore>();
        store.StreamEventsAsync(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Replay(events, callInfo.ArgAt<long>(1)));
        store.GetEventCountAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((long)events.Length);
        store.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>())
            .Returns(events.Length == 0 ? 0 : events.Max(e => e.GlobalPosition));
        return store;
    }

    private static async IAsyncEnumerable<EventData> Replay(EventData[] events, long fromPosition)
    {
        foreach (var e in events.Where(e => e.GlobalPosition > fromPosition).OrderBy(e => e.GlobalPosition))
        {
            yield return e;
            await Task.Yield();
        }
    }

    private sealed class SwitchableLease : IProjectionConsumerLease
    {
        public bool Granted { get; set; }

        public bool Released { get; private set; }

        public Task<IProjectionConsumerLeaseHandle?> TryAcquireAsync(
            string consumerName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IProjectionConsumerLeaseHandle?>(Granted ? new Handle(this) : null);

        private sealed class Handle(SwitchableLease owner) : IProjectionConsumerLeaseHandle
        {
            public bool IsHeld => owner.Granted;

            public Task<bool> RenewAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(owner.Granted);

            public ValueTask DisposeAsync()
            {
                owner.Released = true;
                return ValueTask.CompletedTask;
            }
        }
    }

    private class CountingProjection(string name)
        : Compendium.Infrastructure.Projections.IProjection, IProjection<TestEvent>
    {
        public string ProjectionName { get; } = name;

        public int Version => 1;

        public ConcurrentQueue<long> AppliedQueue { get; } = new();

        public IReadOnlyList<long> Applied => AppliedQueue.ToArray();

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            AppliedQueue.Clear();
            return Task.CompletedTask;
        }

        public Task ApplyAsync(TestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
        {
            AppliedQueue.Enqueue(metadata.GlobalPosition);
            return Task.CompletedTask;
        }
    }

    // A distinct type so two projections can be registered side by side (registration is
    // keyed on the DI type, not on the projection name).
    private sealed class EstablishedProjection(string name) : CountingProjection(name);

    private sealed class ThrowingProjection(string name, long throwFromPosition)
        : Compendium.Infrastructure.Projections.IProjection, IProjection<TestEvent>
    {
        public string ProjectionName { get; } = name;

        public int Version => 1;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyAsync(TestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (metadata.GlobalPosition >= throwFromPosition)
            {
                throw new InvalidOperationException($"boom at {metadata.GlobalPosition}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FlakyProjection(string name, int failuresBeforeSuccess)
        : Compendium.Infrastructure.Projections.IProjection, IProjection<TestEvent>
    {
        private int _failuresLeft = failuresBeforeSuccess;

        public string ProjectionName { get; } = name;

        public int Version => 1;

        public int Attempts { get; private set; }

        public ConcurrentQueue<long> AppliedQueue { get; } = new();

        public IReadOnlyList<long> Applied => AppliedQueue.ToArray();

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyAsync(TestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new InvalidOperationException($"transient failure at {metadata.GlobalPosition}");
            }

            AppliedQueue.Enqueue(metadata.GlobalPosition);
            return Task.CompletedTask;
        }
    }
}
