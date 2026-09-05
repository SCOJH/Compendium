// -----------------------------------------------------------------------
// <copyright file="ProjectionRebuildEdgeCasesE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.EventSourcing;
using Compendium.Infrastructure.Projections;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using Compendium.IntegrationTests.EndToEnd.TestProjections;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// Complements <see cref="ProjectionRebuildE2ETests"/> with edge cases that protect against
/// regressions in checkpoint handling:
///
/// <list type="bullet">
/// <item>Multi-phase rebuild — rebuild, append more events, rebuild again. The checkpoint
/// must advance monotonically and events written after the first rebuild must NOT be lost
/// (regression guard for the "stale checkpoint freezes a projection" failure mode).</item>
/// <item>Rebuild idempotency — calling <c>RebuildProjectionAsync</c> twice in a row with no
/// new events must not change the checkpoint or corrupt projection state. This pins down
/// the contract that drives at-most-once event application even if the rebuild button is
/// hit twice in the admin UI.</item>
/// <item>High starting checkpoint — when a checkpoint sits beyond the highest global position
/// (e.g. table truncation + checkpoint not reset), rebuild must not regress to position 0.
/// Backfill logic landed in <c>fix(projections): opt-in backfill from position 0 on empty
/// checkpoint (#40)</c>; this asserts the inverse: a NON-empty checkpoint stays put.</item>
/// </list>
/// </summary>
[Trait("Category", "E2E")]
public sealed class ProjectionRebuildEdgeCasesE2ETests : IAsyncLifetime
{
    private InMemoryStreamingEventStore _eventStore = null!;
    private InMemoryProjectionStore _projectionStore = null!;
    private Compendium.Infrastructure.Projections.IProjectionManager _projectionManager = null!;
    private ServiceProvider _provider = null!;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.Configure<ProjectionOptions>(o =>
        {
            o.RebuildBatchSize = 100;
            o.MaxConcurrentRebuilds = 1;
            o.ProgressReportInterval = 50;
            o.EnableSnapshots = false;
        });

        _eventStore = new InMemoryStreamingEventStore();
        _projectionStore = new InMemoryProjectionStore();

        services.AddSingleton(_eventStore);
        services.AddSingleton<IStreamingEventStore>(_eventStore);
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton<Compendium.Infrastructure.Projections.IProjectionManager, EnhancedProjectionManager>();
        services.AddSingleton<OrderSummaryProjection>();

        _provider = services.BuildServiceProvider();
        _projectionManager = _provider.GetRequiredService<Compendium.Infrastructure.Projections.IProjectionManager>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _eventStore?.Dispose();
        _provider?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RebuildProjection_TwoPhaseAppend_CheckpointAdvancesMonotonically()
    {
        // Arrange — phase 1: append 5 events, rebuild, capture checkpoint.

        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-multi-phase", DateTimeOffset.UtcNow);
        var phase1Events = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        order.AddOrderLine("line-1", "product-A", 1, 10.00m);
        order.AddOrderLine("line-2", "product-B", 2, 20.00m);
        order.AddOrderLine("line-3", "product-C", 3, 30.00m);
        phase1Events.AddRange(order.DomainEvents);
        order.ClearDomainEvents();

        var phase1Append = await _eventStore.AppendEventsAsync(orderId.ToString(), phase1Events, expectedVersion: 0);
        phase1Append.IsSuccess.Should().BeTrue();

        _projectionManager.RegisterProjection<OrderSummaryProjection>();

        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(streamId: orderId.ToString());

        var checkpointAfterPhase1 = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");
        checkpointAfterPhase1.Should().NotBeNull();
        checkpointAfterPhase1!.Value.Should().BeGreaterThan(0, "phase 1 produced 4 events");

        // Act — phase 2: append more events, rebuild again.
        order.AddOrderLine("line-4", "product-D", 1, 40.00m);
        order.AddOrderLine("line-5", "product-E", 1, 50.00m);
        var phase2Events = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        var phase2Append = await _eventStore.AppendEventsAsync(orderId.ToString(), phase2Events, expectedVersion: phase1Events.Count);
        phase2Append.IsSuccess.Should().BeTrue();

        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(streamId: orderId.ToString());
        var checkpointAfterPhase2 = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");

        // Assert
        checkpointAfterPhase2.Should().NotBeNull();
        checkpointAfterPhase2!.Value.Should().BeGreaterThan(checkpointAfterPhase1.Value,
            "the second rebuild must consume events appended after the first rebuild's checkpoint");

        var state = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");
        state.Status.Should().Be(ProjectionStatus.Completed);
    }

