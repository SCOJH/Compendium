// -----------------------------------------------------------------------
// <copyright file="LiveProjectionProcessorCheckpointTests.cs" company="Sassy Solutions">
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
/// Regression tests for the checkpoint-advance-on-error bug: the live processor
/// used to swallow a projection's apply exception AND advance a single shared
/// checkpoint for every projection, permanently dropping the failed event from
/// that read model. These prove the per-projection checkpoint semantics:
/// <list type="number">
///   <item>A projection that throws does NOT advance its checkpoint past the
///     failed event, while a healthy sibling advances to the batch head.</item>
///   <item>A persistently-failing projection is dead-lettered after
///     <see cref="ProjectionOptions.MaxProjectionApplyFailures"/> attempts, and
///     healthy projections still advance.</item>
///   <item>On restart, the processor resumes from the MIN of per-projection
///     checkpoints so a previously held-back projection re-receives its events.</item>
/// </list>
/// </summary>
public sealed class LiveProjectionProcessorCheckpointTests
{
    [Fact]
    public async Task ProcessBatch_OneProjectionThrows_HoldsItsCheckpoint_SiblingAdvances()
    {
        // Arrange: two projections over the same event type — one always throws.
        var healthy = new CountingProjection("Healthy");
        var failing = new ThrowingProjection("Failing", throwFromPosition: 2);

        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);

        var services = new ServiceCollection();
        services.AddSingleton(healthy);
        services.AddSingleton(failing);
        var sp = services.BuildServiceProvider();

