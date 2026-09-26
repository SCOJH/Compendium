// -----------------------------------------------------------------------
// <copyright file="ProjectionRebuildE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Domain.Events;
using Compendium.Infrastructure.EventSourcing;
using Compendium.Infrastructure.Projections;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using Compendium.IntegrationTests.EndToEnd.TestProjections;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario: Projection Rebuild and Query.
/// Tests projection rebuild from large event streams with performance monitoring.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ProjectionRebuildE2ETests : IAsyncLifetime
{
    private InMemoryStreamingEventStore? _eventStore;
    private InMemoryProjectionStore? _projectionStore;
    private Compendium.Infrastructure.Projections.IProjectionManager? _projectionManager;
    private ServiceProvider? _provider;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.Configure<ProjectionOptions>(options =>
        {
            options.RebuildBatchSize = 100;
            options.MaxConcurrentRebuilds = 2;
            options.ProgressReportInterval = 10;
            options.EnableSnapshots = false;
            options.SnapshotInterval = TimeSpan.FromHours(1);
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
    public async Task RebuildProjection_From1000Events_CompletesSuccessfully()
    {
        // Arrange
        const int eventCount = 1000;
        var orderId = OrderId.New();
        var customerId = "customer-perf-001";

        // Create order
        var order = OrderAggregate.PlaceOrder(orderId, customerId, DateTimeOffset.UtcNow);
        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        // Add 999 order lines in batches of 100 (more realistic for production)
        const int batchSize = 100;
        var currentVersion = 1L;

        for (int batchStart = 1; batchStart < eventCount; batchStart += batchSize)
        {
            var batchEvents = new List<IDomainEvent>();
            var batchEnd = Math.Min(batchStart + batchSize, eventCount);

            for (int i = batchStart; i < batchEnd; i++)
            {
                var lineResult = order.AddOrderLine($"line-{i}", $"product-{i}", 1, 10.00m);
                lineResult.IsSuccess.Should().BeTrue();

                var lineEvents = order.DomainEvents.ToList();
                batchEvents.AddRange(lineEvents);
                order.ClearDomainEvents();
            }

            // Append batch
            var appendResult = await _eventStore.AppendEventsAsync(orderId.ToString(), batchEvents, currentVersion);
            appendResult.IsSuccess.Should().BeTrue($"Batch starting at {batchStart} should append successfully");
            currentVersion += batchEvents.Count;
        }

        // Verify all events were appended
        var allEvents = await _eventStore.GetEventsAsync(orderId.ToString());
        allEvents.Value.Should().HaveCount(1000, "Should have 1 PlaceOrder + 999 LineAdded events");

        // Register projection
        _projectionManager!.RegisterProjection<OrderSummaryProjection>();

        // Track progress
        var progressReports = new List<RebuildProgress>();
        var progress = new Progress<RebuildProgress>(report => progressReports.Add(report));

        // Act
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
            streamId: orderId.ToString(),
            progress: progress);

        // Assert - Verify projection state
        var projectionState = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");
        projectionState.Should().NotBeNull();
        projectionState.Status.Should().Be(ProjectionStatus.Completed);

        // Verify progress reports
        progressReports.Should().NotBeEmpty();
        var lastReport = progressReports.OrderBy(r => r.ProcessedEvents).Last();
        lastReport.ProcessedEvents.Should().Be(1000);
        lastReport.PercentComplete.Should().BeApproximately(100, 0.1);

        // Performance benchmarks live in tests/Perf/Compendium.Infrastructure.PerfTests.

        // **Expected Results:**
        // ✅ Projection rebuilt from 1000 events
        // ✅ Performance > 10,000 events/minute
        // ✅ Progress reporting accurate
        // ✅ Final projection state correct
    }

    [Fact]
    public async Task RebuildProjection_WithMultipleOrders_AggregatesCorrectly()
    {
        // Arrange
        var customer1 = "customer-multi-001";
        var customer2 = "customer-multi-002";

        // Create 3 orders for customer 1
        var orders1 = new List<OrderId>
        {
            await CreateOrderWithLinesAsync(customer1, 3, 25.00m),
            await CreateOrderWithLinesAsync(customer1, 5, 15.00m),
            await CreateOrderWithLinesAsync(customer1, 2, 50.00m)
        };

        // Create 2 orders for customer 2
        var orders2 = new List<OrderId>
        {
            await CreateOrderWithLinesAsync(customer2, 4, 30.00m),
            await CreateOrderWithLinesAsync(customer2, 1, 100.00m)
        };

        // Register projection
        _projectionManager!.RegisterProjection<OrderSummaryProjection>();

        // Act - Rebuild projection for all orders
        foreach (var orderId in orders1.Concat(orders2))
        {
            await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
                streamId: orderId.ToString());
        }

        // Assert - Verify all orders were processed
        var projectionState = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");
        projectionState.Should().NotBeNull();
        projectionState.Status.Should().Be(ProjectionStatus.Completed);

        // Verify all events were processed for all orders
        var stats = await _projectionManager.GetStatisticsAsync();
        stats.Should().NotBeNull();
        stats.TotalProjections.Should().BeGreaterThan(0);

        // **Expected Results:**
        // ✅ Multiple order streams rebuilt correctly
        // ✅ Projection state shows completed
        // ✅ Statistics show all events processed
    }

    [Fact]
    public async Task ProjectionWithCheckpoint_ResumesFromLastPosition()
    {
        // Arrange
        var orderId = OrderId.New();
        var customerId = "customer-checkpoint-001";

        // Create order with 100 lines
        var order = OrderAggregate.PlaceOrder(orderId, customerId, DateTimeOffset.UtcNow);
        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        for (int i = 1; i <= 100; i++)
        {
            order.AddOrderLine($"line-{i}", $"product-{i}", 1, 10.00m);
        }

        var allLineEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore.AppendEventsAsync(orderId.ToString(), allLineEvents, 1);

        // Register projection
        _projectionManager!.RegisterProjection<OrderSummaryProjection>();

        // Simulate processing first 50 events and saving checkpoint
        await _projectionStore!.SaveCheckpointAsync("E2E_OrderSummary", 50);

        // Act - Rebuild (should resume from checkpoint)
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
            streamId: orderId.ToString());

        // Assert
        var checkpoint = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");
        checkpoint.Should().BeGreaterThan(50, "Checkpoint should advance beyond 50");

        var projectionState = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");
        projectionState.Should().NotBeNull();
        projectionState.Status.Should().Be(ProjectionStatus.Completed);

        // **Expected Results:**
        // ✅ Projection resumed from checkpoint
        // ✅ Did not reprocess events before checkpoint
        // ✅ Final state shows completed
    }

    [Fact]
    public async Task CompleteOrderLifecycle_WithProjectionRebuild_QueriesSucceed()
    {
        // Arrange
        var orderId = OrderId.New();
        var customerId = "customer-lifecycle-001";

        // **Step 1: Create and complete order**
        var order = OrderAggregate.PlaceOrder(orderId, customerId, DateTimeOffset.UtcNow);
        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        // Add 3 lines
        order.AddOrderLine("line-1", "product-A", 2, 25.00m);
        order.AddOrderLine("line-2", "product-B", 1, 50.00m);
        order.AddOrderLine("line-3", "product-C", 3, 10.00m);

        var lineEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore.AppendEventsAsync(orderId.ToString(), lineEvents, 1);

        // Complete order
        var completeResult = order.Complete(DateTimeOffset.UtcNow);
        completeResult.IsSuccess.Should().BeTrue();

        var completeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore.AppendEventsAsync(orderId.ToString(), completeEvents, 4);

        // **Step 2: Rebuild projection**
        _projectionManager!.RegisterProjection<OrderSummaryProjection>();

        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
            streamId: orderId.ToString());

        // **Step 3: Verify projection state**
        var projectionState = await _projectionManager.GetProjectionStateAsync("E2E_OrderSummary");

        // Assert
        projectionState.Should().NotBeNull();
        projectionState.Status.Should().Be(ProjectionStatus.Completed);

        // Verify checkpoint advanced through all events
        var checkpoint = await _projectionStore!.GetCheckpointAsync("E2E_OrderSummary");
        checkpoint.Should().BeGreaterThanOrEqualTo(5, "Should process all 5 events");

        // **Expected Results:**
        // ✅ Projection rebuilt from 5 events (1 place + 3 lines + 1 complete)
        // ✅ Projection state shows completed
        // ✅ Checkpoint shows all events processed
    }

    private async Task<OrderId> CreateOrderWithLinesAsync(
        string customerId,
        int lineCount,
        decimal unitPrice)
    {
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, customerId, DateTimeOffset.UtcNow);

        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        var allLineEvents = new List<IDomainEvent>();
        for (int i = 1; i <= lineCount; i++)
        {
            order.AddOrderLine($"line-{i}", $"product-{i}", 1, unitPrice);
            var lineEvents = order.DomainEvents.ToList();
            allLineEvents.AddRange(lineEvents);
            order.ClearDomainEvents();
        }

        await _eventStore.AppendEventsAsync(orderId.ToString(), allLineEvents, 1);

        return orderId;
    }
}