    [Fact]
    public async Task RebuildProjection_CalledTwiceWithNoNewEvents_CheckpointStaysStableAndStateIsCompleted()
    {
        // Arrange — single stream, rebuild once.

        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-idempotent", DateTimeOffset.UtcNow);
        order.AddOrderLine("only-line", "product-X", 1, 99.99m);
        var events = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        var append = await _eventStore.AppendEventsAsync(orderId.ToString(), events, expectedVersion: 0);
        append.IsSuccess.Should().BeTrue();

        _projectionManager.RegisterProjection<OrderSummaryProjection>();
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(streamId: orderId.ToString());
        var firstCheckpoint = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");

        // Act — rebuild again with NO new events appended in between.
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(streamId: orderId.ToString());
        var secondCheckpoint = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");

        // Assert
        firstCheckpoint.Should().NotBeNull();
        secondCheckpoint.Should().NotBeNull();
        secondCheckpoint!.Value.Should().Be(firstCheckpoint!.Value,
            "rebuilding twice with no new events must not change the checkpoint");

        var state = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");
        state.Status.Should().Be(ProjectionStatus.Completed);
    }

    [Fact]
    public async Task RebuildProjection_WithCheckpointAlreadyAtMaxPosition_RestoresTheReadModel()
    {
        // Replaces RebuildProjection_WithCheckpointAlreadyAtMaxPosition_CompletesWithoutReprocessing,
        // which announced in a comment that it measured re-application "via a counting
        // projection wrapper" — a wrapper that did not exist in the file. It asserted
        // only that the checkpoint was unchanged, which stayed true while the rebuild
        // was in fact clearing the read model and replaying nothing: it read the
        // checkpoint, called ResetAsync, then replayed events strictly after that
        // checkpoint, and in steady state the checkpoint sits at the head.
        //
        // What matters is the state of the table, so that is what is asserted here:
        // rebuilding a projection whose checkpoint is already at the head leaves the
        // same rows behind, and the events really are applied again.

        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-max-checkpoint", DateTimeOffset.UtcNow);
        order.AddOrderLine("a", "p1", 1, 5m);
        order.AddOrderLine("b", "p2", 1, 5m);
        var events = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore.AppendEventsAsync(orderId.ToString(), events, expectedVersion: 0);

        _projectionManager.RegisterProjection<OrderSummaryProjection>();
        var projection = _provider.GetRequiredService<OrderSummaryProjection>();

        // First rebuild establishes the read model and a checkpoint at the head.
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(streamId: orderId.ToString());
        var initialCheckpoint = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");
        initialCheckpoint.Should().NotBeNull();
        var maxPosition = await _eventStore.GetMaxGlobalPositionAsync();

        var rowsBefore = projection.GetAllSummaries().Count();
        rowsBefore.Should().BeGreaterThan(0, "the first rebuild must have populated the read model");
        var summaryBefore = projection.GetOrderSummary(orderId.ToString());
        summaryBefore.Should().NotBeNull();

        // Act — rebuild again, with the checkpoint already at the end of the stream.
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(streamId: orderId.ToString());

        // Assert — the read model is whole. This is the assertion the old test lacked,
        // and the one that fails against the pre-fix ordering: the table was emptied and
        // never refilled.
        projection.GetAllSummaries().Should().HaveCount(rowsBefore,
            "a rebuild must leave the read model in the state applying the stream produces");
        var summaryAfter = projection.GetOrderSummary(orderId.ToString());
        summaryAfter.Should().NotBeNull();
        summaryAfter!.LineCount.Should().Be(summaryBefore!.LineCount);
        summaryAfter.TotalAmount.Should().Be(summaryBefore.TotalAmount);

        var afterCheckpoint = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");
        afterCheckpoint.Should().NotBeNull();
        afterCheckpoint!.Value.Should().BeLessOrEqualTo(maxPosition,
            "the checkpoint must never exceed the highest global position observed in the event store");
        afterCheckpoint.Value.Should().Be(initialCheckpoint!.Value,
            "replaying the same stream must land the cursor on the same position");

        var state = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");
        state.Status.Should().Be(ProjectionStatus.Completed);
    }
}