        using var sut = new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions
            {
                EnableSnapshots = false,
                BackfillFromBeginningOnEmptyCheckpoint = true,
                MaxProjectionApplyFailures = 5,
            }));

        sut.RegisterProjection<CountingProjection>();
        sut.RegisterProjection<ThrowingProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Events at global positions 1, 2, 3. Failing throws from position 2.
        var batch = new List<EventData>
        {
            Evt(1), Evt(2), Evt(3),
        };

        // Act
        await sut.ProcessEventBatchAsync(batch, CancellationToken.None);

        // Assert — healthy applied all three and checkpointed at 3.
        healthy.Applied.Should().Equal(1, 2, 3);
        await store.Received().SaveCheckpointAsync("Healthy", 3, Arg.Any<CancellationToken>());

        // Failing applied only position 1, then held its checkpoint at 1 (BEFORE the
        // failed position 2) — it must NEVER be saved at 2 or 3.
        failing.Applied.Should().Equal(1);
        await store.Received().SaveCheckpointAsync("Failing", 1, Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveCheckpointAsync("Failing", 2, Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveCheckpointAsync("Failing", 3, Arg.Any<CancellationToken>());

        // Shared read cursor = MIN(healthy=3, failing=1) = 1, so position 2 is
        // re-streamed to the failing projection on the next pass.
        sut.GetStatus().LastProcessedPosition.Should().Be(1);
    }

    [Fact]
    public async Task ProcessBatch_RepeatedFailures_DeadLettersProjection_SiblingKeepsAdvancing()
    {
        var healthy = new CountingProjection("Healthy");
        var failing = new ThrowingProjection("Failing", throwFromPosition: 1); // throws on everything

        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);

        var services = new ServiceCollection();
        services.AddSingleton(healthy);
        services.AddSingleton(failing);
        var sp = services.BuildServiceProvider();

        using var sut = new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions
            {
                EnableSnapshots = false,
                BackfillFromBeginningOnEmptyCheckpoint = true,
                MaxProjectionApplyFailures = 3,
            }));

        sut.RegisterProjection<CountingProjection>();
        sut.RegisterProjection<ThrowingProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Act — run the same poison event enough times to cross the halt threshold.
        for (var pass = 0; pass < 3; pass++)
        {
            await sut.ProcessEventBatchAsync(new List<EventData> { Evt(1), Evt(2) }, CancellationToken.None);
        }

        // Assert — failing projection is dead-lettered; healthy advanced normally.
        sut.GetStatus().HaltedProjections.Should().Be(1);
        healthy.Applied.Should().Equal(1, 2);

        // Once healthy is the only live projection, the cursor is free to advance to
        // its position (2) rather than being pinned at the poison position forever.
        sut.GetStatus().LastProcessedPosition.Should().Be(2);

        // The failing projection never applied anything and its checkpoint stayed at 0.
        failing.Applied.Should().BeEmpty();
        await store.DidNotReceive().SaveCheckpointAsync("Failing", 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Initialize_ResumesFromMinCheckpoint_NotMax()
    {
        // A previously held-back projection persisted a LOWER checkpoint than its
        // sibling. Resuming from the max would skip (and permanently lose) the events
        // between the two positions for the laggard; the fix resumes from the min.
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync("Healthy", Arg.Any<CancellationToken>()).Returns((long?)100);
        store.GetCheckpointAsync("Failing", Arg.Any<CancellationToken>()).Returns((long?)40);

        var services = new ServiceCollection();
        services.AddSingleton(new CountingProjection("Healthy"));
        services.AddSingleton(new ThrowingProjection("Failing", throwFromPosition: long.MaxValue));
        var sp = services.BuildServiceProvider();

        using var sut = new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions { EnableSnapshots = false }));

        sut.RegisterProjection<CountingProjection>();
        sut.RegisterProjection<ThrowingProjection>();

        // Act
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Assert — cursor resumes from MIN(100, 40) = 40.
        sut.GetStatus().LastProcessedPosition.Should().Be(40);
    }

    [Fact]
    public async Task ProcessBatch_ReDelivery_SkipsEventsAlreadyAppliedByAProjection()
    {
        // Simulate the MIN-cursor re-delivery: an ahead projection must NOT re-apply
        // an event it already processed (which would crash/duplicate non-idempotent
        // read models). Seed a checkpoint at 2, then deliver 1..3; only 3 is applied.
        var store = Substitute.For<IProjectionStore>();
        store.GetCheckpointAsync("Ahead", Arg.Any<CancellationToken>()).Returns((long?)2);

        var ahead = new CountingProjection("Ahead");
        var services = new ServiceCollection();
        services.AddSingleton(ahead);
        var sp = services.BuildServiceProvider();

        using var sut = new LiveProjectionProcessor(
            Substitute.For<IStreamingEventStore>(),
            store,
            sp,
            NullLogger<LiveProjectionProcessor>.Instance,
            Options.Create(new ProjectionOptions { EnableSnapshots = false }));

        sut.RegisterProjection<CountingProjection>();
        await sut.InitializeProjectionsAsync(CancellationToken.None);

        // Act — re-deliver 1, 2 (already applied) and 3 (new).
        await sut.ProcessEventBatchAsync(new List<EventData> { Evt(1), Evt(2), Evt(3) }, CancellationToken.None);

        // Assert — only position 3 applied; 1 and 2 skipped by the per-projection guard.
        ahead.Applied.Should().Equal(3);
        await store.Received().SaveCheckpointAsync("Ahead", 3, Arg.Any<CancellationToken>());
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

    private sealed class CountingProjection(string name)
        : Compendium.Infrastructure.Projections.IProjection, IProjection<TestEvent>
    {
        public string ProjectionName { get; } = name;

        public int Version => 1;

        public ConcurrentQueue<long> AppliedQueue { get; } = new();

        public IReadOnlyList<long> Applied => AppliedQueue.ToArray();

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyAsync(TestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
        {
            AppliedQueue.Enqueue(metadata.GlobalPosition);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingProjection(string name, long throwFromPosition)
        : Compendium.Infrastructure.Projections.IProjection, IProjection<TestEvent>
    {
        public string ProjectionName { get; } = name;

        public int Version => 1;

        public ConcurrentQueue<long> AppliedQueue { get; } = new();

        public IReadOnlyList<long> Applied => AppliedQueue.ToArray();

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyAsync(TestEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
        {
            if (metadata.GlobalPosition >= throwFromPosition)
            {
                throw new InvalidOperationException($"boom at {metadata.GlobalPosition}");
            }

            AppliedQueue.Enqueue(metadata.GlobalPosition);
            return Task.CompletedTask;
        }
    }
}
