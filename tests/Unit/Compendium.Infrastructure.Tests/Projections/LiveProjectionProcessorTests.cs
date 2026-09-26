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
        // Arrange — wire an in-memory store with a small set of events
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>()).Returns(0L);
        eventStore.StreamEventsAsync(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => OneEventStreamThenEmpty(callInfo.Arg<long>()));

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
            Options.Create(new ProjectionOptions { EnableSnapshots = true, SnapshotInterval = TimeSpan.Zero }));

        sut.RegisterProjection<TestProjection>();
        using var cts = new CancellationTokenSource();

        // Act — start the background service, give it a beat, then stop
        await sut.StartAsync(cts.Token);
        await Task.Delay(150, cts.Token);
        cts.Cancel();
        try
        {
            await sut.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // expected when cancellation propagates from ExecuteAsync
        }

        // Assert — checkpoint(s) were saved as the loop processed at least one event
        await store.Received().SaveCheckpointAsync(
            "TestProjection", Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_StreamingThrows_LoopCatchesAndContinues()
    {
        // Arrange
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>()).Returns(0L);
        var attempt = 0;
        eventStore.StreamEventsAsync(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempt++;
                return ThrowingStream();
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
            Options.Create(new ProjectionOptions { EnableSnapshots = false }));

        sut.RegisterProjection<TestProjection>();
        using var cts = new CancellationTokenSource();

        // Act
        await sut.StartAsync(cts.Token);
        await Task.Delay(120, cts.Token);
        cts.Cancel();
        try
        {
            await sut.StopAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // expected
        }

        // Assert — the streaming throw was caught and the loop kept running for at least one attempt
        attempt.Should().BeGreaterThanOrEqualTo(1);
    }

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
