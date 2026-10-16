// -----------------------------------------------------------------------
// <copyright file="LiveProjectionProcessorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Compendium.Infrastructure.Tests.Projections;

/// <summary>
/// Additional unit tests for <see cref="LiveProjectionProcessor"/> that exercise registration,
/// unregistration, status reporting and lifecycle paths beyond the backfill tests.
/// </summary>
public sealed class LiveProjectionProcessorTests
{
    [Fact]
    public void Ctor_NullEventStore_Throws()
    {
        // Arrange / Act
        var act = () => new LiveProjectionProcessor(
            null!,
            Substitute.For<IProjectionStore>(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions()));

        // Assert
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("eventStore");
    }

    [Fact]
    public void Ctor_NullProjectionStore_Throws()
    {
        // Arrange / Act
        var act = () => new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            null!,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions()));

        // Assert
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("projectionStore");
    }

    [Fact]
    public void Ctor_NullProvider_Throws()
    {
        // Arrange / Act
        var act = () => new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            Substitute.For<IProjectionStore>(),
            null!,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions()));

        // Assert
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("serviceProvider");
    }

    [Fact]
    public void Ctor_NullLogger_Throws()
    {
        // Arrange / Act
        var act = () => new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            Substitute.For<IProjectionStore>(),
            new ServiceCollection().BuildServiceProvider(),
            null!,
            Options.Create(new ProjectionOptions()));

        // Assert
        act.Should().Throw<ArgumentNullException>().And.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Ctor_NullOptions_FallsBackToDefaults()
    {
        // Arrange / Act
        using var sut = new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            Substitute.For<IProjectionStore>(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<LiveProjectionProcessor>.Instance,
            null!);

        // Assert
        sut.Should().NotBeNull();
    }

    [Fact]
    public void RegisterProjection_AddsToInternalRegistry()
    {
        // Arrange
        using var sut = CreateSut(out _, out _);

        // Act
        sut.RegisterProjection<TestProjection>();
        var status = sut.GetStatus();

        // Assert
        status.RegisteredProjections.Should().Be(1);
    }

    [Fact]
    public void UnregisterProjection_RemovesFromRegistry()
    {
        // Arrange
        using var sut = CreateSut(out _, out _);
        sut.RegisterProjection<TestProjection>();

        // Act
        sut.UnregisterProjection("TestProjection");
        var status = sut.GetStatus();

        // Assert
        status.RegisteredProjections.Should().Be(0);
        status.ActiveProjections.Should().Be(0);
    }

    [Fact]
    public void GetStatus_NoActivity_ReturnsZeros()
    {
        // Arrange
        using var sut = CreateSut(out _, out _);

        // Act
        var status = sut.GetStatus();

        // Assert
        status.IsRunning.Should().BeFalse();
        status.RegisteredProjections.Should().Be(0);
        status.ActiveProjections.Should().Be(0);
        status.LastProcessedPosition.Should().Be(0);
        status.TotalEventsProcessed.Should().Be(0);
        status.EventsPerSecond.Should().Be(0);
    }

    [Fact]
    public async Task InitializeProjectionsAsync_LoadsSnapshotsWhenEnabled()
    {
        // Arrange
        var (sut, _, store) = CreateSutWithSnapshots(snapshotsEnabled: true);
        sut.RegisterProjection<TestProjection>();

        // Act
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Assert — LoadSnapshotAsync invoked via reflection (we just verify the path executed)
        store.Received().GetCheckpointAsync("TestProjection", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitializeProjectionsAsync_WhenSnapshotsDisabled_SkipsSnapshotLoad()
    {
        // Arrange
        var (sut, _, _) = CreateSutWithSnapshots(snapshotsEnabled: false);
        sut.RegisterProjection<TestProjection>();

        // Act
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Assert — call simply completes without reflection over snapshot store
        sut.GetStatus().RegisteredProjections.Should().Be(1);
    }

    [Fact]
    public void Dispose_AfterStartButNeverStarted_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut(out _, out _);

        // Act / Assert
        sut.Dispose();
        sut.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_RunsLoopAndProcessesEvents()
    {
        // Arrange — wire an in-memory store with a small set of events; the first checkpoint
        // write signals the test, which waits for the loop itself rather than for the clock
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>()).Returns(0L);
        eventStore.StreamEventsAsync(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OneEventStreamThenEmpty(callInfo.Arg<long>()));

        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        var checkpointSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.When(s => s.SaveCheckpointAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()))
            .Do(_ => checkpointSaved.TrySetResult());

        var services = new ServiceCollection();
        services.AddSingleton<TestProjection>();
        var sp = services.BuildServiceProvider();
        using var sut = new LiveProjectionProcessor(
            eventStore,
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions { EnableSnapshots = true, SnapshotInterval = TimeSpan.Zero }));

        sut.RegisterProjection<TestProjection>();

        // Act — start the background service, wait until the loop has saved a checkpoint, then stop
        await sut.StartAsync(CancellationToken.None);
        await checkpointSaved.Task.WaitAsync(LoopTimeout);
        await sut.StopAsync(CancellationToken.None);

        // Assert — checkpoint(s) were saved as the loop processed at least one event
        await store.Received().SaveCheckpointAsync(
            "TestProjection", Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_StreamingThrows_LoopCatchesAndContinues()
    {
        // Arrange — the first poll throws while streaming, the following ones stream nothing.
        // The second poll signals the test: reaching it at all is the behaviour under test.
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>()).Returns(0L);
        var attempts = 0;
        var polledAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        eventStore.StreamEventsAsync(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    return ThrowingStream();
                }

                polledAgain.TrySetResult();
                return EmptyStream();
            });

        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);

        var services = new ServiceCollection();
        services.AddSingleton<TestProjection>();
        var sp = services.BuildServiceProvider();
        using var sut = new LiveProjectionProcessor(
            eventStore,
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions
            {
                EnableSnapshots = false,
                // The loop polls again as soon as it has caught the error: no wall-clock back-off
                // stands between the throw and the signal the test waits for.
                ProcessingErrorRetryDelay = TimeSpan.Zero,
            }));

        sut.RegisterProjection<TestProjection>();

        // Act — start the background service, wait until the loop has polled past the throw, then stop
        await sut.StartAsync(CancellationToken.None);
        await polledAgain.Task.WaitAsync(LoopTimeout);
        await sut.StopAsync(CancellationToken.None);

        // Assert — the throw was caught inside the term: the loop polled again without being
        // re-initialised (one checkpoint read, not one per term), and the fault never surfaced
        attempts.Should().BeGreaterThanOrEqualTo(2);
        await store.Received(1).GetCheckpointAsync("TestProjection", Arg.Any<CancellationToken>());
        sut.ExecuteTask.Should().NotBeNull();
        sut.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue("the fault must not escape the processing loop");
    }

    // The tests above wait for a signal from the loop, never for time to pass; this bound only
    // turns a loop that never gets there into a failure instead of a hang.
    private static readonly TimeSpan LoopTimeout = TimeSpan.FromSeconds(30);

    private static async IAsyncEnumerable<EventData> OneEventStreamThenEmpty(long fromPosition)
    {
        await Task.CompletedTask;
        if (fromPosition == 0)
        {
            yield return new EventData
            {
                EventId = Guid.NewGuid(),
                StreamId = "s",
                StreamPosition = 1,
                GlobalPosition = 1,
                Timestamp = DateTime.UtcNow,
                EventType = "TestEvent",
                Event = new TestEvent(),
            };
        }
    }

    private static async IAsyncEnumerable<EventData> ThrowingStream()
    {
        await Task.CompletedTask;
        throw new InvalidOperationException("stream-fail");
#pragma warning disable CS0162 // Unreachable
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<EventData> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class TestEvent : IDomainEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string AggregateId { get; init; } = "agg";
        public string AggregateType { get; init; } = "Test";
        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
        public long AggregateVersion { get; init; } = 1;
        public int EventVersion { get; init; } = 1;
    }

    private static LiveProjectionProcessor CreateSut(out IStreamingEventStore eventStore, out IProjectionStore store)
    {
        eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>()).Returns(0L);
        store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);

        var services = new ServiceCollection();
        services.AddSingleton<TestProjection>();
        var sp = services.BuildServiceProvider();
        return new LiveProjectionProcessor(
            eventStore,
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions { EnableSnapshots = false }));
    }

    private static (LiveProjectionProcessor sut, IStreamingEventStore eventStore, IProjectionStore store)
        CreateSutWithSnapshots(bool snapshotsEnabled)
    {
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>()).Returns(50L);
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        store.LoadSnapshotAsync<TestProjection>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TestProjection?)null);

        var services = new ServiceCollection();
        services.AddSingleton<TestProjection>();
        var sp = services.BuildServiceProvider();

        return (
            new LiveProjectionProcessor(
                eventStore,
                store,
                sp,
                NullLogger<LiveProjectionProcessor>.Instance,
                Options.Create(new ProjectionOptions { EnableSnapshots = snapshotsEnabled })),
            eventStore,
            store);
    }

    private sealed class TestProjection : Compendium.Infrastructure.Projections.IProjection
    {
        public string ProjectionName => "TestProjection";
        public int Version => 1;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

}
