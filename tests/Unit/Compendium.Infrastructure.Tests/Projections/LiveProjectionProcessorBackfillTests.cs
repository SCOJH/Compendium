// -----------------------------------------------------------------------
// <copyright file="LiveProjectionProcessorBackfillTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.Projections;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Compendium.Infrastructure.Tests.Projections;

/// <summary>
/// Verifies the cold-start position picked by <see cref="LiveProjectionProcessor"/>
/// based on <see cref="ProjectionOptions.BackfillFromBeginningOnEmptyCheckpoint"/>.
/// </summary>
public sealed class LiveProjectionProcessorBackfillTests
{
    [Fact]
    public async Task EmptyCheckpoints_DefaultOptions_JumpsToHead()
    {
        var (eventStore, projectionStore) = SetupStores(headPosition: 250L, checkpoint: null);

        var processor = CreateProcessor(eventStore, projectionStore, backfill: false);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(250L,
            "default behaviour avoids replaying weeks of history on cold restart");
        await eventStore.Received().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyCheckpoints_BackfillFlagOn_StaysAtZero()
    {
        var (eventStore, projectionStore) = SetupStores(headPosition: 250L, checkpoint: null);

        var processor = CreateProcessor(eventStore, projectionStore, backfill: true);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(0L,
            "BackfillFromBeginningOnEmptyCheckpoint=true keeps the cursor at 0 so the polling loop reads from position > 0");
        await eventStore.DidNotReceive().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyCheckpoints_DefaultOptions_HeadAtZero_StaysAtZero()
    {
        var (eventStore, projectionStore) = SetupStores(headPosition: 0L, checkpoint: null);

        var processor = CreateProcessor(eventStore, projectionStore, backfill: false);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(0L);
        await eventStore.Received().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyCheckpoints_DefaultOptions_HeadLookupFails_InitializationFails()
    {
        var eventStore = Substitute.For<IStreamingEventStore>();
        var projectionStore = Substitute.For<IProjectionStore>();
        var ex = new InvalidOperationException("head lookup failed");

        projectionStore
            .GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((long?)null);

        eventStore
            .GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<long>(ex));

        var processor = CreateProcessor(eventStore, projectionStore, backfill: false);
        processor.RegisterProjection<TestProjection>();

        var act = async () => await processor.InitializeProjectionsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("head lookup failed");
        await eventStore.Received().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingCheckpoint_TakesPrecedence_RegardlessOfBackfillFlag()
    {
        var (eventStore, projectionStore) = SetupStores(headPosition: 999L, checkpoint: 100L);

        // Even with backfill=true, a real checkpoint always wins.
        var processor = CreateProcessor(eventStore, projectionStore, backfill: true);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(100L);
        await eventStore.DidNotReceive().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingCheckpoint_DefaultOptions_DoesNotJumpToHead()
    {
        var (eventStore, projectionStore) = SetupStores(headPosition: 999L, checkpoint: 100L);

        var processor = CreateProcessor(eventStore, projectionStore, backfill: false);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(100L);
        await eventStore.DidNotReceive().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MultipleProjections_WithDifferentCheckpoints_ResumesFromMinimumCheckpoint()
    {
        // The shared read cursor must resume from the MIN of per-projection checkpoints,
        // not the max: a projection that lagged (e.g. held at a failed event while its
        // sibling advanced) would otherwise be skipped straight to the max position and
        // permanently lose the events in between. Already-applied events are skipped by
        // the per-projection position guard, so resuming low is a no-op for the ahead
        // projection and a genuine catch-up for the behind one.
        var (eventStore, projectionStore) = SetupStoresForProjectionCheckpoints(
            headPosition: 999L,
            ("TestProjection", 100L),
            ("AnotherTestProjection", 250L));

        var processor = CreateProcessor(eventStore, projectionStore, backfill: true);
        processor.RegisterProjection<TestProjection>();
        processor.RegisterProjection<AnotherTestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(100L);
        await eventStore.DidNotReceive().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckpointPersistedAtZero_DefaultOptions_StaysAtZero_NoHeadJump()
    {
        // Edge case: a projection legitimately persisted checkpoint=0 (just started, no
        // events yet, but the row exists). The cold-start policy must NOT fire — we
        // distinguish "checkpoint absent" from "checkpoint = 0".
        var (eventStore, projectionStore) = SetupStores(headPosition: 999L, checkpoint: 0L);

        var processor = CreateProcessor(eventStore, projectionStore, backfill: false);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(0L,
            "an explicit checkpoint at 0 means the projection trusts the persisted state, even with the legacy default flag");
        await eventStore.DidNotReceive().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckpointPersistedAtZero_BackfillFlagOn_StaysAtZero()
    {
        // Same edge case under the opt-in flag — the persisted state still wins.
        // No head-jump (already covered by the assertion above) and no warning needed:
        // backfill flag is only a *cold start* policy, not "always start at 0".
        var (eventStore, projectionStore) = SetupStores(headPosition: 999L, checkpoint: 0L);

        var processor = CreateProcessor(eventStore, projectionStore, backfill: true);
        processor.RegisterProjection<TestProjection>();

        await processor.InitializeProjectionsAsync(CancellationToken.None);

        processor.GetStatus().LastProcessedPosition.Should().Be(0L);
        await eventStore.DidNotReceive().GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>());
    }

    private static (IStreamingEventStore eventStore, IProjectionStore projectionStore) SetupStores(
        long headPosition,
        long? checkpoint,
        IReadOnlyDictionary<string, long?>? checkpointsByProjection = null)
    {
        var eventStore = Substitute.For<IStreamingEventStore>();
        eventStore.GetMaxGlobalPositionAsync(Arg.Any<CancellationToken>())
            .Returns(headPosition);

        var projectionStore = Substitute.For<IProjectionStore>();
        projectionStore.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var projectionName = callInfo.ArgAt<string>(0);

                if (checkpointsByProjection is not null &&
                    checkpointsByProjection.TryGetValue(projectionName, out var namedCheckpoint))
                {
                    return namedCheckpoint;
                }

                return checkpoint;
            });
        // Snapshot path is intentionally not exercised — EnableSnapshots=false in CreateProcessor.

        return (eventStore, projectionStore);
    }

    private static (IStreamingEventStore eventStore, IProjectionStore projectionStore) SetupStoresForProjectionCheckpoints(
        long headPosition,
        params (string projectionName, long? checkpoint)[] checkpointsByProjection)
    {
        var projectionCheckpoints = checkpointsByProjection
            .ToDictionary(x => x.projectionName, x => x.checkpoint);

        return SetupStores(headPosition, checkpoint: null, checkpointsByProjection: projectionCheckpoints);
    }

    private static LiveProjectionProcessor CreateProcessor(
        IStreamingEventStore eventStore,
        IProjectionStore projectionStore,
        bool backfill)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestProjection>();
        services.AddSingleton<AnotherTestProjection>();
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new ProjectionOptions
        {
            BackfillFromBeginningOnEmptyCheckpoint = backfill,
            EnableSnapshots = false,
        });

        return new LiveProjectionProcessor(
            eventStore,
            projectionStore,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            options);
    }

    /// <summary>Minimal projection used purely so the processor has at least one registered.</summary>
    private sealed class TestProjection : Compendium.Infrastructure.Projections.IProjection
    {
        public string ProjectionName => "TestProjection";
        public int Version => 1;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AnotherTestProjection : Compendium.Infrastructure.Projections.IProjection
    {
        public string ProjectionName => "AnotherTestProjection";
        public int Version => 1;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
