// -----------------------------------------------------------------------
// <copyright file="EnhancedProjectionManagerRebuildTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Compendium.Infrastructure.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Compendium.Infrastructure.Tests.Projections;

/// <summary>
/// Regression tests for the rebuild-is-a-delete bug. The manager used to read the
/// checkpoint, clear the read model, then replay events STRICTLY after that checkpoint.
/// With the checkpoint at the head of the stream — its normal state — the replay selected
/// nothing and the projection was left empty. These pin the corrected order: rewind to 0,
/// clear, replay everything, and do it to the named projection alone.
/// </summary>
public sealed class EnhancedProjectionManagerRebuildTests
{
    [Fact]
    public async Task Rebuild_WithCheckpointAtTheHead_ReplaysEverything_AndLeavesTheReadModelWhole()
    {
        var projection = new RecordingProjection("Recording");
        var (sut, store, _) = CreateSut(projection, checkpoint: 3, events: [1, 2, 3]);

        // The read model is in its steady state: three rows, cursor at the head.
        projection.Rows.Should().HaveCount(0);
        await sut.RebuildProjectionAsync<RecordingProjection>();

        // Every event was re-applied — the row count is what applying the stream yields,
        // not the 0 the old order produced.
        projection.Rows.Should().Equal(1, 2, 3);
        projection.ResetCount.Should().Be(1);

        // The cursor was rewound before anything was cleared, then written forward again.
        await store.Received().SaveCheckpointAsync("Recording", 0, Arg.Any<CancellationToken>());
        await store.Received().SaveCheckpointAsync("Recording", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rebuild_RewindsTheCursorBeforeClearingTheReadModel()
    {
        // Order matters: a process that dies between the two must leave a cursor that
        // replays too much, never one that claims applied what has just been deleted.
        var order = new List<string>();
        var projection = new RecordingProjection("Recording", onReset: () => order.Add("reset"));
        var (sut, store, _) = CreateSut(projection, checkpoint: 3, events: [1, 2, 3]);

        store.SaveCheckpointAsync("Recording", 0, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("rewind");
                return Task.CompletedTask;
            });

        await sut.RebuildProjectionAsync<RecordingProjection>();

        order.Should().StartWith(["rewind", "reset"]);
    }

    [Fact]
    public async Task Rebuild_SuspendsTheProjectionForItsDuration_AndHandsItBackOnSuccess()
    {
        var projection = new RecordingProjection("Recording");
        var (sut, _, processor) = CreateSut(projection, checkpoint: 3, events: [1, 2, 3]);

        await sut.RebuildProjectionAsync<RecordingProjection>();

        processor.Calls.Should().Equal("suspend:Recording", "resume:Recording");
    }

    [Fact]
    public async Task Rebuild_ThatFails_LeavesTheProjectionSuspended()
    {
        // Half a read model must not go back into service as though it were whole.
        var projection = new RecordingProjection("Recording", onApply: _ => throw new InvalidOperationException("nope"));
        var (sut, store, processor) = CreateSut(projection, checkpoint: 3, events: [1, 2, 3]);

        var act = async () => await sut.RebuildProjectionAsync<RecordingProjection>();

        await act.Should().ThrowAsync<InvalidOperationException>();
        processor.Calls.Should().Equal("suspend:Recording");
        await store.Received().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state => state.Status == ProjectionStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PauseAndResume_ReachTheEngineRatherThanOnlyTheStateRow()
    {
        var projection = new RecordingProjection("Recording");
        var (sut, store, processor) = CreateSut(projection, checkpoint: 7, events: []);

        await sut.PauseProjectionAsync("Recording");
        await sut.ResumeProjectionAsync("Recording");

        processor.Calls.Should().Equal("suspend:Recording", "resume:Recording");

        // And the state row carries the real position instead of the 0 every write used
        // to put in last_processed_position.
        await store.Received().SaveProjectionStateAsync(
            Arg.Is<ProjectionState>(state =>
                state.Status == ProjectionStatus.Paused && state.LastProcessedPosition == 7),
            Arg.Any<CancellationToken>());
    }

    private static (EnhancedProjectionManager Sut, IProjectionStore Store, RecordingProcessor Processor) CreateSut(
        RecordingProjection projection,
        long? checkpoint,
        long[] events)
    {
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetEventCountAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((long)events.Length);
        eventStore.StreamEventsAsync(Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Replay(events, callInfo.ArgAt<long>(1)));

        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(checkpoint);

        var processor = new RecordingProcessor();
        var services = new ServiceCollection();
        services.AddSingleton(projection);
        services.AddSingleton<ILiveProjectionProcessor>(processor);
        var sp = services.BuildServiceProvider();

        var sut = new EnhancedProjectionManager(
            eventStore,
            store,
            sp,
            NullLogger<EnhancedProjectionManager>.Instance,
            Options.Create(new ProjectionOptions
            {
                EnableSnapshots = false,
                RebuildBatchSize = 10,
                ProgressReportInterval = 1,
            }));

        return (sut, store, processor);
    }

    private static async IAsyncEnumerable<EventData> Replay(
        long[] positions,
        long fromPosition,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        foreach (var position in positions.Where(p => p > fromPosition).OrderBy(p => p))
        {
            yield return new EventData
            {
                EventId = Guid.NewGuid(),
                StreamId = "s",
                StreamPosition = position,
                GlobalPosition = position,
                Timestamp = DateTime.UtcNow,
                EventType = "RebuildTestEvent",
                Event = new RebuildTestEvent(),
            };
        }
    }

    private sealed class RebuildTestEvent : IDomainEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();

        public string AggregateId { get; init; } = "agg";

        public string AggregateType { get; init; } = "Test";

        public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;

        public long AggregateVersion { get; init; } = 1;

        public int EventVersion { get; init; } = 1;
    }

    /// <summary>
    /// Stands in for the 26 Nexus projections whose <c>ResetAsync</c> is a
    /// <c>DELETE FROM</c> without a filter: its rows disappear on reset and only come
    /// back if the events are actually replayed.
    /// </summary>
    private sealed class RecordingProjection(string name, Action? onReset = null, Action<long>? onApply = null)
        : Compendium.Infrastructure.Projections.IProjection, IProjection<RebuildTestEvent>
    {
        public string ProjectionName { get; } = name;

        public int Version => 1;

        public List<long> Rows { get; } = [];

        public int ResetCount { get; private set; }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            ResetCount++;
            Rows.Clear();
            onReset?.Invoke();
            return Task.CompletedTask;
        }

        public Task ApplyAsync(
            RebuildTestEvent @event,
            EventMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            onApply?.Invoke(metadata.GlobalPosition);
            Rows.Add(metadata.GlobalPosition);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProcessor : ILiveProjectionProcessor
    {
        public List<string> Calls { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RegisterProjection<TProjection>()
            where TProjection : Compendium.Infrastructure.Projections.IProjection
        {
        }

        public void UnregisterProjection(string projectionName)
        {
        }

        public void SuspendProjection(string projectionName) => Calls.Add($"suspend:{projectionName}");

        public void ResumeProjection(string projectionName) => Calls.Add($"resume:{projectionName}");

        public bool IsProjectionSuspended(string projectionName) =>
            Calls.LastOrDefault(c => c.EndsWith($":{projectionName}", StringComparison.Ordinal))
                ?.StartsWith("suspend", StringComparison.Ordinal) == true;

        public LiveProcessingStatus GetStatus() => new();
    }
}
